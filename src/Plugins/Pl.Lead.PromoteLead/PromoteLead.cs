using System;
using Microsoft.Xrm.Sdk;
using OpenTavu.Dataverse.Common;

namespace Pl.Lead.PromoteLead
{
    /// <summary>
    /// Custom API tavu_PromoteLead. Module 3, Step 3 (the deliberate 2nd-line human gate).
    /// This is the ONE write that touches the clean master Contact/Account DB, so it only
    /// ever runs from an explicit human click on a lead in "Awaiting Human Review".
    ///
    /// Two modes, driven by the inputs:
    ///   - LINK TO EXISTING: LinkToContactId supplied -> no record is created; the lead is
    ///     linked to that contact and closed as "Promoted to Contact".
    ///   - APPROVE &amp; PROMOTE (create new): no LinkToContactId -> resolve the account
    ///     (LinkToAccountId  >  the lead's Matched Account  >  create a new account from the
    ///     raw company name) and create a new contact under it from the lead's fields.
    ///
    /// Idempotent: if the lead already carries a Promoted Contact, it returns that instead of
    /// creating a duplicate (guards a double-click / retry). Uses UserService so every write
    /// respects the acting user's privileges; the human is accountable for this promotion.
    /// </summary>
    /// <remarks>
    /// Registered as the plugin type of the Custom API tavu_PromoteLead
    /// (bound OR global; here global/unbound for simplicity):
    ///   Request:  LeadId [String, required], LinkToContactId [String, optional],
    ///             LinkToAccountId [String, optional]
    ///   Response: ContactId [String], AccountId [String]
    /// No SDK message step to register; the Custom API message is the trigger.
    /// </remarks>
    public class PromoteLead : PluginBase
    {
        // ===== Custom API parameters =====
        private const string InLeadId        = "LeadId";
        private const string InLinkContactId = "LinkToContactId";
        private const string InLinkAccountId = "LinkToAccountId";
        private const string OutContactId    = "ContactId";
        private const string OutAccountId    = "AccountId";

        // ===== tavu_lead =====
        private const string LeadEntity          = "tavu_lead";
        private const string LeadFirstName       = "tavu_firstname";
        private const string LeadLastName        = "tavu_lastname";
        private const string LeadEmail           = "tavu_email";
        private const string LeadPhone           = "tavu_phone";
        private const string LeadMobilePhone     = "tavu_mobilephone";
        private const string LeadCompanyName     = "tavu_companyname";
        private const string LeadMatchedAccount  = "tavu_matchedaccount";
        private const string LeadMatchedContact  = "tavu_matchedcontact";
        private const string LeadPromotedContact = "tavu_promotedcontact";

        // ===== contact / account (standard) =====
        private const string ContactEntity = "contact";
        private const string CFirstName    = "firstname";
        private const string CLastName     = "lastname";
        private const string CEmail        = "emailaddress1";
        private const string CPhone        = "telephone1";
        private const string CMobile       = "mobilephone";
        private const string CParent       = "parentcustomerid";
        private const string AccountEntity = "account";
        private const string AName         = "name";
        private const string APrimaryCt    = "primarycontactid";

        // Provenance: lookup to tavu_lead, present on BOTH contact and account. Set only when
        // this API CREATES the record (never on link-to-existing or matched-account reuse), so
        // the new master record can jump back to the raw inbound signal and the AI's reasoning.
        private const string OriginatingLead = "tavu_originatinglead";

        // ===== tavu_lead status =====
        private const int StatusPromotedToContact = 576600005; // Inactive
        private const int StateInactive = 1;

        public PromoteLead() : base(typeof(PromoteLead)) { }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null) throw new ArgumentNullException(nameof(localContext));
            var ctx = localContext.PluginExecutionContext;
            localContext.Trace("PromoteLead: entered. Message={0}.", ctx.MessageName);

            // ----- inputs -----
            Guid leadId = ParseRequiredGuid(ctx, InLeadId, "LeadId");
            Guid linkContactId = ParseOptionalGuid(ctx, InLinkContactId);
            Guid linkAccountId = ParseOptionalGuid(ctx, InLinkAccountId);

            // The human is acting: writes must respect their privileges (the 2nd-line gate).
            IOrganizationService svc = localContext.UserService;

            Entity lead = svc.Retrieve(LeadEntity, leadId, new Microsoft.Xrm.Sdk.Query.ColumnSet(
                LeadFirstName, LeadLastName, LeadEmail, LeadPhone, LeadMobilePhone,
                LeadCompanyName, LeadMatchedAccount, LeadMatchedContact, LeadPromotedContact));

            // ----- idempotency: already promoted? return the existing contact -----
            EntityReference alreadyPromoted = lead.GetAttributeValue<EntityReference>(LeadPromotedContact);
            if (alreadyPromoted != null)
            {
                localContext.Trace("Lead already promoted to contact {0}. Returning it (no-op).", alreadyPromoted.Id);
                SetOutputs(ctx, alreadyPromoted.Id, ResolveAccountOfContact(svc, alreadyPromoted.Id));
                return;
            }

            Guid contactId;
            Guid accountId;

            if (linkContactId != Guid.Empty)
            {
                // ---------- LINK TO EXISTING contact ----------
                contactId = linkContactId;
                accountId = ResolveAccountOfContact(svc, contactId);
                localContext.Trace("Link-to-existing mode. contact={0} account={1}.", contactId, accountId);
            }
            else
            {
                // ---------- APPROVE & PROMOTE (create new contact) ----------
                accountId = ResolveOrCreateAccount(localContext, svc, lead, linkAccountId);
                contactId = CreateContact(localContext, svc, lead, accountId);
                localContext.Trace("Promote-create-new mode. created contact={0} account={1}.", contactId, accountId);
            }

            // ----- close the lead: link + Promoted to Contact (Inactive) -----
            var update = new Entity(LeadEntity, leadId);
            update[LeadPromotedContact] = new EntityReference(ContactEntity, contactId);
            if (accountId != Guid.Empty)
                update[LeadMatchedAccount] = new EntityReference(AccountEntity, accountId);
            update["statecode"] = new OptionSetValue(StateInactive);
            update["statuscode"] = new OptionSetValue(StatusPromotedToContact);
            svc.Update(update);

            SetOutputs(ctx, contactId, accountId);
            localContext.Trace("PromoteLead: done. contact={0} account={1}.", contactId, accountId);
        }

        // ================= account resolution =================

        /// <summary>LinkToAccountId  >  lead's Matched Account  >  create a new account from the raw company name.</summary>
        private Guid ResolveOrCreateAccount(LocalPluginContext localContext, IOrganizationService svc,
            Entity lead, Guid linkAccountId)
        {
            if (linkAccountId != Guid.Empty)
                return linkAccountId;

            EntityReference matched = lead.GetAttributeValue<EntityReference>(LeadMatchedAccount);
            if (matched != null && string.Equals(matched.LogicalName, AccountEntity, StringComparison.Ordinal))
                return matched.Id;

            string company = (lead.GetAttributeValue<string>(LeadCompanyName) ?? string.Empty).Trim();
            if (company.Length == 0)
            {
                // No company context at all -> promote the person without a parent account.
                localContext.Trace("No account link, no matched account, no company name. Contact will have no parent.");
                return Guid.Empty;
            }

            var account = new Entity(AccountEntity);
            account[AName] = company;
            // Provenance: this account is being created from the lead (create path only).
            account[OriginatingLead] = new EntityReference(LeadEntity, lead.Id);
            Guid newId = svc.Create(account);
            localContext.Trace("Created new account '{0}' = {1}.", company, newId);
            return newId;
        }

        // ================= contact creation =================

        private Guid CreateContact(LocalPluginContext localContext, IOrganizationService svc,
            Entity lead, Guid accountId)
        {
            string first = (lead.GetAttributeValue<string>(LeadFirstName) ?? string.Empty).Trim();
            string last  = (lead.GetAttributeValue<string>(LeadLastName) ?? string.Empty).Trim();
            string email = (lead.GetAttributeValue<string>(LeadEmail) ?? string.Empty).Trim();
            string phone = (lead.GetAttributeValue<string>(LeadPhone) ?? string.Empty).Trim();
            string mobile = (lead.GetAttributeValue<string>(LeadMobilePhone) ?? string.Empty).Trim();

            // Dataverse needs at least a last name to build a meaningful contact.
            if (last.Length == 0)
            {
                if (first.Length > 0) { last = first; first = string.Empty; }
                else if (email.Length > 0) last = email;   // last-resort label
                else last = "New Contact";
            }

            var contact = new Entity(ContactEntity);
            if (first.Length > 0)  contact[CFirstName] = first;
            contact[CLastName] = last;
            if (email.Length > 0)  contact[CEmail]  = email;
            if (phone.Length > 0)  contact[CPhone]  = phone;
            if (mobile.Length > 0) contact[CMobile] = mobile;
            if (accountId != Guid.Empty)
                contact[CParent] = new EntityReference(AccountEntity, accountId);

            // Provenance: this contact is being created from the lead.
            contact[OriginatingLead] = new EntityReference(LeadEntity, lead.Id);

            Guid id = svc.Create(contact);
            localContext.Trace("Created contact '{0} {1}' = {2}.", first, last, id);
            return id;
        }

        // ================= helpers =================

        private static Guid ResolveAccountOfContact(IOrganizationService svc, Guid contactId)
        {
            if (contactId == Guid.Empty) return Guid.Empty;
            try
            {
                Entity c = svc.Retrieve(ContactEntity, contactId,
                    new Microsoft.Xrm.Sdk.Query.ColumnSet(CParent));
                var parent = c.GetAttributeValue<EntityReference>(CParent);
                if (parent != null && string.Equals(parent.LogicalName, AccountEntity, StringComparison.Ordinal))
                    return parent.Id;
            }
            catch { /* contact may have no parent account */ }
            return Guid.Empty;
        }

        private static void SetOutputs(IPluginExecutionContext ctx, Guid contactId, Guid accountId)
        {
            ctx.OutputParameters[OutContactId] = contactId == Guid.Empty ? string.Empty : contactId.ToString();
            ctx.OutputParameters[OutAccountId] = accountId == Guid.Empty ? string.Empty : accountId.ToString();
        }

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

        private static string Clean(string raw)
        {
            return raw.Replace("{", string.Empty).Replace("}", string.Empty).Trim();
        }
    }
}
