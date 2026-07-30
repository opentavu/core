using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.Proposal.BuildEmailDraft
{
    /// <summary>
    /// Custom API tavu_BuildProposalEmailDraft. Given a ProposalId, gathers the proposal +
    /// lines + the tenant Company Profile branding, calls the OpenTavu gateway
    /// (POST {tavu_GatewayUrl}/api/proposal/email-draft) to get a short AI email
    /// {subject, body} and a rendered PDF, then creates a DRAFT email activity (regarding the
    /// proposal, From = the current user, To = the client contact) with the PDF attached, and
    /// returns the new EmailId. The JS "Send to Client" handler opens that draft in the OOB
    /// email form for the seller to review and send.
    /// </summary>
    /// <remarks>
    /// Registered as the plugin type of the Custom API tavu_BuildProposalEmailDraft
    /// (Global/unbound; Request: ProposalId [String]; Response: EmailId [String]).
    /// No SDK message step to register — the Custom API message is the trigger.
    /// </remarks>
    public class BuildEmailDraft : PluginBase
    {
        // ===== Custom API parameters =====
        private const string InProposalId = "ProposalId";
        private const string OutEmailId = "EmailId";

        // ===== tavu_proposal =====
        private const string ProposalEntity = "tavu_proposal";
        private const string PName = "tavu_name";
        private const string PNumber = "tavu_proposalnumber";
        private const string PVersion = "tavu_version";
        private const string PSentDate = "tavu_sentdate";
        private const string PValidUntil = "tavu_expecteddecisiondate";
        private const string PCustomer = "tavu_customer";
        private const string PAccount = "tavu_account";
        private const string PContact = "tavu_contact";
        private const string PDiscoveryNotes = "tavu_discoverynotes";
        private const string PProposalContent = "tavu_proposalcontent";
        private const string PSubtotal = "tavu_subtotal";
        private const string PTotalTax = "tavu_totaltax";
        private const string PTotal = "tavu_total";
        private const string POpportunity = "tavu_opportunity";
        private const string PCurrency = "transactioncurrencyid";

        // ===== tavu_proposalline =====
        private const string LineEntity = "tavu_proposalline";
        private const string LProposal = "tavu_proposal";
        private const string LProduct = "tavu_product";
        private const string LUom = "tavu_unitofmeasure";
        private const string LQuantity = "tavu_quantity";
        private const string LPricePerUnit = "tavu_priceperunit";
        private const string LSubtotal = "tavu_subtotal";

        // ===== tavu_companyprofile =====
        private const string ProfileEntity = "tavu_companyprofile";
        private const string CpName = "tavu_name";
        private const string CpAddress = "tavu_address";
        private const string CpEmail = "tavu_email";
        private const string CpPhone = "tavu_phone";
        private const string CpTaxId = "tavu_taxid";
        private const string CpWebsite = "tavu_website";
        private const string CpAccent = "tavu_brandaccentcolor";
        private const string CpTerms = "tavu_defaultproposalterms";
        private const string CpLogo = "tavu_logo";

        // ===== opportunity (fallback recipient) =====
        private const string OppEntity = "tavu_opportunity";
        private const string OppPrimaryContact = "tavu_primarycontact";

        // ===== gateway env vars =====
        private const string GatewayUrlVar = "tavu_GatewayUrl";
        private const string GatewayKeyVar = "tavu_GatewayKey";
        private const string TenantHeader = "X-OpenTavu-Tenant-Key";
        private const int GatewayTimeoutSeconds = 100;

        private const int StateActive = 0;

        public BuildEmailDraft() : base(typeof(BuildEmailDraft)) { }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null) throw new ArgumentNullException(nameof(localContext));
            var ctx = localContext.PluginExecutionContext;
            localContext.Trace("BuildEmailDraft: entered. Message={0}.", ctx.MessageName);

            // ----- input -----
            if (!ctx.InputParameters.Contains(InProposalId) ||
                !(ctx.InputParameters[InProposalId] is string proposalIdRaw) ||
                string.IsNullOrWhiteSpace(proposalIdRaw))
                throw new InvalidPluginExecutionException("ProposalId is required.");

            if (!Guid.TryParse(proposalIdRaw.Replace("{", "").Replace("}", "").Trim(), out Guid proposalId))
                throw new InvalidPluginExecutionException("ProposalId is not a valid GUID.");

            IOrganizationService sys = localContext.SystemService;   // config + document reads
            IOrganizationService usr = localContext.UserService;     // create the email as the user

            // ----- proposal -----
            Entity proposal = sys.Retrieve(ProposalEntity, proposalId, new ColumnSet(
                PName, PNumber, PVersion, PSentDate, PValidUntil, PCustomer, PAccount, PContact,
                PDiscoveryNotes, PProposalContent, PSubtotal, PTotalTax, PTotal, POpportunity, PCurrency));

            string currency = ResolveCurrency(sys, proposal);

            // ----- lines -----
            var lines = LoadLines(sys, proposalId);

            // ----- company profile (branding) -----
            Entity profile = RetrieveTop1(sys, ProfileEntity, null, new ColumnSet(
                CpName, CpAddress, CpEmail, CpPhone, CpTaxId, CpWebsite, CpAccent, CpTerms));
            string logoBase64 = profile != null ? TryDownloadLogo(localContext, sys, profile.Id) : null;

            // ----- recipient resolution -----
            // proposal contact (B2C) -> customer-as-contact -> primary contact of the account
            // (from tavu_account, or a customer that is an account) -> opportunity primary contact.
            EntityReference oppRef = proposal.GetAttributeValue<EntityReference>(POpportunity);
            EntityReference customerRef = proposal.GetAttributeValue<EntityReference>(PCustomer);
            EntityReference toContact = proposal.GetAttributeValue<EntityReference>(PContact);

            if (toContact == null && customerRef != null &&
                string.Equals(customerRef.LogicalName, "contact", StringComparison.Ordinal))
                toContact = customerRef;

            if (toContact == null)
            {
                EntityReference acctRef = proposal.GetAttributeValue<EntityReference>(PAccount);
                if (acctRef == null && customerRef != null &&
                    string.Equals(customerRef.LogicalName, "account", StringComparison.Ordinal))
                    acctRef = customerRef;

                if (acctRef != null)
                {
                    try
                    {
                        Entity acct = sys.Retrieve("account", acctRef.Id, new ColumnSet("primarycontactid"));
                        toContact = acct.GetAttributeValue<EntityReference>("primarycontactid");
                    }
                    catch (Exception ex) { localContext.Trace("Could not read account primary contact: {0}", ex.Message); }
                }
            }
            if (toContact == null && oppRef != null)
            {
                try
                {
                    Entity opp = sys.Retrieve(OppEntity, oppRef.Id, new ColumnSet(OppPrimaryContact));
                    toContact = opp.GetAttributeValue<EntityReference>(OppPrimaryContact);
                }
                catch (Exception ex) { localContext.Trace("Could not read opportunity primary contact: {0}", ex.Message); }
            }

            // Sender name for the AI signature: the current (sending) user.
            string senderName = null;
            try
            {
                Entity me = sys.Retrieve("systemuser", ctx.InitiatingUserId, new ColumnSet("fullname"));
                senderName = me.GetAttributeValue<string>("fullname");
            }
            catch (Exception ex) { localContext.Trace("Could not read sender name: {0}", ex.Message); }

            // ----- gateway config -----
            string gatewayUrl = ReadEnvironmentVariable(sys, GatewayUrlVar);
            string gatewayKey = ReadEnvironmentVariable(sys, GatewayKeyVar);
            if (string.IsNullOrEmpty(gatewayUrl) || string.IsNullOrEmpty(gatewayKey))
                throw new InvalidPluginExecutionException(
                    "The AI gateway is not configured (tavu_GatewayUrl / tavu_GatewayKey). " +
                    "Proposal email drafting requires a configured gateway.");

            // ----- build request + call gateway -----
            var request = new EmailDraftRequest
            {
                Proposal = new ProposalDto
                {
                    Name = proposal.GetAttributeValue<string>(PName),
                    Number = proposal.GetAttributeValue<string>(PNumber),
                    Version = proposal.GetAttributeValue<string>(PVersion),
                    IssueDate = FormatDate(proposal.GetAttributeValue<DateTime?>(PSentDate) ?? DateTime.UtcNow),
                    ValidUntil = FormatDateOrNull(proposal.GetAttributeValue<DateTime?>(PValidUntil)),
                    ClientName = LookupName(proposal, PAccount) ?? LookupName(proposal, PContact) ?? LookupName(proposal, PCustomer),
                    ContactName = (toContact != null && !string.IsNullOrEmpty(toContact.Name)) ? toContact.Name : LookupName(proposal, PContact),
                    Currency = currency,
                    Subtotal = MoneyVal(proposal, PSubtotal),
                    Tax = MoneyVal(proposal, PTotalTax),
                    Total = MoneyVal(proposal, PTotal),
                    Lines = lines,
                    DiscoveryNotes = proposal.GetAttributeValue<string>(PDiscoveryNotes),
                    ProposalContent = proposal.GetAttributeValue<string>(PProposalContent),
                    SenderName = senderName
                },
                Branding = new BrandingDto
                {
                    CompanyName = profile?.GetAttributeValue<string>(CpName),
                    Address = profile?.GetAttributeValue<string>(CpAddress),
                    Email = profile?.GetAttributeValue<string>(CpEmail),
                    Phone = profile?.GetAttributeValue<string>(CpPhone),
                    TaxId = profile?.GetAttributeValue<string>(CpTaxId),
                    Website = profile?.GetAttributeValue<string>(CpWebsite),
                    AccentColorHex = profile?.GetAttributeValue<string>(CpAccent),
                    LogoBase64 = logoBase64,
                    Terms = profile?.GetAttributeValue<string>(CpTerms)
                }
            };

            EmailDraftResponse result = CallGateway(localContext, gatewayUrl, gatewayKey, request);

            // ----- create the draft email + attach the PDF -----
            string fileName = SanitizeFileName(request.Proposal.Name ?? "Proposal") + ".pdf";
            Guid emailId = CreateDraftEmail(usr, ctx.InitiatingUserId, proposalId, toContact,
                result.Subject, result.Body);
            AttachPdf(usr, emailId, result.PdfBase64, fileName);

            ctx.OutputParameters[OutEmailId] = emailId.ToString();
            localContext.Trace("BuildEmailDraft: created draft email {0}. Exiting.", emailId);
        }

        // ================= data gathering =================

        private List<LineDto> LoadLines(IOrganizationService svc, Guid proposalId)
        {
            var q = new QueryExpression(LineEntity)
            {
                ColumnSet = new ColumnSet(LProduct, LUom, LQuantity, LPricePerUnit, LSubtotal),
                NoLock = true,
                Criteria = new FilterExpression()
            };
            q.Criteria.AddCondition(LProposal, ConditionOperator.Equal, proposalId);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            q.AddOrder("createdon", OrderType.Ascending);

            var list = new List<LineDto>();
            foreach (var e in svc.RetrieveMultiple(q).Entities)
            {
                list.Add(new LineDto
                {
                    Description = e.GetAttributeValue<EntityReference>(LProduct)?.Name ?? string.Empty,
                    Quantity = e.GetAttributeValue<decimal>(LQuantity),
                    Unit = e.GetAttributeValue<EntityReference>(LUom)?.Name ?? string.Empty,
                    UnitPrice = MoneyVal(e, LPricePerUnit),
                    Amount = MoneyVal(e, LSubtotal)
                });
            }
            return list;
        }

        private string ResolveCurrency(IOrganizationService svc, Entity proposal)
        {
            var cur = proposal.GetAttributeValue<EntityReference>(PCurrency);
            if (cur == null) return string.Empty;
            try
            {
                Entity c = svc.Retrieve("transactioncurrency", cur.Id, new ColumnSet("isocurrencycode"));
                return c.GetAttributeValue<string>("isocurrencycode") ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private string TryDownloadLogo(LocalPluginContext localContext, IOrganizationService svc, Guid profileId)
        {
            try
            {
                var init = new InitializeFileBlocksDownloadRequest
                {
                    Target = new EntityReference(ProfileEntity, profileId),
                    FileAttributeName = CpLogo
                };
                var initResp = (InitializeFileBlocksDownloadResponse)svc.Execute(init);
                if (initResp.FileSizeInBytes <= 0) return null;

                var dl = new DownloadBlockRequest
                {
                    FileContinuationToken = initResp.FileContinuationToken,
                    Offset = 0,
                    BlockLength = initResp.FileSizeInBytes
                };
                var dlResp = (DownloadBlockResponse)svc.Execute(dl);
                if (dlResp.Data == null || dlResp.Data.Length == 0) return null;
                return Convert.ToBase64String(dlResp.Data);
            }
            catch (Exception ex)
            {
                localContext.Trace("Logo download skipped: {0}", ex.Message);
                return null; // no logo -> PDF still renders
            }
        }

        // ================= email creation =================

        private Guid CreateDraftEmail(IOrganizationService svc, Guid fromUserId, Guid proposalId,
            EntityReference toContact, string subject, string body)
        {
            var email = new Entity("email");
            email["subject"] = string.IsNullOrWhiteSpace(subject) ? "Proposal" : subject;
            email["description"] = ToHtml(body);
            email["regardingobjectid"] = new EntityReference(ProposalEntity, proposalId);

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

        private void AttachPdf(IOrganizationService svc, Guid emailId, string pdfBase64, string fileName)
        {
            if (string.IsNullOrEmpty(pdfBase64)) return;
            var att = new Entity("activitymimeattachment");
            att["objectid"] = new EntityReference("email", emailId);
            att["objecttypecode"] = "email";
            att["subject"] = fileName;
            att["filename"] = fileName;
            att["mimetype"] = "application/pdf";
            att["body"] = pdfBase64; // base64-encoded content
            svc.Create(att);
        }

        // ================= gateway call =================

        private EmailDraftResponse CallGateway(LocalPluginContext localContext, string baseUrl,
            string tenantKey, EmailDraftRequest request)
        {
            string url = baseUrl.TrimEnd('/') + "/api/proposal/email-draft";
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                byte[] payload = Encoding.UTF8.GetBytes(Serialize(request));

                var http = (HttpWebRequest)WebRequest.Create(url);
                http.Method = "POST";
                http.ContentType = "application/json";
                http.Accept = "application/json";
                http.Headers[TenantHeader] = tenantKey;
                http.Timeout = GatewayTimeoutSeconds * 1000;
                http.ContentLength = payload.Length;
                using (Stream rs = http.GetRequestStream()) rs.Write(payload, 0, payload.Length);

                string json;
                using (var resp = (HttpWebResponse)http.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (var reader = new StreamReader(s, Encoding.UTF8))
                    json = reader.ReadToEnd();

                var parsed = Deserialize<EmailDraftResponse>(json);
                if (parsed == null || string.IsNullOrEmpty(parsed.PdfBase64))
                    throw new InvalidPluginExecutionException("Gateway returned no PDF.");
                return parsed;
            }
            catch (WebException wex)
            {
                string detail = wex.Message;
                try
                {
                    if (wex.Response != null)
                        using (Stream es = wex.Response.GetResponseStream())
                        using (var er = new StreamReader(es, Encoding.UTF8))
                            detail = er.ReadToEnd();
                }
                catch { /* ignore */ }
                localContext.Trace("Gateway error: {0}", detail);
                throw new InvalidPluginExecutionException("Couldn't build the proposal email: " + detail);
            }
        }

        // ================= helpers =================

        private static decimal MoneyVal(Entity e, string attr)
        {
            var m = e.GetAttributeValue<Money>(attr);
            return m != null ? m.Value : 0m;
        }

        private static string LookupName(Entity e, string attr)
        {
            var r = e.GetAttributeValue<EntityReference>(attr);
            return r != null && !string.IsNullOrEmpty(r.Name) ? r.Name : null;
        }

        private static string FormatDate(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        private static string FormatDateOrNull(DateTime? d) => d.HasValue ? FormatDate(d.Value) : null;

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Proposal";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
            return name.Length > 120 ? name.Substring(0, 120) : name;
        }

        /// <summary>Minimal plain-text -> HTML so newlines render in the OOB email body.</summary>
        private static string ToHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string encoded = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            return encoded.Replace("\r\n", "<br>").Replace("\n", "<br>");
        }

        private static Entity RetrieveTop1(IOrganizationService svc, string entity,
            FilterExpression filter, ColumnSet cols)
        {
            var q = new QueryExpression(entity) { ColumnSet = cols, TopCount = 1, NoLock = true };
            if (filter != null) q.Criteria = filter;
            var r = svc.RetrieveMultiple(q);
            return r.Entities.Count > 0 ? r.Entities[0] : null;
        }

        private static string ReadEnvironmentVariable(IOrganizationService svc, string schemaName)
        {
            if (string.IsNullOrEmpty(schemaName)) return null;

            var defFilter = new FilterExpression();
            defFilter.AddCondition("schemaname", ConditionOperator.Equal, schemaName);
            Entity def = RetrieveTop1(svc, "environmentvariabledefinition", defFilter,
                new ColumnSet("environmentvariabledefinitionid", "defaultvalue"));
            if (def == null) return null;

            string defaultValue = def.GetAttributeValue<string>("defaultvalue");

            var valFilter = new FilterExpression();
            valFilter.AddCondition("environmentvariabledefinitionid", ConditionOperator.Equal, def.Id);
            Entity val = RetrieveTop1(svc, "environmentvariablevalue", valFilter, new ColumnSet("value"));

            string current = val?.GetAttributeValue<string>("value");
            return !string.IsNullOrEmpty(current) ? current : defaultValue;
        }

        private static string Serialize<T>(T obj)
        {
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(ms, obj);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static T Deserialize<T>(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(ms);
        }

        // ================= DTOs (JSON matches the gateway's camelCase contract) =================

        [DataContract]
        private class EmailDraftRequest
        {
            [DataMember(Name = "proposal", Order = 0)] public ProposalDto Proposal { get; set; }
            [DataMember(Name = "branding", Order = 1)] public BrandingDto Branding { get; set; }
        }

        [DataContract]
        private class ProposalDto
        {
            [DataMember(Name = "name", Order = 0)] public string Name { get; set; }
            [DataMember(Name = "number", Order = 1)] public string Number { get; set; }
            [DataMember(Name = "version", Order = 2)] public string Version { get; set; }
            [DataMember(Name = "issueDate", Order = 3)] public string IssueDate { get; set; }
            [DataMember(Name = "validUntil", Order = 4)] public string ValidUntil { get; set; }
            [DataMember(Name = "clientName", Order = 5)] public string ClientName { get; set; }
            [DataMember(Name = "contactName", Order = 6)] public string ContactName { get; set; }
            [DataMember(Name = "currency", Order = 7)] public string Currency { get; set; }
            [DataMember(Name = "subtotal", Order = 8)] public decimal Subtotal { get; set; }
            [DataMember(Name = "tax", Order = 9)] public decimal Tax { get; set; }
            [DataMember(Name = "total", Order = 10)] public decimal Total { get; set; }
            [DataMember(Name = "lines", Order = 11)] public List<LineDto> Lines { get; set; }
            [DataMember(Name = "discoveryNotes", Order = 12)] public string DiscoveryNotes { get; set; }
            [DataMember(Name = "proposalContent", Order = 13)] public string ProposalContent { get; set; }
            [DataMember(Name = "senderName", Order = 14)] public string SenderName { get; set; }
        }

        [DataContract]
        private class LineDto
        {
            [DataMember(Name = "description", Order = 0)] public string Description { get; set; }
            [DataMember(Name = "quantity", Order = 1)] public decimal Quantity { get; set; }
            [DataMember(Name = "unit", Order = 2)] public string Unit { get; set; }
            [DataMember(Name = "unitPrice", Order = 3)] public decimal UnitPrice { get; set; }
            [DataMember(Name = "amount", Order = 4)] public decimal Amount { get; set; }
        }

        [DataContract]
        private class BrandingDto
        {
            [DataMember(Name = "companyName", Order = 0)] public string CompanyName { get; set; }
            [DataMember(Name = "address", Order = 1)] public string Address { get; set; }
            [DataMember(Name = "email", Order = 2)] public string Email { get; set; }
            [DataMember(Name = "phone", Order = 3)] public string Phone { get; set; }
            [DataMember(Name = "taxId", Order = 4)] public string TaxId { get; set; }
            [DataMember(Name = "website", Order = 5)] public string Website { get; set; }
            [DataMember(Name = "accentColorHex", Order = 6)] public string AccentColorHex { get; set; }
            [DataMember(Name = "logoBase64", Order = 7)] public string LogoBase64 { get; set; }
            [DataMember(Name = "terms", Order = 8)] public string Terms { get; set; }
        }

        [DataContract]
        private class EmailDraftResponse
        {
            [DataMember(Name = "subject")] public string Subject { get; set; }
            [DataMember(Name = "body")] public string Body { get; set; }
            [DataMember(Name = "pdfBase64")] public string PdfBase64 { get; set; }
        }
    }
}
