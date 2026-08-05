using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;
using OpenTavu.Dataverse.AI;

namespace Pl.Lead.Triage
{
    /// <summary>
    /// Module 3 - AI Lead Triage (Path B, anonymous inbound buffer).
    /// Async, Post-Operation on Create of tavu_lead. Runs a deterministic-first,
    /// AI-second pipeline (Option A) and a strict promotion boundary that protects
    /// the clean master Contact/Account DB:
    ///   - deterministic exact matches / junk / duplicates spend ZERO AI tokens;
    ///   - the AI does fuzzy match + real-prospect-vs-noise + field extraction;
    ///   - a brand-new master record is NEVER auto-created here; a real prospect with
    ///     no existing match routes to "Awaiting Human Review" for a one-click Approve.
    /// Never blocks lead creation: any failure degrades to "Manual Review Required".
    /// </summary>
    /// <remarks>
    /// Plugin Registration Tool configuration:
    ///   Message:              Create
    ///   Primary Entity:       tavu_lead
    ///   Stage:                40 (Post-operation)
    ///   Execution Mode:       Asynchronous
    ///   Deployment:           Server
    /// </remarks>
    public class Triage : PluginBase
    {
        // ============================================================
        // SCHEMA CONSTANTS
        // Status-reason and Task Key values are the REAL values read from the
        // opentavu.crm.dynamics.com environment (2026-07-30).
        // ============================================================

        private const string LeadEntity = "tavu_lead";

        // tavu_lead columns (logical names, lowercase)
        private const string LeadSubject        = "tavu_subject";
        private const string LeadSource         = "tavu_source";            // Choice column (bound to the global choice tavu_leadsource); currently unused by triage
        private const string LeadSourceDetails  = "tavu_sourcedetails";     // Multiline: raw message body (AI input), confirmed 2026-07-31
        private const string LeadEmail          = "tavu_email";
        private const string LeadFirstName      = "tavu_firstname";
        private const string LeadLastName       = "tavu_lastname";
        private const string LeadCompanyName    = "tavu_companyname";
        private const string LeadMatchedAccount = "tavu_matchedaccount";    // Lookup -> account
        private const string LeadMatchedContact = "tavu_matchedcontact";    // Lookup -> contact
        private const string LeadPromotedContact = "tavu_promotedcontact"; // Lookup -> contact (the resolved contact)
        private const string LeadAiConfidence   = "tavu_aiconfidencescore"; // Decimal (0-1)
        private const string LeadAiRecommend    = "tavu_airecommendation";  // Multiline
        private const string LeadLastAiDate     = "tavu_lastaiprocessingdate";

        // Standard tables used for matching
        private const string ContactEntity  = "contact";
        private const string AccountEntity  = "account";
        private const string ContactEmail   = "emailaddress1";
        private const string ContactParent  = "parentcustomerid";          // Customer lookup (account or contact)
        private const string ContactFull    = "fullname";
        private const string AccountName    = "name";
        private const string AccountWebsite = "websiteurl";

        // Task Key option value for "Lead Triage" (global choice tavu_aitaskkeyconfig)
        private const int TaskKeyLeadTriage = 576600003;

        // tavu_lead statuscode values (Status Reason). statecode: Active = 0, Inactive = 1.
        private const int StatusNew                 = 576600001; // Active
        private const int StatusAIProcessing        = 576600002; // Active
        private const int StatusAwaitingHumanReview = 576600003; // Active
        private const int StatusManualReviewReq     = 576600004; // Active
        private const int StatusPromotedToContact   = 576600005; // Inactive
        private const int StatusDiscardedAsNoise    = 576600006; // Inactive
        private const int StatusDuplicate           = 576600007; // Inactive
        private const int StatusNotQualified        = 576600008; // Inactive
        private const int StatusStale               = 576600009; // Inactive

        private const int StateActive   = 0;
        private const int StateInactive = 1;

        // Bounds for the candidate lists handed to the AI (cost/latency control).
        private const int MaxCandidateContacts = 25;
        private const int MaxCandidateAccounts = 25;

        // Free / consumer email providers: an exact person match is still valid, but a
        // *domain* match against these must be ignored (gmail.com != a shared employer).
        private static readonly HashSet<string> FreeEmailDomains =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "gmail.com","googlemail.com","hotmail.com","hotmail.co.uk","outlook.com",
                "live.com","msn.com","yahoo.com","yahoo.co.uk","ymail.com","icloud.com",
                "me.com","mac.com","aol.com","protonmail.com","proton.me","gmx.com",
                "gmx.net","mail.com","yandex.com","zoho.com","hey.com","pm.me"
            };

        public Triage() : base(typeof(Triage)) { }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null) throw new ArgumentNullException(nameof(localContext));

            localContext.Trace("Triage: ExecuteInternal entered.");

            var ctx = localContext.PluginExecutionContext;

            if (!string.Equals(ctx.MessageName, "Create", StringComparison.OrdinalIgnoreCase))
            {
                localContext.Trace("Message is '{0}', not Create. Exiting.", ctx.MessageName);
                return;
            }

            if (!(ctx.InputParameters.Contains("Target") && ctx.InputParameters["Target"] is Entity target))
            {
                localContext.Trace("Target missing or not an Entity. Exiting.");
                return;
            }

            if (!string.Equals(target.LogicalName, LeadEntity, StringComparison.Ordinal))
            {
                localContext.Trace("Unexpected entity '{0}'. Exiting.", target.LogicalName);
                return;
            }

            try
            {
                TriageLead(localContext, target.Id);
            }
            catch (Exception ex)
            {
                // Never lose the lead: degrade to Manual Review on any unexpected failure.
                localContext.Trace("Unhandled exception in TriageLead: {0}", ex);
                try { RouteToManualReview(localContext.SystemService, target.Id, "Unhandled error: " + ex.Message); }
                catch (Exception inner) { localContext.Trace("Also failed to route to Manual Review: {0}", inner.Message); }
            }

            localContext.Trace("Triage: ExecuteInternal exiting.");
        }

        // ============================================================
        // Pipeline
        // ============================================================

        private void TriageLead(LocalPluginContext localContext, Guid leadId)
        {
            // Derived/audit writes performed by a possibly low-privilege connection user
            // (the Power Automate flow that dropped the lead) -> SystemService.
            IOrganizationService svc = localContext.SystemService;

            // Read the committed lead (async post-op: record exists). ColumnSet(true) so a
            // still-to-be-confirmed body/source column name can't throw the whole triage;
            // absent columns simply read as empty (see VERIFY notes on the constants).
            Entity lead = svc.Retrieve(LeadEntity, leadId, new ColumnSet(true));

            string email       = (lead.GetAttributeValue<string>(LeadEmail) ?? string.Empty).Trim();
            string firstName   = (lead.GetAttributeValue<string>(LeadFirstName) ?? string.Empty).Trim();
            string lastName    = (lead.GetAttributeValue<string>(LeadLastName) ?? string.Empty).Trim();
            string companyName = (lead.GetAttributeValue<string>(LeadCompanyName) ?? string.Empty).Trim();
            string subject     = (lead.GetAttributeValue<string>(LeadSubject) ?? string.Empty).Trim();
            string body        = (lead.GetAttributeValue<string>(LeadSourceDetails) ?? string.Empty).Trim();

            localContext.Trace("Lead read. email='{0}' company='{1}' first='{2}' last='{3}'",
                email, companyName, firstName, lastName);

            // ---------- 1. DETERMINISTIC PASS (zero AI tokens) ----------

            // 1a. Obvious junk.
            if (IsJunk(email, subject, body))
            {
                localContext.Trace("Deterministic: junk detected. -> Discarded as Noise.");
                Finalize(svc, leadId, StateInactive, StatusDiscardedAsNoise, 0.99m,
                    "Deterministic: recognised automated/no-reply/empty noise. Discarded without AI.");
                return;
            }

            // 1b. Exact email match to an existing active contact -> auto-link + Promoted.
            Entity contact = FindContactByExactEmail(svc, email);
            if (contact != null)
            {
                var update = NewUpdate(leadId);
                var matchRef = new EntityReference(ContactEntity, contact.Id);
                update[LeadMatchedContact] = matchRef;
                // Promoted to Contact by any route fills Promoted Contact, so the field is
                // reliable for reporting (auto-link and human promotion both populate it).
                update[LeadPromotedContact] = matchRef;
                EntityReference acct = GetParentAccount(contact);
                if (acct != null) update[LeadMatchedAccount] = acct;
                SetStatus(update, StateInactive, StatusPromotedToContact);
                StampAi(update, 1.0m,
                    "Deterministic: exact email match to existing contact '"
                    + contact.GetAttributeValue<string>(ContactFull) + "'. Auto-linked (no new record created).");
                localContext.Trace("Deterministic: exact contact email match -> Promoted to Contact.");
                svc.Update(update);
                return;
            }

            // 1c. Duplicate of another OPEN lead with the same email.
            if (!string.IsNullOrEmpty(email) && HasOtherOpenLeadWithEmail(svc, email, leadId))
            {
                localContext.Trace("Deterministic: duplicate open lead with same email. -> Duplicate.");
                Finalize(svc, leadId, StateInactive, StatusDuplicate, 0.95m,
                    "Deterministic: another open lead already exists for this email. Marked as duplicate.");
                return;
            }

            // 1d. Corporate-domain match (non-free provider) -> account known, person still new.
            EntityReference domainAccount = FindAccountByEmailDomain(svc, email);
            if (domainAccount != null)
            {
                var update = NewUpdate(leadId);
                update[LeadMatchedAccount] = domainAccount;
                SetStatus(update, StateActive, StatusAwaitingHumanReview);
                StampAi(update, 0.9m,
                    "Deterministic: email domain matches existing account '" + domainAccount.Name
                    + "'. Recommendation: Promote, create new contact under matched account. Human confirms.");
                localContext.Trace("Deterministic: domain match to account '{0}' -> Awaiting Human Review.", domainAccount.Name);
                svc.Update(update);
                return;
            }

            // ---------- 2. AI PASS (only when deterministic was inconclusive) ----------

            AIResolvedConfig cfg = AIConfigResolver.Resolve(svc, TaskKeyLeadTriage);
            if (!cfg.Usable)
            {
                localContext.Trace("AI config not usable: {0}. -> Manual Review.", cfg.Reason);
                RouteToManualReview(svc, leadId, "AI not available: " + cfg.Reason);
                return;
            }

            // Build bounded candidate lists so the AI can only pick an EXACT existing name.
            var candContacts = LoadCandidateContacts(svc, lastName, email);
            var candAccounts = LoadCandidateAccounts(svc, companyName);
            localContext.Trace("Candidates loaded: {0} contacts, {1} accounts.",
                candContacts.Count, candAccounts.Count);

            string userContent = BuildUserContent(subject, body, email, firstName, lastName,
                companyName, candContacts, candAccounts);

            IAIProvider provider = cfg.UseGateway
                ? new GatewayProvider(cfg.GatewayUrl, cfg.GatewayKey)
                : AIProviderFactory.Create(cfg.ProviderValue);

            AICompletionResult ai = provider.Complete(
                AIConfigResolver.ToRequest(cfg, userContent, jsonResponse: true));

            if (!ai.Success)
            {
                localContext.Trace("AI call failed: {0}. -> Manual Review.", ai.ErrorMessage);
                RouteToManualReview(svc, leadId, "AI call failed: " + ai.ErrorMessage);
                return;
            }

            LeadTriageOutput o = ParseOutput(localContext, ai.Content);
            if (o == null)
            {
                RouteToManualReview(svc, leadId, "Could not parse AI response.");
                return;
            }

            ApplyAiRouting(localContext, svc, leadId, o, cfg, firstName, lastName, companyName,
                candContacts, candAccounts);
        }

        // ============================================================
        // AI routing (Option A promotion boundary)
        // ============================================================

        private void ApplyAiRouting(LocalPluginContext localContext, IOrganizationService svc, Guid leadId,
            LeadTriageOutput o, AIResolvedConfig cfg, string firstName, string lastName, string companyName,
            Dictionary<string, CandidateContact> contacts, Dictionary<string, Guid> accounts)
        {
            var update = NewUpdate(leadId);

            // Extracted fields: fill only when the lead had nothing (never overwrite input).
            if (string.IsNullOrEmpty(firstName) && !string.IsNullOrEmpty(o.FirstName))
                update[LeadFirstName] = Trunc(o.FirstName, 100);
            if (string.IsNullOrEmpty(lastName) && !string.IsNullOrEmpty(o.LastName))
                update[LeadLastName] = Trunc(o.LastName, 100);
            if (string.IsNullOrEmpty(companyName) && !string.IsNullOrEmpty(o.CompanyName))
                update[LeadCompanyName] = Trunc(o.CompanyName, 200);

            bool confident = o.Confidence >= (double)cfg.ConfidenceThreshold;
            int state, status;
            string tag;

            CandidateContact matchedContact = ResolveContact(contacts, o.MatchContactName);
            Guid matchedAccountId = ResolveAccount(accounts, o.MatchAccountName);

            if (matchedContact != null)
            {
                // AI fuzzy match to an EXISTING contact.
                update[LeadMatchedContact] = new EntityReference(ContactEntity, matchedContact.Id);
                if (matchedContact.ParentAccount != null)
                    update[LeadMatchedAccount] = matchedContact.ParentAccount;

                if (confident)
                {
                    state = StateInactive; status = StatusPromotedToContact;
                    // Auto-link resolves the lead, so fill Promoted Contact too (reporting parity
                    // with the human promotion path). Below threshold, leave it for the human.
                    update[LeadPromotedContact] = new EntityReference(ContactEntity, matchedContact.Id);
                    tag = "AI matched existing contact '" + matchedContact.FullName + "' (confident). Auto-linked.";
                }
                else
                {
                    state = StateActive; status = StatusAwaitingHumanReview;
                    tag = "AI suggests existing contact '" + matchedContact.FullName
                        + "' but below threshold. Human confirms the link.";
                }
            }
            else if (matchedAccountId != Guid.Empty)
            {
                // Account matched, but the person is new -> human confirms new contact.
                update[LeadMatchedAccount] = new EntityReference(AccountEntity, matchedAccountId);
                state = StateActive; status = StatusAwaitingHumanReview;
                tag = "AI matched an existing account; contact is new. Recommendation: Promote, create new contact under matched account.";
            }
            else if (IsDiscard(o.Recommendation) || !o.IsRealProspect)
            {
                state = StateInactive; status = StatusDiscardedAsNoise;
                tag = "AI judged this as noise / not a real prospect. Discarded.";
            }
            else if (IsPromoteCreateNew(o.Recommendation) && o.IsRealProspect)
            {
                state = StateActive; status = StatusAwaitingHumanReview;
                tag = "AI judged a real prospect with no existing match. Recommendation: Promote, create new. Human confirms.";
            }
            else
            {
                // Ambiguous / low confidence / explicit "review".
                state = StateActive; status = StatusAwaitingHumanReview;
                tag = "AI could not decide confidently. Routed for human review.";
            }

            SetStatus(update, state, status);
            StampAi(update, (decimal)o.Confidence,
                Compose(o.Recommendation, tag, o.Reasoning));

            localContext.Trace(
                "AI routing done. confidence={0} threshold={1} rec='{2}' contact={3} account={4} -> state={5} status={6}",
                o.Confidence, cfg.ConfidenceThreshold, o.Recommendation,
                matchedContact != null, matchedAccountId != Guid.Empty, state, status);

            svc.Update(update);
        }

        // ============================================================
        // Deterministic helpers
        // ============================================================

        private static bool IsJunk(string email, string subject, string body)
        {
            // Empty everything -> nothing to triage.
            if (string.IsNullOrEmpty(email) && string.IsNullOrEmpty(subject) && string.IsNullOrEmpty(body))
                return true;

            string e = (email ?? string.Empty).ToLowerInvariant();
            string local = e.Contains("@") ? e.Substring(0, e.IndexOf('@')) : e;

            if (local.StartsWith("no-reply") || local.StartsWith("noreply") ||
                local.StartsWith("donotreply") || local.StartsWith("do-not-reply") ||
                local == "mailer-daemon" || local == "postmaster" || local == "bounce" ||
                local.StartsWith("bounces"))
                return true;

            return false;
        }

        private static Entity FindContactByExactEmail(IOrganizationService svc, string email)
        {
            if (string.IsNullOrEmpty(email)) return null;
            var q = new QueryExpression(ContactEntity)
            {
                ColumnSet = new ColumnSet(ContactFull, ContactParent),
                NoLock = true,
                TopCount = 1
            };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            q.Criteria.AddCondition(ContactEmail, ConditionOperator.Equal, email);
            EntityCollection r = svc.RetrieveMultiple(q);
            return r.Entities.Count > 0 ? r.Entities[0] : null;
        }

        private static bool HasOtherOpenLeadWithEmail(IOrganizationService svc, string email, Guid selfId)
        {
            var q = new QueryExpression(LeadEntity)
            {
                ColumnSet = new ColumnSet(false),
                NoLock = true,
                TopCount = 1
            };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            q.Criteria.AddCondition(LeadEmail, ConditionOperator.Equal, email);
            q.Criteria.AddCondition("tavu_leadid", ConditionOperator.NotEqual, selfId);
            return svc.RetrieveMultiple(q).Entities.Count > 0;
        }

        private static EntityReference FindAccountByEmailDomain(IOrganizationService svc, string email)
        {
            string domain = ExtractDomain(email);
            if (string.IsNullOrEmpty(domain) || FreeEmailDomains.Contains(domain)) return null;

            // Prefer an account whose website carries the domain.
            var qa = new QueryExpression(AccountEntity)
            {
                ColumnSet = new ColumnSet(AccountName),
                NoLock = true,
                TopCount = 1
            };
            qa.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            qa.Criteria.AddCondition(AccountWebsite, ConditionOperator.Like, "%" + SanitizeLike(domain) + "%");
            EntityCollection ra = svc.RetrieveMultiple(qa);
            if (ra.Entities.Count > 0)
                return new EntityReference(AccountEntity, ra.Entities[0].Id)
                { Name = ra.Entities[0].GetAttributeValue<string>(AccountName) };

            // Else: an existing contact on that corporate domain -> use its parent account.
            var qc = new QueryExpression(ContactEntity)
            {
                ColumnSet = new ColumnSet(ContactParent),
                NoLock = true,
                TopCount = 1
            };
            qc.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            qc.Criteria.AddCondition(ContactEmail, ConditionOperator.Like, "%@" + SanitizeLike(domain));
            EntityCollection rc = svc.RetrieveMultiple(qc);
            if (rc.Entities.Count > 0)
            {
                EntityReference acct = GetParentAccount(rc.Entities[0]);
                if (acct != null) return acct;
            }
            return null;
        }

        private static EntityReference GetParentAccount(Entity contact)
        {
            var parent = contact.GetAttributeValue<EntityReference>(ContactParent);
            if (parent != null && string.Equals(parent.LogicalName, AccountEntity, StringComparison.Ordinal))
                return parent;
            return null;
        }

        // ============================================================
        // Candidate loading (for the AI)
        // ============================================================

        private Dictionary<string, CandidateContact> LoadCandidateContacts(
            IOrganizationService svc, string lastName, string email)
        {
            var map = new Dictionary<string, CandidateContact>(StringComparer.OrdinalIgnoreCase);
            string domain = ExtractDomain(email);

            var q = new QueryExpression(ContactEntity)
            {
                ColumnSet = new ColumnSet(ContactFull, ContactEmail, ContactParent),
                NoLock = true,
                TopCount = MaxCandidateContacts
            };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);

            var or = new FilterExpression(LogicalOperator.Or);
            bool any = false;
            if (!string.IsNullOrEmpty(lastName))
            {
                or.AddCondition("lastname", ConditionOperator.Like, "%" + SanitizeLike(lastName) + "%");
                any = true;
            }
            if (!string.IsNullOrEmpty(domain) && !FreeEmailDomains.Contains(domain))
            {
                or.AddCondition(ContactEmail, ConditionOperator.Like, "%@" + SanitizeLike(domain));
                any = true;
            }
            if (!any) return map; // nothing to fuzzy-match on
            q.Criteria.AddFilter(or);

            foreach (var e in svc.RetrieveMultiple(q).Entities)
            {
                string name = e.GetAttributeValue<string>(ContactFull);
                if (string.IsNullOrEmpty(name) || map.ContainsKey(name)) continue;
                map[name] = new CandidateContact
                {
                    Id = e.Id,
                    FullName = name,
                    Email = e.GetAttributeValue<string>(ContactEmail),
                    ParentAccount = GetParentAccount(e)
                };
            }
            return map;
        }

        private Dictionary<string, Guid> LoadCandidateAccounts(IOrganizationService svc, string companyName)
        {
            var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(companyName)) return map;

            var q = new QueryExpression(AccountEntity)
            {
                ColumnSet = new ColumnSet(AccountName),
                NoLock = true,
                TopCount = MaxCandidateAccounts
            };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            q.Criteria.AddCondition(AccountName, ConditionOperator.Like, "%" + SanitizeLike(companyName) + "%");

            foreach (var e in svc.RetrieveMultiple(q).Entities)
            {
                string name = e.GetAttributeValue<string>(AccountName);
                if (!string.IsNullOrEmpty(name) && !map.ContainsKey(name)) map[name] = e.Id;
            }
            return map;
        }

        private string BuildUserContent(string subject, string body, string email, string firstName,
            string lastName, string companyName, Dictionary<string, CandidateContact> contacts,
            Dictionary<string, Guid> accounts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("INBOUND LEAD");
            sb.AppendLine("Subject: " + subject);
            sb.AppendLine("From email: " + email);
            sb.AppendLine("Extracted first name: " + firstName);
            sb.AppendLine("Extracted last name: " + lastName);
            sb.AppendLine("Extracted company: " + companyName);
            sb.AppendLine("Message body:");
            sb.AppendLine(body);
            sb.AppendLine();

            sb.AppendLine("CANDIDATE EXISTING CONTACTS (pick an EXACT Name to link, or none):");
            if (contacts.Count == 0) sb.AppendLine("- (none)");
            else foreach (var c in contacts.Values)
                sb.AppendLine("- " + c.FullName + (string.IsNullOrEmpty(c.Email) ? "" : " <" + c.Email + ">"));
            sb.AppendLine();

            sb.AppendLine("CANDIDATE EXISTING ACCOUNTS (pick an EXACT Name to link, or none):");
            if (accounts.Count == 0) sb.AppendLine("- (none)");
            else foreach (var n in accounts.Keys) sb.AppendLine("- " + n);
            sb.AppendLine();

            sb.AppendLine("Return ONLY the JSON object described in the system prompt. Use exact Names "
                + "from the candidate lists above; leave a match field empty if nothing fits. "
                + "Never invent a record. Creating a new record is a human decision.");
            return sb.ToString();
        }

        // ============================================================
        // Manual-review fallback
        // ============================================================

        private void RouteToManualReview(IOrganizationService svc, Guid leadId, string note)
        {
            var update = NewUpdate(leadId);
            SetStatus(update, StateActive, StatusManualReviewReq);
            StampAi(update, 0m, "Manual Review Required. " + note);
            svc.Update(update);
        }

        // ============================================================
        // AI output parsing
        // ============================================================

        private LeadTriageOutput ParseOutput(LocalPluginContext localContext, string content)
        {
            try
            {
                string json = CleanJson(content);
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return (LeadTriageOutput)
                        new DataContractJsonSerializer(typeof(LeadTriageOutput)).ReadObject(ms);
                }
            }
            catch (Exception ex)
            {
                localContext.Trace("Failed to parse AI JSON: {0}. Raw: {1}", ex.Message, content);
                return null;
            }
        }

        private static string CleanJson(string content)
        {
            if (string.IsNullOrEmpty(content)) return "{}";
            string s = content.Trim();
            if (s.StartsWith("```"))
            {
                int first = s.IndexOf('{');
                int last = s.LastIndexOf('}');
                if (first >= 0 && last > first) s = s.Substring(first, last - first + 1);
            }
            return s;
        }

        // ============================================================
        // Small helpers
        // ============================================================

        private static Entity NewUpdate(Guid leadId) { return new Entity(LeadEntity, leadId); }

        private static void SetStatus(Entity update, int state, int status)
        {
            // Dataverse requires statecode + statuscode set together on a state transition.
            update["statecode"] = new OptionSetValue(state);
            update["statuscode"] = new OptionSetValue(status);
        }

        private static void StampAi(Entity update, decimal confidence, string recommendation)
        {
            // Stored as a whole percentage (0-100) so the field/header reads "90", not "0.90".
            // The routing threshold comparison stays on the raw 0-1 value (see ApplyAiRouting);
            // only the persisted/displayed score is scaled here.
            update[LeadAiConfidence] = decimal.Round(confidence * 100m, 0);
            update[LeadAiRecommend] = Trunc(recommendation, 2000);
            update[LeadLastAiDate] = DateTime.UtcNow;
        }

        /// <summary>Sets status + AI stamp for a terminal deterministic outcome, then updates.</summary>
        private static void Finalize(IOrganizationService svc, Guid leadId, int state, int status,
            decimal confidence, string recommendation)
        {
            var update = NewUpdate(leadId);
            SetStatus(update, state, status);
            StampAi(update, confidence, recommendation);
            svc.Update(update);
        }

        private static CandidateContact ResolveContact(Dictionary<string, CandidateContact> map, string proposedName)
        {
            if (string.IsNullOrEmpty(proposedName)) return null;
            CandidateContact c;
            return map.TryGetValue(proposedName.Trim(), out c) ? c : null;
        }

        private static Guid ResolveAccount(Dictionary<string, Guid> map, string proposedName)
        {
            if (string.IsNullOrEmpty(proposedName)) return Guid.Empty;
            Guid id;
            return map.TryGetValue(proposedName.Trim(), out id) ? id : Guid.Empty;
        }

        private static bool IsDiscard(string rec)
        {
            return !string.IsNullOrEmpty(rec) && rec.Trim().ToLowerInvariant() == "discard";
        }

        private static bool IsPromoteCreateNew(string rec)
        {
            if (string.IsNullOrEmpty(rec)) return false;
            string r = rec.Trim().ToLowerInvariant();
            return r == "promote-create-new" || r == "promote" || r == "create-new";
        }

        private static string Compose(string recommendation, string tag, string reasoning)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(recommendation)) sb.Append("[").Append(recommendation).Append("] ");
            sb.Append(tag);
            if (!string.IsNullOrEmpty(reasoning))
            {
                // Avoid a double period when the tag already ends with sentence punctuation.
                string t = (tag ?? string.Empty).TrimEnd();
                bool endsWithPunct = t.Length > 0 && (t.EndsWith(".") || t.EndsWith("!") || t.EndsWith("?"));
                sb.Append(endsWithPunct ? " " : ". ").Append(reasoning);
            }
            return sb.ToString();
        }

        private static string ExtractDomain(string email)
        {
            if (string.IsNullOrEmpty(email)) return null;
            int at = email.IndexOf('@');
            if (at < 0 || at >= email.Length - 1) return null;
            return email.Substring(at + 1).Trim().ToLowerInvariant();
        }

        private static string SanitizeLike(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            // Strip LIKE wildcards / brackets so user text can't broaden or break the query.
            return value.Replace("%", string.Empty)
                        .Replace("_", string.Empty)
                        .Replace("[", string.Empty)
                        .Replace("]", string.Empty)
                        .Trim();
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max);
        }

        // ============================================================
        // In-memory candidate + AI output contract
        // ============================================================

        private sealed class CandidateContact
        {
            public Guid Id;
            public string FullName;
            public string Email;
            public EntityReference ParentAccount;
        }

        [DataContract]
        private sealed class LeadTriageOutput
        {
            [DataMember(Name = "matchContactName")] public string MatchContactName { get; set; }
            [DataMember(Name = "matchAccountName")] public string MatchAccountName { get; set; }
            [DataMember(Name = "recommendation")]   public string Recommendation { get; set; }
            [DataMember(Name = "isRealProspect")]   public bool   IsRealProspect { get; set; }
            [DataMember(Name = "firstName")]        public string FirstName { get; set; }
            [DataMember(Name = "lastName")]         public string LastName { get; set; }
            [DataMember(Name = "companyName")]      public string CompanyName { get; set; }
            [DataMember(Name = "confidence")]       public double Confidence { get; set; }
            [DataMember(Name = "reasoning")]        public string Reasoning { get; set; }
        }
    }
}
