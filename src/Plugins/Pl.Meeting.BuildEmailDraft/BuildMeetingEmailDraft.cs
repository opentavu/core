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

namespace Pl.Meeting.BuildEmailDraft
{
    /// <summary>
    /// Custom API tavu_BuildMeetingEmailDraft. Module 3, Part B (Activity Capture), the follow-up.
    /// Given a MeetingId, gathers the AI summary + discovery extract and the deal context, asks the
    /// AI (via the resolved provider, which routes through the gateway when configured) for a short
    /// follow-up email {subject, body}, and creates a DRAFT email activity (regarding the associated
    /// opportunity, From = the current user, To = the meeting contact). It links the draft back on
    /// the meeting (tavu_draftemail) and returns the new EmailId. The "Review draft email" button
    /// opens that draft in the OOB email form for the rep to corroborate and send.
    ///
    /// Unlike the proposal email draft, there is no PDF, so no bespoke gateway endpoint is needed:
    /// a plain AI completion (JSON) is enough. Config reads use SystemService; the email is created
    /// with UserService so the rep owns and sends it.
    /// </summary>
    /// <remarks>
    /// Registered as the plugin type of the Custom API tavu_BuildMeetingEmailDraft
    /// (Global/unbound; Request: MeetingId [String]; Response: EmailId [String]).
    /// No SDK message step; the Custom API message is the trigger.
    /// </remarks>
    public class BuildMeetingEmailDraft : PluginBase
    {
        // ===== Custom API parameters =====
        private const string InMeetingId = "MeetingId";
        private const string OutEmailId  = "EmailId";

        // ===== tavu_meeting =====
        private const string MeetingEntity = "tavu_meeting";
        private const string MSubject     = "subject";
        private const string MSummary     = "tavu_summary";
        private const string MDiscovery   = "tavu_discoveryextract";
        private const string MAttendees   = "tavu_attendees";
        private const string MContact     = "tavu_contact";
        private const string MAccount     = "tavu_account";
        private const string MOpportunity = "tavu_opportunity";
        private const string MDraftEmail  = "tavu_draftemail"; // lookup -> email

        // ===== opportunity (context + fallback recipient) =====
        private const string OppEntity        = "tavu_opportunity";
        private const string OppTopic         = "tavu_topic";
        private const string OppPrimaryContact = "tavu_primarycontact";

        private const string ContactEntity = "contact";

        // Task Key option value for "Meeting Follow-up Email" in tavu_aitaskkeyconfig.
        // VERIFY: add "Meeting Follow-up Email" to that choice and set this to the assigned integer
        // (next after Meeting Capture = 576600004).
        private const int TaskKeyMeetingEmailDraft = 576600005;

        public BuildMeetingEmailDraft() : base(typeof(BuildMeetingEmailDraft)) { }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null) throw new ArgumentNullException(nameof(localContext));
            var ctx = localContext.PluginExecutionContext;
            localContext.Trace("BuildMeetingEmailDraft: entered. Message={0}.", ctx.MessageName);

            // ----- input -----
            if (!ctx.InputParameters.Contains(InMeetingId) ||
                !(ctx.InputParameters[InMeetingId] is string meetingIdRaw) ||
                string.IsNullOrWhiteSpace(meetingIdRaw))
                throw new InvalidPluginExecutionException("MeetingId is required.");

            if (!Guid.TryParse(meetingIdRaw.Replace("{", "").Replace("}", "").Trim(), out Guid meetingId))
                throw new InvalidPluginExecutionException("MeetingId is not a valid GUID.");

            IOrganizationService sys = localContext.SystemService; // config reads
            IOrganizationService usr = localContext.UserService;   // create the email as the user

            // ----- meeting -----
            Entity meeting = sys.Retrieve(MeetingEntity, meetingId, new ColumnSet(
                MSubject, MSummary, MDiscovery, MAttendees, MContact, MAccount, MOpportunity));

            string summary   = (meeting.GetAttributeValue<string>(MSummary) ?? string.Empty).Trim();
            string discovery = (meeting.GetAttributeValue<string>(MDiscovery) ?? string.Empty).Trim();
            if (summary.Length == 0 && discovery.Length == 0)
                throw new InvalidPluginExecutionException(
                    "Nothing to draft from yet. Capture the meeting (AI summary/discovery) first.");

            EntityReference oppRef     = meeting.GetAttributeValue<EntityReference>(MOpportunity);
            EntityReference contactRef = meeting.GetAttributeValue<EntityReference>(MContact);

            // Recipient: the meeting contact, else the opportunity's primary contact.
            EntityReference toContact = contactRef;
            if (toContact == null && oppRef != null)
            {
                try
                {
                    Entity opp = sys.Retrieve(OppEntity, oppRef.Id, new ColumnSet(OppPrimaryContact));
                    toContact = opp.GetAttributeValue<EntityReference>(OppPrimaryContact);
                }
                catch (Exception ex) { localContext.Trace("Could not read opportunity primary contact: {0}", ex.Message); }
            }

            // Sender name for the signature: the current (sending) user.
            string senderName = null;
            try
            {
                Entity me = sys.Retrieve("systemuser", ctx.InitiatingUserId, new ColumnSet("fullname"));
                senderName = me.GetAttributeValue<string>("fullname");
            }
            catch (Exception ex) { localContext.Trace("Could not read sender name: {0}", ex.Message); }

            // ----- resolve AI + draft the email -----
            AIResolvedConfig cfg = AIConfigResolver.Resolve(sys, TaskKeyMeetingEmailDraft);
            if (!cfg.Usable)
                throw new InvalidPluginExecutionException(
                    "The AI is not available for the follow-up draft: " + cfg.Reason);

            string userContent = BuildUserContent(
                meeting.GetAttributeValue<string>(MSubject),
                summary, discovery,
                meeting.GetAttributeValue<string>(MAttendees),
                (toContact != null ? toContact.Name : null),
                senderName);

            IAIProvider provider = cfg.UseGateway
                ? new GatewayProvider(cfg.GatewayUrl, cfg.GatewayKey)
                : AIProviderFactory.Create(cfg.ProviderValue);

            AICompletionResult ai = provider.Complete(
                AIConfigResolver.ToRequest(cfg, userContent, jsonResponse: true));
            if (!ai.Success)
                throw new InvalidPluginExecutionException("Couldn't draft the follow-up email: " + ai.ErrorMessage);

            EmailDraftOutput o = ParseOutput(localContext, ai.Content);
            if (o == null || string.IsNullOrWhiteSpace(o.Body))
                throw new InvalidPluginExecutionException("The AI returned an empty follow-up email.");

            // ----- create the draft email -----
            // Regarding the opportunity (so it lands in the deal timeline); else the contact.
            EntityReference regarding =
                oppRef != null ? oppRef :
                toContact != null ? toContact : null;

            Guid emailId = CreateDraftEmail(usr, ctx.InitiatingUserId, regarding, toContact,
                o.Subject, o.Body);

            // Link the draft back on the meeting for the "Review draft email" button.
            try
            {
                var link = new Entity(MeetingEntity, meetingId);
                link[MDraftEmail] = new EntityReference("email", emailId);
                usr.Update(link);
            }
            catch (Exception ex) { localContext.Trace("Could not link draft email on the meeting: {0}", ex.Message); }

            ctx.OutputParameters[OutEmailId] = emailId.ToString();
            localContext.Trace("BuildMeetingEmailDraft: created draft email {0}. Exiting.", emailId);
        }

        // ============================================================
        // AI input
        // ============================================================

        private static string BuildUserContent(string subject, string summary, string discovery,
            string attendees, string contactName, string senderName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Write a short, warm follow-up email to the client after this meeting.");
            sb.AppendLine("Recap what was discussed, confirm the next steps, and keep it concise and professional.");
            sb.AppendLine("Do not invent commitments the notes do not support. Do not use em dashes.");
            sb.AppendLine();
            sb.AppendLine("MEETING SUBJECT: " + (subject ?? string.Empty));
            sb.AppendLine("ATTENDEES: " + (attendees ?? string.Empty));
            sb.AppendLine("RECIPIENT: " + (contactName ?? "the client"));
            sb.AppendLine("SENDER (sign as): " + (senderName ?? string.Empty));
            sb.AppendLine();
            sb.AppendLine("SUMMARY:");
            sb.AppendLine(summary);
            sb.AppendLine();
            sb.AppendLine("DISCOVERY / NEXT STEPS:");
            sb.AppendLine(discovery);
            sb.AppendLine();
            sb.AppendLine("Return ONLY the JSON object from the system prompt: { \"subject\": ..., \"body\": ... }.");
            return sb.ToString();
        }

        private EmailDraftOutput ParseOutput(LocalPluginContext localContext, string content)
        {
            try
            {
                string json = CleanJson(content);
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return (EmailDraftOutput)
                        new DataContractJsonSerializer(typeof(EmailDraftOutput)).ReadObject(ms);
            }
            catch (Exception ex)
            {
                localContext.Trace("Failed to parse follow-up JSON: {0}. Raw: {1}", ex.Message, content);
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
        // email creation
        // ============================================================

        private Guid CreateDraftEmail(IOrganizationService svc, Guid fromUserId,
            EntityReference regarding, EntityReference toContact, string subject, string body)
        {
            var email = new Entity("email");
            email["subject"] = string.IsNullOrWhiteSpace(subject) ? "Follow-up" : subject;
            email["description"] = ToHtml(body);
            if (regarding != null)
                email["regardingobjectid"] = new EntityReference(regarding.LogicalName, regarding.Id);

            var from = new Entity("activityparty");
            from["partyid"] = new EntityReference("systemuser", fromUserId);
            email["from"] = new EntityCollection(new List<Entity> { from }) { EntityName = "activityparty" };

            if (toContact != null)
            {
                var to = new Entity("activityparty");
                to["partyid"] = new EntityReference(toContact.LogicalName, toContact.Id);
                email["to"] = new EntityCollection(new List<Entity> { to }) { EntityName = "activityparty" };
            }

            return svc.Create(email); // created Open/Draft by default
        }

        /// <summary>Minimal plain-text -> HTML so newlines render in the OOB email body.</summary>
        private static string ToHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string encoded = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            return encoded.Replace("\r\n", "<br>").Replace("\n", "<br>");
        }

        // ============================================================
        // AI output contract
        // ============================================================

        [DataContract]
        private sealed class EmailDraftOutput
        {
            [DataMember(Name = "subject")] public string Subject { get; set; }
            [DataMember(Name = "body")]    public string Body { get; set; }
        }
    }
}
