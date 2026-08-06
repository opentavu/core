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

namespace Pl.Meeting.Associate
{
    /// <summary>
    /// Custom API tavu_AssociateMeeting. Module 3, Part B (Activity Capture), the human step.
    /// The capture plugin (Pl.Meeting.Capture) only extracts and suggests; this API is the
    /// deliberate human action that commits the meeting to an opportunity and enriches the deal.
    ///
    /// It does three things, in order:
    ///   1. Resolve the target opportunity: an explicit OpportunityId, OR create a new one from
    ///      the meeting's matched account/contact, OR accept the AI's suggested opportunity.
    ///   2. Associate the meeting to that opportunity: set regardingobjectid (drives the native
    ///      timeline) AND the typed tavu_opportunity lookup (for reporting), and complete the
    ///      activity as Reviewed.
    ///   3. Best-effort: consolidate the discovery extracts of ALL that opportunity's meetings
    ///      into the opportunity's discovery notes via one AI call. Gated by a System Settings
    ///      flag; never blocks the association if AI is unavailable.
    ///
    /// The follow-up draft email is a SEPARATE concern (a later Custom API,
    /// tavu_BuildMeetingEmailDraft, mirroring the proposal email draft over the gateway).
    ///
    /// Association writes use UserService (the human is accountable). Config reads and the derived
    /// discovery-notes write use SystemService (config tables + derived field the user cannot own).
    /// </summary>
    /// <remarks>
    /// Registered as the plugin type of the Custom API tavu_AssociateMeeting (global/unbound):
    ///   Request:  MeetingId [String, required], OpportunityId [String, optional],
    ///             CreateNewOpportunity [Boolean, optional], OpportunityTopic [String, optional]
    ///   Response: OpportunityId [String], DiscoveryConsolidated [Boolean]
    /// No SDK message step; the Custom API message is the trigger.
    /// </remarks>
    public class AssociateMeeting : PluginBase
    {
        // ===== Custom API parameters =====
        private const string InMeetingId    = "MeetingId";
        private const string InOpportunityId = "OpportunityId";
        private const string InCreateNewOpp  = "CreateNewOpportunity";
        private const string InOppTopic      = "OpportunityTopic";
        private const string OutOpportunityId    = "OpportunityId";
        private const string OutDiscoveryConsolidated = "DiscoveryConsolidated";

        // ===== tavu_meeting (activity) =====
        private const string MeetingEntity = "tavu_meeting";
        private const string MSubject      = "subject";
        private const string MAccount      = "tavu_account";
        private const string MContact      = "tavu_contact";
        private const string MSuggestedOpp = "tavu_suggestedopportunity";
        private const string MDiscovery    = "tavu_discoveryextract";
        private const string MOpportunity  = "tavu_opportunity";  // typed lookup (reporting)
        private const string MRegarding    = "regardingobjectid"; // polymorphic (timeline)

        // tavu_meeting statuscode + activity statecode (from Pl.Meeting.Capture)
        private const int StatusReviewed  = 576600005; // Completed
        private const int StateCompleted  = 1;

        // ===== tavu_opportunity =====
        private const string OppEntity   = "tavu_opportunity";
        private const string OppTopic    = "tavu_topic";          // title  (VERIFY)
        private const string OppCustomer = "tavu_customer";       // polymorphic customer (VERIFY)
        private const string OppDiscovery = "tavu_discoverynotes"; // consolidated discovery (VERIFY)

        // ===== standard =====
        private const string AccountEntity = "account";
        private const string ContactEntity = "contact";

        // ===== System Settings flag (gate consolidation) =====
        private const string SettingsEntity     = "tavu_systemsettings";
        private const string SettingConsolidate = "tavu_meetingconsolidateddiscovery"; // Yes/No (VERIFY)

        // Reuse the Meeting Capture AI task config + JSON contract for consolidation.
        private const int TaskKeyMeetingCapture = 576600004; // VERIFY (matches Pl.Meeting.Capture)

        private const int MaxMeetingsToConsolidate = 50;

        public AssociateMeeting() : base(typeof(AssociateMeeting)) { }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null) throw new ArgumentNullException(nameof(localContext));
            var ctx = localContext.PluginExecutionContext;
            localContext.Trace("AssociateMeeting: entered. Message={0}.", ctx.MessageName);

            // ----- inputs -----
            Guid meetingId = ParseRequiredGuid(ctx, InMeetingId, "MeetingId");
            Guid oppIdInput = ParseOptionalGuid(ctx, InOpportunityId);
            bool createNew = ParseOptionalBool(ctx, InCreateNewOpp);
            string oppTopicInput = ParseOptionalString(ctx, InOppTopic);

            // The human is acting: association writes respect their privileges.
            IOrganizationService userSvc = localContext.UserService;

            Entity meeting = userSvc.Retrieve(MeetingEntity, meetingId, new ColumnSet(
                MSubject, MAccount, MContact, MSuggestedOpp, MDiscovery));

            // ----- 1. resolve the target opportunity -----
            Guid oppId = ResolveOpportunity(localContext, userSvc, meeting, oppIdInput, createNew, oppTopicInput);

            // ----- 2. associate + complete the meeting -----
            var update = new Entity(MeetingEntity, meetingId);
            update[MOpportunity] = new EntityReference(OppEntity, oppId);
            update[MRegarding]   = new EntityReference(OppEntity, oppId);
            update["statecode"]  = new OptionSetValue(StateCompleted);
            update["statuscode"] = new OptionSetValue(StatusReviewed);
            userSvc.Update(update);
            localContext.Trace("Meeting {0} associated to opportunity {1} and marked Reviewed.", meetingId, oppId);

            // ----- 3. best-effort discovery consolidation -----
            bool consolidated = false;
            try
            {
                consolidated = MaybeConsolidateDiscovery(localContext, oppId);
            }
            catch (Exception ex)
            {
                // Consolidation is an enrichment; never fail the association because of it.
                localContext.Trace("Discovery consolidation skipped (non-fatal): {0}", ex.Message);
            }

            // ----- outputs -----
            ctx.OutputParameters[OutOpportunityId] = oppId.ToString();
            ctx.OutputParameters[OutDiscoveryConsolidated] = consolidated;
            localContext.Trace("AssociateMeeting: done. opp={0} consolidated={1}.", oppId, consolidated);
        }

        // ============================================================
        // 1. Opportunity resolution
        // ============================================================

        /// <summary>
        /// Explicit OpportunityId  >  create a new opportunity  >  the meeting's AI-suggested
        /// opportunity. Throws a user-facing error if none of the three yields an opportunity.
        /// </summary>
        private Guid ResolveOpportunity(LocalPluginContext localContext, IOrganizationService svc,
            Entity meeting, Guid oppIdInput, bool createNew, string oppTopicInput)
        {
            if (oppIdInput != Guid.Empty)
            {
                localContext.Trace("Opportunity provided explicitly: {0}.", oppIdInput);
                return oppIdInput;
            }

            if (createNew)
                return CreateOpportunity(localContext, svc, meeting, oppTopicInput);

            EntityReference suggested = meeting.GetAttributeValue<EntityReference>(MSuggestedOpp);
            if (suggested != null && string.Equals(suggested.LogicalName, OppEntity, StringComparison.Ordinal))
            {
                localContext.Trace("Accepting AI-suggested opportunity: {0}.", suggested.Id);
                return suggested.Id;
            }

            throw new InvalidPluginExecutionException(
                "No opportunity to associate. Provide an OpportunityId, or set CreateNewOpportunity, "
                + "or capture a suggested opportunity first.");
        }

        private Guid CreateOpportunity(LocalPluginContext localContext, IOrganizationService svc,
            Entity meeting, string oppTopicInput)
        {
            EntityReference account = meeting.GetAttributeValue<EntityReference>(MAccount);
            EntityReference contact = meeting.GetAttributeValue<EntityReference>(MContact);

            // The opportunity needs a customer. Prefer the account (B2B); fall back to the contact.
            EntityReference customer =
                account != null ? new EntityReference(AccountEntity, account.Id) :
                contact != null ? new EntityReference(ContactEntity, contact.Id) : null;

            if (customer == null)
                throw new InvalidPluginExecutionException(
                    "Cannot create an opportunity: the meeting has no matched account or contact. "
                    + "Match the meeting to a customer first, or associate to an existing opportunity.");

            string topic = (oppTopicInput ?? string.Empty).Trim();
            if (topic.Length == 0)
            {
                string subject = (meeting.GetAttributeValue<string>(MSubject) ?? string.Empty).Trim();
                topic = subject.Length > 0 ? subject : "New opportunity from meeting";
            }

            var opp = new Entity(OppEntity);
            opp[OppTopic] = Trunc(topic, 300);
            opp[OppCustomer] = customer;
            Guid id = svc.Create(opp);
            localContext.Trace("Created opportunity '{0}' = {1} (customer {2}).", topic, id, customer.LogicalName);
            return id;
        }

        // ============================================================
        // 3. Discovery consolidation (best-effort, AI)
        // ============================================================

        private bool MaybeConsolidateDiscovery(LocalPluginContext localContext, Guid oppId)
        {
            // Config reads + derived write -> SystemService.
            IOrganizationService svc = localContext.SystemService;

            if (!ConsolidationEnabled(svc))
            {
                localContext.Trace("Discovery consolidation disabled in System Settings. Skipping.");
                return false;
            }

            // Gather the discovery extracts of every meeting on this opportunity (incl. the one
            // we just associated, since its tavu_opportunity is now set).
            List<string> extracts = GatherDiscoveryExtracts(svc, oppId);
            if (extracts.Count == 0)
            {
                localContext.Trace("No discovery extracts to consolidate. Skipping.");
                return false;
            }

            AIResolvedConfig cfg = AIConfigResolver.Resolve(svc, TaskKeyMeetingCapture);
            if (!cfg.Usable)
            {
                localContext.Trace("AI not usable for consolidation: {0}. Skipping.", cfg.Reason);
                return false;
            }

            string userContent = BuildConsolidationContent(extracts);
            IAIProvider provider = cfg.UseGateway
                ? new GatewayProvider(cfg.GatewayUrl, cfg.GatewayKey)
                : AIProviderFactory.Create(cfg.ProviderValue);

            AICompletionResult ai = provider.Complete(
                AIConfigResolver.ToRequest(cfg, userContent, jsonResponse: true));
            if (!ai.Success)
            {
                localContext.Trace("AI consolidation call failed: {0}. Skipping.", ai.ErrorMessage);
                return false;
            }

            string consolidated = ParseConsolidatedDiscovery(localContext, ai.Content);
            if (string.IsNullOrEmpty(consolidated))
            {
                localContext.Trace("AI returned no consolidated discovery. Skipping.");
                return false;
            }

            var opp = new Entity(OppEntity, oppId);
            opp[OppDiscovery] = Trunc(consolidated, 4000);
            svc.Update(opp);
            localContext.Trace("Consolidated discovery ({0} sessions) written to opportunity {1}.",
                extracts.Count, oppId);
            return true;
        }

        private static bool ConsolidationEnabled(IOrganizationService svc)
        {
            var q = new QueryExpression(SettingsEntity)
            {
                ColumnSet = new ColumnSet(SettingConsolidate),
                TopCount = 1,
                NoLock = true
            };
            EntityCollection r = svc.RetrieveMultiple(q);
            if (r.Entities.Count == 0) return true; // no settings record -> default on
            Entity s = r.Entities[0];
            // Absent attribute -> default on; explicit false -> off.
            return !s.Contains(SettingConsolidate) || s.GetAttributeValue<bool>(SettingConsolidate);
        }

        private static List<string> GatherDiscoveryExtracts(IOrganizationService svc, Guid oppId)
        {
            var list = new List<string>();
            var q = new QueryExpression(MeetingEntity)
            {
                ColumnSet = new ColumnSet(MSubject, MDiscovery),
                NoLock = true,
                TopCount = MaxMeetingsToConsolidate
            };
            q.Criteria.AddCondition(MOpportunity, ConditionOperator.Equal, oppId);
            q.AddOrder("createdon", OrderType.Ascending);
            foreach (Entity m in svc.RetrieveMultiple(q).Entities)
            {
                string d = (m.GetAttributeValue<string>(MDiscovery) ?? string.Empty).Trim();
                if (d.Length == 0) continue;
                string subject = (m.GetAttributeValue<string>(MSubject) ?? "Session").Trim();
                list.Add(subject + ": " + d);
            }
            return list;
        }

        private static string BuildConsolidationContent(List<string> extracts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Consolidate the discovery notes from these meeting sessions into ONE coherent "
                + "view of the client's current situation, needs, and open questions. Deduplicate, keep "
                + "the latest understanding when sessions conflict, and write it as the opportunity's "
                + "discovery notes.");
            sb.AppendLine();
            sb.AppendLine("SESSIONS (oldest first):");
            foreach (string e in extracts) sb.AppendLine("- " + e);
            sb.AppendLine();
            sb.AppendLine("Return ONLY the JSON object from the system prompt. Put the consolidated notes "
                + "in the \"discoveryExtract\" field; leave the other fields empty.");
            return sb.ToString();
        }

        // Tolerant parse (same rationale as Pl.Meeting.Capture): the model may return
        // discoveryExtract as a nested object rather than a string. Read the JSON into an XML DOM
        // and flatten the discoveryExtract value to readable text.
        private static string ParseConsolidatedDiscovery(LocalPluginContext localContext, string content)
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

                foreach (System.Xml.XmlNode child in root.ChildNodes)
                    if (child.LocalName == "discoveryExtract")
                        return FlattenJsonNode(child);
                return null;
            }
            catch (Exception ex)
            {
                localContext.Trace("Failed to parse consolidation JSON: {0}. Raw: {1}", ex.Message, content);
                return null;
            }
        }

        /// <summary>
        /// Readable text for a JSON node that may be a plain string, an object, or an array.
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
        // input parsing (mirrors PromoteLead)
        // ============================================================

        private static Guid ParseRequiredGuid(IPluginExecutionContext ctx, string paramName, string label)
        {
            if (!ctx.InputParameters.Contains(paramName) ||
                !(ctx.InputParameters[paramName] is string raw) ||
                string.IsNullOrWhiteSpace(raw))
                throw new InvalidPluginExecutionException(label + " is required.");
            if (!Guid.TryParse(Clean(raw), out Guid value))
                throw new InvalidPluginExecutionException(label + " is not a valid GUID.");
            return value;
        }

        private static Guid ParseOptionalGuid(IPluginExecutionContext ctx, string paramName)
        {
            if (!ctx.InputParameters.Contains(paramName) ||
                !(ctx.InputParameters[paramName] is string raw) ||
                string.IsNullOrWhiteSpace(raw))
                return Guid.Empty;
            return Guid.TryParse(Clean(raw), out Guid value) ? value : Guid.Empty;
        }

        private static bool ParseOptionalBool(IPluginExecutionContext ctx, string paramName)
        {
            if (!ctx.InputParameters.Contains(paramName)) return false;
            object v = ctx.InputParameters[paramName];
            if (v is bool b) return b;
            return v is string s && bool.TryParse(s.Trim(), out bool parsed) && parsed;
        }

        private static string ParseOptionalString(IPluginExecutionContext ctx, string paramName)
        {
            if (!ctx.InputParameters.Contains(paramName)) return null;
            return ctx.InputParameters[paramName] as string;
        }

        private static string Clean(string raw)
        {
            return raw.Replace("{", string.Empty).Replace("}", string.Empty).Trim();
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max);
        }

        // ============================================================
        // AI output contract (reuses the Meeting Capture JSON shape;
        // we only read discoveryExtract here)
        // ============================================================

        [DataContract]
        private sealed class ConsolidationOutput
        {
            [DataMember(Name = "summary")]          public string Summary { get; set; }
            [DataMember(Name = "discoveryExtract")] public string DiscoveryExtract { get; set; }
            [DataMember(Name = "confidence")]       public double Confidence { get; set; }
            [DataMember(Name = "reasoning")]        public string Reasoning { get; set; }
        }
    }
}
