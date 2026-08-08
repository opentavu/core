using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;
using OpenTavu.Dataverse.AI;

namespace Pl.Meeting.Capture
{
    /// <summary>
    /// Module 3, Part B (Activity Capture). Async Post-Operation on Create of tavu_meeting.
    /// A connector (Teams, note-taker, etc.) or a human paste creates a tavu_meeting with the
    /// raw transcript; this plugin runs the AI extraction: a summary, a discovery extract, an
    /// attendee-to-contact/account match, and a suggested opportunity (from a candidate list).
    /// It does NOT associate the meeting to an opportunity, consolidate discovery, or draft the
    /// follow-up email; those are the human step (tavu_AssociateMeeting Custom API).
    /// Never blocks capture: any failure degrades to Manual Review Required.
    /// </summary>
    /// <remarks>
    /// Plugin Registration Tool configuration:
    ///   Message:              Create
    ///   Primary Entity:       tavu_meeting
    ///   Stage:                40 (Post-operation)
    ///   Execution Mode:       Asynchronous
    ///   Deployment:           Server
    /// </remarks>
    public class MeetingCapture : PluginBase
    {
        // ============================================================
        // SCHEMA CONSTANTS (verified against the built tavu_meeting activity table, 2026-08-05)
        // ============================================================

        private const string MeetingEntity = "tavu_meeting";

        // tavu_meeting columns (activity table)
        private const string MSubject      = "subject";                   // activity primary
        private const string MSource       = "tavu_source";               // Choice (provider)
        private const string MTranscript   = "tavu_transcript";           // Multiline (AI input)
        private const string MAttendees    = "tavu_attendees";            // Multiline (raw emails/names)
        private const string MAccount      = "tavu_account";              // Lookup -> account
        private const string MContact      = "tavu_contact";              // Lookup -> contact
        private const string MSuggestedOpp = "tavu_suggestedopportunity"; // Lookup -> tavu_opportunity
        private const string MSummary      = "tavu_summary";              // Multiline (AI)
        private const string MDiscovery    = "tavu_discoveryextract";     // Multiline (AI)
        private const string MConfidence   = "tavu_aiconfidence";         // Decimal 0-100 (note: NOT tavu_aiconfidencescore)
        private const string MLastAiDate   = "tavu_lastaiprocessingdate"; // DateTime

        // Prospect fields: the account/contact details the AI extracts from the transcript, used to
        // auto-provision a new contact + account when nothing matched (the human still clicks
        // Create Opportunity, which reviews and creates them). Only stamped when no contact matched.
        private const string MProspectCompany = "tavu_prospectcompanyname"; // Text
        private const string MProspectFirst   = "tavu_prospectfirstname";   // Text
        private const string MProspectLast    = "tavu_prospectlastname";    // Text
        private const string MProspectEmail   = "tavu_prospectemail";       // Text
        private const string MProspectPhone   = "tavu_prospectphone";       // Text

        // Standard tables used for matching
        private const string ContactEntity = "contact";
        private const string AccountEntity = "account";
        private const string ContactEmail  = "emailaddress1";
        private const string ContactParent = "parentcustomerid";
        private const string ContactFull   = "fullname";
        private const string AccountName   = "name";

        // Internal users (sellers/staff). They are excluded from prospect-contact matching so the
        // meeting's Contact resolves to the CLIENT, never to the internal person who ran the call.
        private const string SystemUserEntity = "systemuser";
        private const string SUInternalEmail  = "internalemailaddress";

        // Opportunity (candidate source). Primary display is tavu_topic (see tavu_proposal_form.js).
        private const string OppEntity  = "tavu_opportunity";
        private const string OppTopic   = "tavu_topic";      // opportunity title
        private const string OppAccount = "tavu_account";    // typed lookup on opp
        private const string OppContact = "tavu_contact";    // typed lookup on opp

        // Task Key option value for "Meeting Capture" in the tavu_aitaskkeyconfig global choice.
        // VERIFY: add "Meeting Capture" to that choice and set this to the assigned integer
        // (likely 576600004, after Lead Triage = 576600003).
        private const int TaskKeyMeetingCapture = 576600004;

        // tavu_meeting statuscode (Status Reason) values, and activity statecode.
        private const int StatusCaptured     = 576600001; // Open
        private const int StatusAIProcessing = 576600002; // Open
        private const int StatusProcessed    = 576600003; // Open (awaiting human association)
        private const int StatusManualReview = 576600004; // Open
        private const int StatusReviewed     = 576600005; // Completed
        private const int StatusDiscarded    = 576600006; // Canceled

        private const int StateOpen      = 0;
        private const int StateCompleted = 1;
        private const int StateCanceled  = 2;

        private const int MaxCandidateOpps = 25;

        private static readonly Regex EmailRegex =
            new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

        public MeetingCapture() : base(typeof(MeetingCapture)) { }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null) throw new ArgumentNullException(nameof(localContext));
            var ctx = localContext.PluginExecutionContext;
            localContext.Trace("MeetingCapture: ExecuteInternal entered.");

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
            if (!string.Equals(target.LogicalName, MeetingEntity, StringComparison.Ordinal))
            {
                localContext.Trace("Unexpected entity '{0}'. Exiting.", target.LogicalName);
                return;
            }

            try
            {
                CaptureMeeting(localContext, target.Id);
            }
            catch (Exception ex)
            {
                localContext.Trace("Unhandled exception in CaptureMeeting: {0}", ex);
                try { RouteToManualReview(localContext.SystemService, target.Id, "Unhandled error: " + ex.Message); }
                catch (Exception inner) { localContext.Trace("Also failed to route to Manual Review: {0}", inner.Message); }
            }

            localContext.Trace("MeetingCapture: ExecuteInternal exiting.");
        }

        private void CaptureMeeting(LocalPluginContext localContext, Guid meetingId)
        {
            // Derived/audit writes performed by a low-privilege connection user -> SystemService.
            IOrganizationService svc = localContext.SystemService;

            Entity meeting = svc.Retrieve(MeetingEntity, meetingId, new ColumnSet(true));

            string subject    = (meeting.GetAttributeValue<string>(MSubject) ?? string.Empty).Trim();
            string transcript = (meeting.GetAttributeValue<string>(MTranscript) ?? string.Empty).Trim();
            string attendees  = (meeting.GetAttributeValue<string>(MAttendees) ?? string.Empty).Trim();

            localContext.Trace("Meeting read. subject='{0}' transcriptLen={1}", subject, transcript.Length);

            if (transcript.Length == 0)
            {
                localContext.Trace("No transcript. Routing to Manual Review.");
                RouteToManualReview(svc, meetingId, "No transcript to process.");
                return;
            }

            // --- Attendee match (fill contact/account if not already set by the connector) ---
            // Internal (seller/staff) domains, so the meeting's Contact resolves to the CLIENT, not the host.
            HashSet<string> internalDomains = GetInternalDomains(svc);
            localContext.Trace("Internal domains loaded: {0}.", internalDomains.Count);
            Entity matchedContact = ResolveContactFromAttendees(svc, attendees, internalDomains);
            EntityReference accountRef = meeting.GetAttributeValue<EntityReference>(MAccount);
            EntityReference contactRef = meeting.GetAttributeValue<EntityReference>(MContact);
            if (contactRef == null && matchedContact != null)
                contactRef = new EntityReference(ContactEntity, matchedContact.Id);
            if (accountRef == null && matchedContact != null)
                accountRef = GetParentAccount(matchedContact);

            // Authoritative CLIENT facts derived from the external attendee's email domain. Speech-to-text
            // mangles company names (e.g. "Seti" -> "Ceti"); the email domain is the source of truth. We
            // hand these to the AI as ground truth AND stamp them, so the company/email never depend on
            // what the transcript sounded like.
            string clientEmail   = FirstExternalAttendeeEmail(attendees, internalDomains);
            string clientCompany = CompanyFromDomain(clientEmail);

            // --- Resolve AI config; degrade to Manual Review if unusable ---
            AIResolvedConfig cfg = AIConfigResolver.Resolve(svc, TaskKeyMeetingCapture);
            if (!cfg.Usable)
            {
                localContext.Trace("AI config not usable: {0}. -> Manual Review.", cfg.Reason);
                RouteToManualReview(svc, meetingId, "AI not available: " + cfg.Reason);
                return;
            }

            // --- Candidate open opportunities for the matched account/contact ---
            var candOpps = LoadCandidateOpps(svc,
                accountRef != null ? accountRef.Id : Guid.Empty,
                contactRef != null ? contactRef.Id : Guid.Empty);
            localContext.Trace("Candidate opps loaded: {0}.", candOpps.Count);

            string userContent = BuildUserContent(subject, attendees, transcript, candOpps, clientEmail, clientCompany);

            IAIProvider provider = cfg.UseGateway
                ? new GatewayProvider(cfg.GatewayUrl, cfg.GatewayKey)
                : AIProviderFactory.Create(cfg.ProviderValue);

            AICompletionResult ai = provider.Complete(
                AIConfigResolver.ToRequest(cfg, userContent, jsonResponse: true));

            if (!ai.Success)
            {
                localContext.Trace("AI call failed: {0}. -> Manual Review.", ai.ErrorMessage);
                RouteToManualReview(svc, meetingId, "AI call failed: " + ai.ErrorMessage);
                return;
            }

            MeetingCaptureOutput o = ParseOutput(localContext, ai.Content);
            if (o == null)
            {
                RouteToManualReview(svc, meetingId, "Could not parse AI response.");
                return;
            }

            // No external attendee matched a contact: try the AI-identified prospect email — the
            // client may already exist as a contact. Only if that also misses do we stamp the prospect
            // details for later provisioning (below). This keeps the Contact on the CLIENT, not the seller.
            if (contactRef == null && !string.IsNullOrEmpty(o.ProspectEmail))
            {
                Entity byProspect = FindContactByEmail(svc, o.ProspectEmail.Trim());
                if (byProspect != null)
                {
                    contactRef = new EntityReference(ContactEntity, byProspect.Id);
                    if (accountRef == null) accountRef = GetParentAccount(byProspect);
                }
            }

            // --- Build the update ---
            var update = NewUpdate(meetingId);
            if (contactRef != null) update[MContact] = contactRef;
            if (accountRef != null) update[MAccount] = accountRef;

            // Suggested opportunity only if the AI name maps to a real candidate (hallucination guard).
            Guid suggestedId = ResolveOpp(candOpps, o.SuggestedOpportunityName);
            if (suggestedId != Guid.Empty)
                update[MSuggestedOpp] = new EntityReference(OppEntity, suggestedId);

            update[MSummary]   = Trunc(o.Summary, 4000);
            update[MDiscovery] = Trunc(o.DiscoveryExtract, 4000);

            // No existing contact matched: stamp the prospect details so the human can auto-provision a
            // contact (and account) from the call on Create Opportunity. Company/email come from the
            // attendee email domain (authoritative) when available, falling back to the AI's values.
            if (contactRef == null)
                StampProspect(update, o, clientEmail, clientCompany);

            StampAi(update, (decimal)o.Confidence);
            SetStatus(update, StateOpen, StatusProcessed);

            localContext.Trace(
                "Capture done. confidence={0} suggestedOpp={1} contact={2} account={3} -> Processed.",
                o.Confidence, suggestedId != Guid.Empty, contactRef != null, accountRef != null);

            svc.Update(update);

            // Set the meeting owner to the organizer (the internal host on the call), not the gateway
            // app user that created the record. Best-effort: never fail capture over ownership.
            TryAssignOwnerToOrganizer(localContext, svc, meetingId, attendees, internalDomains);
        }

        // ============================================================
        // Matching helpers
        // ============================================================

        /// <summary>First internal (staff/host) attendee email — used to set the meeting owner to the organizer.</summary>
        private static string FirstInternalAttendeeEmail(string attendees, HashSet<string> internalDomains)
        {
            if (string.IsNullOrEmpty(attendees)) return null;
            foreach (Match m in EmailRegex.Matches(attendees))
            {
                string email = m.Value.Trim();
                if (IsInternalEmail(email, internalDomains)) return email;
            }
            return null;
        }

        private static Guid FindSystemUserIdByEmail(IOrganizationService svc, string email)
        {
            if (string.IsNullOrEmpty(email)) return Guid.Empty;
            var q = new QueryExpression(SystemUserEntity)
            {
                ColumnSet = new ColumnSet(false),
                NoLock = true,
                TopCount = 1
            };
            q.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
            var byEmail = new FilterExpression(LogicalOperator.Or);
            byEmail.AddCondition(SUInternalEmail, ConditionOperator.Equal, email);
            byEmail.AddCondition("domainname", ConditionOperator.Equal, email);
            q.Criteria.AddFilter(byEmail);
            var r = svc.RetrieveMultiple(q);
            return r.Entities.Count > 0 ? r.Entities[0].Id : Guid.Empty;
        }

        private static void TryAssignOwnerToOrganizer(LocalPluginContext lc, IOrganizationService svc,
            Guid meetingId, string attendees, HashSet<string> internalDomains)
        {
            try
            {
                string hostEmail = FirstInternalAttendeeEmail(attendees, internalDomains);
                if (string.IsNullOrEmpty(hostEmail)) return;
                Guid suId = FindSystemUserIdByEmail(svc, hostEmail);
                if (suId == Guid.Empty) return;
                var assign = new Entity(MeetingEntity, meetingId);
                assign["ownerid"] = new EntityReference("systemuser", suId);
                svc.Update(assign);
                lc.Trace("Owner set to organizer {0}.", hostEmail);
            }
            catch (Exception ex)
            {
                lc.Trace("Owner assign skipped (non-fatal): {0}", ex.Message);
            }
        }

        private static Entity ResolveContactFromAttendees(IOrganizationService svc, string attendees,
            HashSet<string> internalDomains)
        {
            if (string.IsNullOrEmpty(attendees)) return null;
            foreach (Match m in EmailRegex.Matches(attendees))
            {
                string email = m.Value.Trim();
                // Skip internal staff (the seller who ran the call): the prospect is the external
                // attendee, who usually is NOT yet a contact. Matching the seller (who IS in the CRM)
                // would wrongly bind them and suppress the AI's prospect extraction.
                if (IsInternalEmail(email, internalDomains)) continue;
                Entity c = FindContactByEmail(svc, email);
                if (c != null) return c; // first resolvable EXTERNAL attendee is the primary
            }
            return null;
        }

        /// <summary>First external (non-staff) attendee email, used to derive the client company + prospect email.</summary>
        private static string FirstExternalAttendeeEmail(string attendees, HashSet<string> internalDomains)
        {
            if (string.IsNullOrEmpty(attendees)) return null;
            foreach (Match m in EmailRegex.Matches(attendees))
            {
                string email = m.Value.Trim();
                if (!IsInternalEmail(email, internalDomains)) return email;
            }
            return null;
        }

        /// <summary>
        /// Domains of all enabled internal users (sellers/staff), used to tell the client apart from the
        /// host. Robust by DOMAIN (not exact email) so aliases and a blank internalemailaddress still work.
        /// If systemuser can't be read, returns empty (no exclusion) rather than throwing.
        /// </summary>
        private static HashSet<string> GetInternalDomains(IOrganizationService svc)
        {
            var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var q = new QueryExpression(SystemUserEntity)
                {
                    ColumnSet = new ColumnSet(SUInternalEmail, "domainname"),
                    NoLock = true
                };
                q.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
                foreach (var u in svc.RetrieveMultiple(q).Entities)
                {
                    AddDomain(domains, u.GetAttributeValue<string>(SUInternalEmail));
                    AddDomain(domains, u.GetAttributeValue<string>("domainname"));
                }
            }
            catch { /* systemuser not readable: fall back to no domain exclusion */ }
            return domains;
        }

        private static void AddDomain(HashSet<string> set, string email)
        {
            string d = DomainOf(email);
            if (!string.IsNullOrEmpty(d)) set.Add(d);
        }

        private static string DomainOf(string email)
        {
            if (string.IsNullOrEmpty(email)) return null;
            int at = email.IndexOf('@');
            return (at >= 0 && at < email.Length - 1) ? email.Substring(at + 1).Trim() : null;
        }

        private static bool IsInternalEmail(string email, HashSet<string> internalDomains)
        {
            if (internalDomains == null || internalDomains.Count == 0) return false;
            string d = DomainOf(email);
            return d != null && internalDomains.Contains(d);
        }

        /// <summary>Client company from an email domain's second-level label: "x@seti.com.co" -> "Seti".</summary>
        private static string CompanyFromDomain(string email)
        {
            string domain = DomainOf(email);
            if (string.IsNullOrEmpty(domain)) return null;
            string label = domain.Split('.')[0];
            if (string.IsNullOrEmpty(label)) return null;
            return char.ToUpperInvariant(label[0]) + (label.Length > 1 ? label.Substring(1) : string.Empty);
        }

        private static Entity FindContactByEmail(IOrganizationService svc, string email)
        {
            if (string.IsNullOrEmpty(email)) return null;
            var q = new QueryExpression(ContactEntity)
            {
                ColumnSet = new ColumnSet(ContactFull, ContactParent),
                NoLock = true,
                TopCount = 1
            };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            q.Criteria.AddCondition(ContactEmail, ConditionOperator.Equal, email);
            EntityCollection r = svc.RetrieveMultiple(q);
            return r.Entities.Count > 0 ? r.Entities[0] : null;
        }

        private static EntityReference GetParentAccount(Entity contact)
        {
            var parent = contact.GetAttributeValue<EntityReference>(ContactParent);
            if (parent != null && string.Equals(parent.LogicalName, AccountEntity, StringComparison.Ordinal))
                return parent;
            return null;
        }

        private Dictionary<string, Guid> LoadCandidateOpps(IOrganizationService svc, Guid accountId, Guid contactId)
        {
            var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            if (accountId != Guid.Empty) AddOpps(svc, OppAccount, accountId, map);
            if (contactId != Guid.Empty) AddOpps(svc, OppContact, contactId, map);
            return map;
        }

        private static void AddOpps(IOrganizationService svc, string linkField, Guid id, Dictionary<string, Guid> map)
        {
            var q = new QueryExpression(OppEntity)
            {
                ColumnSet = new ColumnSet(OppTopic),
                NoLock = true,
                TopCount = MaxCandidateOpps
            };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // Open
            q.Criteria.AddCondition(linkField, ConditionOperator.Equal, id);
            foreach (var e in svc.RetrieveMultiple(q).Entities)
            {
                string name = e.GetAttributeValue<string>(OppTopic);
                if (!string.IsNullOrEmpty(name) && !map.ContainsKey(name)) map[name] = e.Id;
            }
        }

        private string BuildUserContent(string subject, string attendees, string transcript,
            Dictionary<string, Guid> candOpps, string clientEmail, string clientCompany)
        {
            var sb = new StringBuilder();
            sb.AppendLine("MEETING");
            sb.AppendLine("Subject: " + subject);
            sb.AppendLine("Attendees: " + attendees);
            sb.AppendLine();
            if (!string.IsNullOrEmpty(clientCompany) || !string.IsNullOrEmpty(clientEmail))
            {
                sb.AppendLine("CLIENT FACTS (authoritative, derived from the external attendee's email "
                    + "domain). Use these EXACT spellings for the client company and email EVERYWHERE in "
                    + "your output (summary, discoveryExtract, prospect fields). They OVERRIDE any company "
                    + "name misheard/mis-transcribed in the transcript:");
                if (!string.IsNullOrEmpty(clientCompany)) sb.AppendLine("- Client company: " + clientCompany);
                if (!string.IsNullOrEmpty(clientEmail)) sb.AppendLine("- Client contact email: " + clientEmail);
                sb.AppendLine();
            }
            sb.AppendLine("CANDIDATE OPEN OPPORTUNITIES (pick an EXACT Name to suggest, or none):");
            if (candOpps.Count == 0) sb.AppendLine("- (none)");
            else foreach (var n in candOpps.Keys) sb.AppendLine("- " + n);
            sb.AppendLine();
            sb.AppendLine("TRANSCRIPT:");
            sb.AppendLine(transcript);
            sb.AppendLine();
            sb.AppendLine("Return ONLY the JSON object described in the system prompt. Suggest an "
                + "opportunity only by EXACT Name from the candidate list; leave it empty if none fits. "
                + "Never invent an opportunity.");
            sb.AppendLine("Also fill the prospect fields (prospectCompanyName, prospectFirstName, "
                + "prospectLastName, prospectEmail, prospectPhone) from the transcript and attendees so a "
                + "missing account/contact can be created later. Leave any prospect field empty if the "
                + "transcript does not state it; never guess an email or phone.");
            return sb.ToString();
        }

        // ============================================================
        // Manual-review fallback
        // ============================================================

        private void RouteToManualReview(IOrganizationService svc, Guid meetingId, string note)
        {
            var update = NewUpdate(meetingId);
            SetStatus(update, StateOpen, StatusManualReview);
            update[MSummary] = Trunc("Manual review required. " + note, 4000);
            update[MConfidence] = 0m;
            update[MLastAiDate] = DateTime.UtcNow;
            svc.Update(update);
        }

        // ============================================================
        // AI output parsing
        // ============================================================

        // Tolerant parse. The model sometimes returns discoveryExtract as a nested object
        // (needs/budget/timeline/...) instead of a plain string; a strict DataContract parse
        // fails on that. We read the JSON into an XML DOM (sandbox-safe, via JsonReaderWriterFactory)
        // and pull fields by name, flattening any object/array value to readable text.
        private MeetingCaptureOutput ParseOutput(LocalPluginContext localContext, string content)
        {
            try
            {
                string json = CleanJson(content);
                var doc = new System.Xml.XmlDocument();
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(
                    Encoding.UTF8.GetBytes(json), System.Xml.XmlDictionaryReaderQuotas.Max))
                {
                    doc.Load(reader);
                }

                System.Xml.XmlNode root = doc.DocumentElement;
                if (root == null) return null;

                var o = new MeetingCaptureOutput();
                foreach (System.Xml.XmlNode child in root.ChildNodes)
                {
                    switch (child.LocalName)
                    {
                        case "summary": o.Summary = child.InnerText; break;
                        case "discoveryExtract": o.DiscoveryExtract = FlattenJsonNode(child); break;
                        case "suggestedOpportunityName": o.SuggestedOpportunityName = child.InnerText; break;
                        case "reasoning": o.Reasoning = child.InnerText; break;
                        case "prospectCompanyName": o.ProspectCompanyName = child.InnerText; break;
                        case "prospectFirstName": o.ProspectFirstName = child.InnerText; break;
                        case "prospectLastName": o.ProspectLastName = child.InnerText; break;
                        case "prospectEmail": o.ProspectEmail = child.InnerText; break;
                        case "prospectPhone": o.ProspectPhone = child.InnerText; break;
                        case "confidence":
                            double c;
                            o.Confidence = double.TryParse(child.InnerText, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out c) ? c : 0.0;
                            break;
                    }
                }
                return o;
            }
            catch (Exception ex)
            {
                localContext.Trace("Failed to parse AI JSON: {0}. Raw: {1}", ex.Message, content);
                return null;
            }
        }

        /// <summary>
        /// Returns readable text for a JSON node that may be a plain string, an object, or an array.
        /// Objects become "key: value" lines; arrays become "- value" lines; scalars pass through.
        /// </summary>
        private static string FlattenJsonNode(System.Xml.XmlNode node)
        {
            if (node == null) return null;
            string type = node.Attributes?["type"]?.Value;
            if (type == "object" || type == "array")
            {
                var sb = new StringBuilder();
                foreach (System.Xml.XmlNode child in node.ChildNodes)
                {
                    string val = (child.InnerText ?? string.Empty).Trim();
                    if (val.Length == 0) continue;
                    if (type == "object")
                        sb.AppendLine(DecodeJsonName(child.LocalName) + ": " + val);
                    else
                        sb.AppendLine("- " + val);
                }
                return sb.ToString().Trim();
            }
            return node.InnerText;
        }

        /// <summary>JsonReaderWriterFactory encodes non-XML name chars as _xNNNN_; decode the common space.</summary>
        private static string DecodeJsonName(string name)
        {
            return string.IsNullOrEmpty(name) ? name : name.Replace("_x0020_", " ");
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

        private static Entity NewUpdate(Guid meetingId) { return new Entity(MeetingEntity, meetingId); }

        private static void SetStatus(Entity update, int state, int status)
        {
            update["statecode"] = new OptionSetValue(state);
            update["statuscode"] = new OptionSetValue(status);
        }

        private static void StampAi(Entity update, decimal confidence)
        {
            // Stored as a whole percentage (0-100), consistent with lead/case.
            update[MConfidence] = decimal.Round(confidence * 100m, 0);
            update[MLastAiDate] = DateTime.UtcNow;
        }

        // Writes the AI-extracted prospect details (only the non-empty ones) so the human can
        // auto-provision a contact + account from the call when nothing matched.
        private static void StampProspect(Entity update, MeetingCaptureOutput o,
            string clientEmail, string clientCompany)
        {
            // Company + email prefer the authoritative attendee-domain values over the AI's (which can
            // mishear the company name); name/phone come from the AI/transcript.
            string company = !string.IsNullOrEmpty(clientCompany) ? clientCompany : o.ProspectCompanyName;
            string email   = !string.IsNullOrEmpty(clientEmail)   ? clientEmail   : o.ProspectEmail;
            SetIfPresent(update, MProspectCompany, company,              200);
            SetIfPresent(update, MProspectFirst,   o.ProspectFirstName,  100);
            SetIfPresent(update, MProspectLast,    o.ProspectLastName,   100);
            SetIfPresent(update, MProspectEmail,   email,                100);
            SetIfPresent(update, MProspectPhone,   o.ProspectPhone,      50);
        }

        private static void SetIfPresent(Entity e, string attr, string value, int max)
        {
            string v = (value ?? string.Empty).Trim();
            if (v.Length > 0) e[attr] = Trunc(v, max);
        }

        private static Guid ResolveOpp(Dictionary<string, Guid> map, string proposedName)
        {
            if (string.IsNullOrEmpty(proposedName)) return Guid.Empty;
            Guid id;
            return map.TryGetValue(proposedName.Trim(), out id) ? id : Guid.Empty;
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max);
        }

        // ============================================================
        // AI output contract
        // ============================================================

        [DataContract]
        private sealed class MeetingCaptureOutput
        {
            [DataMember(Name = "summary")]                 public string Summary { get; set; }
            [DataMember(Name = "discoveryExtract")]        public string DiscoveryExtract { get; set; }
            [DataMember(Name = "suggestedOpportunityName")] public string SuggestedOpportunityName { get; set; }
            [DataMember(Name = "confidence")]              public double Confidence { get; set; }
            [DataMember(Name = "reasoning")]               public string Reasoning { get; set; }
            [DataMember(Name = "prospectCompanyName")]     public string ProspectCompanyName { get; set; }
            [DataMember(Name = "prospectFirstName")]       public string ProspectFirstName { get; set; }
            [DataMember(Name = "prospectLastName")]        public string ProspectLastName { get; set; }
            [DataMember(Name = "prospectEmail")]           public string ProspectEmail { get; set; }
            [DataMember(Name = "prospectPhone")]           public string ProspectPhone { get; set; }
        }
    }
}
