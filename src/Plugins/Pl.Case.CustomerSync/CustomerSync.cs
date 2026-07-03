using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.Case.CustomerSync
{
    /// <summary>
    /// Mirrors the polymorphic tavu_customer lookup on tavu_case to the typed lookups
    /// tavu_account / tavu_contact (so the Quick View forms, which bind to a specific
    /// entity, can load), auto-fills tavu_primarycontact when the customer is a Contact,
    /// and validates the customer entity type against the tenant-wide Customer Mode
    /// (B2B Only / B2C Only / Mixed) in the tavu_systemsettings singleton.
    ///
    /// Server-side so the rule applies to every write path (UI, import, Power Automate, API).
    /// This is the case-side twin of Pl.Opportunity.CustomerSync.
    /// </summary>
    /// <remarks>
    /// Plugin Registration:
    ///   Message:              Create AND Update (register one step per message)
    ///   Primary Entity:       tavu_case
    ///   Filtering Attributes: tavu_customer      (Update step only)
    ///   Stage:                20 (Pre-operation)
    ///   Execution Mode:       Synchronous
    ///   Deployment:           Server
    /// </remarks>
    public class CustomerSync : PluginBase
    {
        // ----- Schema constants -----
        private const string TargetEntityName = "tavu_case";

        private const string AttrCustomer       = "tavu_customer";
        private const string AttrAccount        = "tavu_account";
        private const string AttrContact        = "tavu_contact";
        private const string AttrPrimaryContact = "tavu_primarycontact";

        private const string EntityAccount = "account";
        private const string EntityContact = "contact";

        private const string SettingsEntityName = "tavu_systemsettings";
        private const string AttrCustomerMode   = "tavu_customermode";

        // tavu_customermode option set values
        private const int MODE_B2B_ONLY = 576600000;
        private const int MODE_B2C_ONLY = 576600001;
        private const int MODE_MIXED    = 576600002;

        public CustomerSync() : base(typeof(CustomerSync)) { }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null) throw new ArgumentNullException(nameof(localContext));

            localContext.Trace("CustomerSync: ExecuteInternal entered.");

            if (!(localContext.PluginExecutionContext.InputParameters.Contains("Target")
                  && localContext.PluginExecutionContext.InputParameters["Target"] is Entity target))
            {
                localContext.Trace("Target missing or not an Entity. Exiting.");
                return;
            }

            if (!string.Equals(target.LogicalName, TargetEntityName, StringComparison.Ordinal))
            {
                localContext.Trace("Unexpected entity '{0}'. Exiting.", target.LogicalName);
                return;
            }

            HandleCustomerMirroring(localContext, target);

            localContext.Trace("CustomerSync: ExecuteInternal exiting.");
        }

        /// <summary>
        /// If tavu_customer is part of the write, validates its type against Customer Mode
        /// and mirrors it to the matching typed lookup (clearing the opposite). When the
        /// customer is a Contact, also sets tavu_primarycontact to that contact.
        /// </summary>
        private void HandleCustomerMirroring(LocalPluginContext localContext, Entity target)
        {
            localContext.Trace("HandleCustomerMirroring: entered.");

            // On Update, tavu_customer is only present if it actually changed.
            if (!target.Contains(AttrCustomer))
            {
                localContext.Trace("tavu_customer not in Target. Skipping.");
                return;
            }

            var customerRef = target.GetAttributeValue<EntityReference>(AttrCustomer);

            // Customer cleared: null both typed lookups in the same write.
            if (customerRef == null)
            {
                localContext.Trace("Customer cleared. Nulling typed lookups.");
                target[AttrAccount] = null;
                target[AttrContact] = null;
                return;
            }

            localContext.Trace("Customer set. Type='{0}', Id={1}", customerRef.LogicalName, customerRef.Id);

            int mode = GetCustomerMode(localContext);
            if (!IsCustomerTypeAllowed(customerRef.LogicalName, mode))
            {
                string message = BuildRejectionMessage(customerRef.LogicalName, mode);
                localContext.Trace("Customer type rejected by mode {0}. Message: {1}", mode, message);
                throw new InvalidPluginExecutionException(message);
            }

            if (string.Equals(customerRef.LogicalName, EntityAccount, StringComparison.Ordinal))
            {
                target[AttrAccount] = new EntityReference(EntityAccount, customerRef.Id);
                target[AttrContact] = null;
                // Per service model: for a B2B (Account) case, tavu_primarycontact is NOT
                // auto-populated (it may hold a manually chosen interlocutor). Leave it alone.
                localContext.Trace("Mirrored to tavu_account. tavu_contact cleared.");
            }
            else if (string.Equals(customerRef.LogicalName, EntityContact, StringComparison.Ordinal))
            {
                target[AttrContact] = new EntityReference(EntityContact, customerRef.Id);
                target[AttrAccount] = null;
                // B2C (Contact) case: the contact is also the primary human interlocutor.
                target[AttrPrimaryContact] = new EntityReference(EntityContact, customerRef.Id);
                localContext.Trace("Mirrored to tavu_contact. tavu_account cleared. tavu_primarycontact set.");
            }
            else
            {
                localContext.Trace("Unexpected customer type '{0}'. Not mirroring.", customerRef.LogicalName);
            }

            localContext.Trace("HandleCustomerMirroring: exiting.");
        }

        /// <summary>
        /// Reads tavu_customermode from the tavu_systemsettings singleton. Falls back to
        /// MIXED (most permissive) if missing/null, so it never blocks a write on misconfig.
        /// </summary>
        private int GetCustomerMode(LocalPluginContext localContext)
        {
            var query = new QueryExpression(SettingsEntityName)
            {
                ColumnSet = new ColumnSet(AttrCustomerMode),
                TopCount = 1,
                NoLock = true
            };

            var result = localContext.SystemService.RetrieveMultiple(query);
            if (result.Entities.Count == 0)
            {
                localContext.Trace("No tavu_systemsettings row. Defaulting to MIXED.");
                return MODE_MIXED;
            }

            var modeValue = result.Entities[0].GetAttributeValue<OptionSetValue>(AttrCustomerMode);
            if (modeValue == null)
            {
                localContext.Trace("tavu_customermode is null. Defaulting to MIXED.");
                return MODE_MIXED;
            }

            localContext.Trace("Customer Mode resolved: {0}", modeValue.Value);
            return modeValue.Value;
        }

        private bool IsCustomerTypeAllowed(string entityType, int mode)
        {
            if (mode == MODE_MIXED) return true;
            if (mode == MODE_B2B_ONLY) return entityType == EntityAccount;
            if (mode == MODE_B2C_ONLY) return entityType == EntityContact;
            return true; // unknown mode: permissive
        }

        private string BuildRejectionMessage(string attemptedType, int mode)
        {
            string typeLabel = attemptedType == EntityAccount ? "an Account" : "a Contact";
            string modeLabel = mode == MODE_B2B_ONLY ? "B2B Only" : "B2C Only";
            string allowedLabel = mode == MODE_B2B_ONLY ? "Accounts" : "Contacts";

            return string.Format(
                "This system is configured in {0} mode and only allows {1} as customers. " +
                "You attempted to set {2}. Please select a {1} customer.",
                modeLabel, allowedLabel, typeLabel);
        }
    }
}
