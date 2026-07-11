using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.Opportunity.CloseOrchestrator
{
    /// <summary>
    /// Post-Operation orchestrator for the close of a tavu_opportunity.
    ///
    /// Architecture: "opportunity is the source of truth" (Arch B). The ribbon
    /// Close Won / Close Lost dialog writes the terminal statuscode and the close
    /// inputs (actual revenue / lost reason / close date / notes) onto the
    /// opportunity in a single Update. Pl.Opportunity.LifecycleTracker (Pre-Op)
    /// validates those inputs and forces the probability (100 / 0). This plugin
    /// runs afterwards, in Post-Operation, to perform the DERIVED side effects that
    /// require the close to be committed and that touch OTHER records:
    ///
    ///   1. Creates a tavu_opportunityclose activity as the immutable historical
    ///      close log (Won = 576600001 / Lost = 576600002), regarding the opportunity.
    ///   2. On Won, marks the customer (Account for B2B, Contact for B2C) as a
    ///      customer: tavu_iscustomer = Yes (if not already) and tavu_customersince
    ///      = today (only if empty; never overwritten).
    ///
    /// Scope note (v1): engagement status (tavu_engagementstatus) is deliberately
    /// NOT touched here. That is communication state owned by Module 3 (AI Activity
    /// Capture), which derives it from real activity rather than as a hardcoded
    /// close side effect. Reopen (statuscode back to Open) is not logged in v1.
    /// </summary>
    /// <remarks>
    /// Plugin Registration Tool configuration:
    ///   Message:              Update
    ///   Primary Entity:       tavu_opportunity
    ///   Filtering Attributes: statuscode
    ///   Stage:                40 (Post-operation)
    ///   Execution Mode:       Synchronous
    ///   Deployment:           Server
    ///
    /// Why Post-Operation: the close log must reference a committed close, and the
    /// customer-status writes target other tables. Doing this Pre-Op would either
    /// see uncommitted state or require an extra self-Update.
    ///
    /// Why no recursion: this plugin never updates tavu_opportunity, so it does not
    /// re-trigger the opportunity pipeline. Its Create (close activity) and Update
    /// (account/contact) run one depth deeper and touch tables with no plugins that
    /// call back into the opportunity.
    ///
    /// SystemService is used for every write here: the close log and the customer
    /// flags are derived/audit data the triggering user must not be required to have
    /// direct write privileges on (imports, low-privilege sellers, Flow).
    /// </remarks>
    public class CloseOrchestrator : PluginBase
    {
        // ----- Schema constants -----
        private const string TargetEntityName = "tavu_opportunity";

        private const string AttrStatusCode = "statuscode";
        private const string AttrTopic = "tavu_topic";
        private const string AttrAccount = "tavu_account";
        private const string AttrContact = "tavu_contact";
        private const string AttrActualRevenue = "tavu_actualrevenue";
        private const string AttrActualCloseDate = "tavu_actualclosedate";
        private const string AttrLostReason = "tavu_lostreason";
        private const string AttrCloseNotes = "tavu_closenotes";

        // tavu_opportunity statuscode values.
        private const int OPP_STATUS_WON = 576600005;
        private const int OPP_STATUS_LOST = 576600006;

        // Close activity (tavu_opportunityclose) — its own state/status values.
        private const string CloseEntityName = "tavu_opportunityclose";
        private const int CLOSE_STATE_COMPLETED = 1;
        private const int CLOSE_STATUS_WON = 576600001;
        private const int CLOSE_STATUS_LOST = 576600002;
        private const string AttrRegarding = "regardingobjectid";
        private const string AttrSubject = "subject";
        private const string AttrDescription = "description";
        private const string AttrActualStart = "actualstart";
        private const string AttrStateCode = "statecode";

        // Customer status fields on account / contact.
        private const string EntityAccount = "account";
        private const string EntityContact = "contact";
        private const string AttrIsCustomer = "tavu_iscustomer";
        private const string AttrCustomerSince = "tavu_customersince";

        public CloseOrchestrator() : base(typeof(CloseOrchestrator)) { }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null)
                throw new ArgumentNullException(nameof(localContext));

            localContext.Trace("CloseOrchestrator: ExecuteInternal entered.");

            if (!(localContext.PluginExecutionContext.InputParameters.Contains("Target")
                  && localContext.PluginExecutionContext
                                 .InputParameters["Target"] is Entity target))
            {
                localContext.Trace("Target is missing or not an Entity. Exiting.");
                return;
            }

            if (!string.Equals(target.LogicalName, TargetEntityName, StringComparison.Ordinal))
            {
                localContext.Trace(
                    "Unexpected entity '{0}'. Plugin only handles '{1}'. Exiting.",
                    target.LogicalName, TargetEntityName);
                return;
            }

            // Only react to a terminal transition. statuscode is the filtering
            // attribute, so it is present whenever this step fires; anything other
            // than Won/Lost (e.g. a reopen back to Open) is a no-op in v1.
            var status = target.GetAttributeValue<OptionSetValue>(AttrStatusCode);
            if (status == null
                || (status.Value != OPP_STATUS_WON && status.Value != OPP_STATUS_LOST))
            {
                localContext.Trace(
                    "statuscode is not Won/Lost (value={0}). Nothing to orchestrate.",
                    status?.Value.ToString() ?? "null");
                return;
            }

            bool isWon = status.Value == OPP_STATUS_WON;
            localContext.Trace("Close detected. Outcome={0}. OpportunityId={1}",
                isWon ? "Won" : "Lost", target.Id);

            // Read the committed opportunity once, under SYSTEM, for everything we need.
            Entity opp = localContext.SystemService.Retrieve(
                TargetEntityName, target.Id,
                new ColumnSet(AttrTopic, AttrAccount, AttrContact,
                              AttrActualRevenue, AttrActualCloseDate,
                              AttrLostReason, AttrCloseNotes));

            CreateCloseLog(localContext, opp, isWon);

            if (isWon)
                MarkCustomer(localContext, opp);

            localContext.Trace("CloseOrchestrator: ExecuteInternal exiting.");
        }

        /// <summary>
        /// Creates the immutable tavu_opportunityclose activity that logs this close.
        /// </summary>
        private void CreateCloseLog(LocalPluginContext localContext, Entity opp, bool isWon)
        {
            localContext.Trace("CreateCloseLog: entered.");

            string topic = opp.GetAttributeValue<string>(AttrTopic) ?? "Opportunity";
            var closeDate = opp.GetAttributeValue<DateTime?>(AttrActualCloseDate)
                            ?? DateTime.UtcNow;

            var log = new Entity(CloseEntityName)
            {
                [AttrSubject] = string.Format("{0} - {1}", isWon ? "Won" : "Lost", topic),
                [AttrRegarding] = new EntityReference(TargetEntityName, opp.Id),
                [AttrActualCloseDate] = closeDate,
                [AttrActualStart] = closeDate,
                [AttrDescription] = opp.GetAttributeValue<string>(AttrCloseNotes)
            };

            if (isWon)
            {
                var revenue = opp.GetAttributeValue<Money>(AttrActualRevenue);
                if (revenue != null) log[AttrActualRevenue] = revenue;
            }
            else
            {
                var lostReason = opp.GetAttributeValue<OptionSetValue>(AttrLostReason);
                if (lostReason != null) log[AttrLostReason] = lostReason;
            }

            // Activities are always created in the Open state. Setting a Completed
            // statuscode (Won/Lost) during Create fails, because Dataverse validates
            // the status against the Open state. So create the log first, then
            // transition it to Completed + Won/Lost in a follow-up update.
            Guid logId = localContext.SystemService.Create(log);
            localContext.Trace("CreateCloseLog: activity created (Open). Id={0}", logId);

            var complete = new Entity(CloseEntityName, logId)
            {
                [AttrStateCode] = new OptionSetValue(CLOSE_STATE_COMPLETED),
                [AttrStatusCode] = new OptionSetValue(
                    isWon ? CLOSE_STATUS_WON : CLOSE_STATUS_LOST)
            };
            localContext.SystemService.Update(complete);
            localContext.Trace("CreateCloseLog: activity completed ({0}). Id={1}",
                isWon ? "Won" : "Lost", logId);
        }

        /// <summary>
        /// On a Won close, flags the commercial subject as a customer. The subject is
        /// the Account (B2B) or the Contact (B2C), read from the typed lookups that
        /// Pl.Opportunity.CustomerSync keeps in sync with the polymorphic customer.
        /// tavu_iscustomer is only ever set to Yes here (never back to No); customer-
        /// since is stamped only when empty so the original date is preserved.
        /// </summary>
        private void MarkCustomer(LocalPluginContext localContext, Entity opp)
        {
            localContext.Trace("MarkCustomer: entered.");

            EntityReference subject =
                opp.GetAttributeValue<EntityReference>(AttrAccount)
                ?? opp.GetAttributeValue<EntityReference>(AttrContact);

            if (subject == null)
            {
                localContext.Trace(
                    "No typed Account/Contact on opportunity. Skipping customer marking.");
                return;
            }

            localContext.Trace("Customer subject: {0} {1}", subject.LogicalName, subject.Id);

            Entity current = localContext.SystemService.Retrieve(
                subject.LogicalName, subject.Id,
                new ColumnSet(AttrIsCustomer, AttrCustomerSince));

            bool alreadyCustomer = current.GetAttributeValue<bool>(AttrIsCustomer);
            bool hasSince = current.Contains(AttrCustomerSince)
                            && current[AttrCustomerSince] != null;

            if (alreadyCustomer && hasSince)
            {
                localContext.Trace("Already a customer with a customer-since date. No change.");
                return;
            }

            var update = new Entity(subject.LogicalName, subject.Id);
            if (!alreadyCustomer) update[AttrIsCustomer] = true;
            if (!hasSince) update[AttrCustomerSince] = DateTime.UtcNow;

            localContext.SystemService.Update(update);
            localContext.Trace("MarkCustomer: {0} flagged as customer.", subject.LogicalName);
        }
    }
}
