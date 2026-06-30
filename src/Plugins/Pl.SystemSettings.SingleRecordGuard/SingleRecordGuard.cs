using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.SystemSettings.SingleRecordGuard
{
    /// <summary>
    /// Enforces the singleton rule for tavu_systemsettings: at most one record may exist.
    /// If a record already exists, any attempt to create another is blocked with a
    /// friendly error. This is the server-side guard behind the "open the single
    /// record directly" UX (the menu web resource hides the New button, but a guard
    /// at the platform level cannot be bypassed by API or imports).
    /// </summary>
    /// <remarks>
    /// Plugin Registration Tool configuration:
    ///   Message:              Create
    ///   Primary Entity:       tavu_systemsettings
    ///   Stage:                20 (Pre-operation)
    ///   Execution Mode:       Synchronous
    ///   Deployment:           Server
    /// </remarks>
    public class SingleRecordGuard : PluginBase
    {
        // ----- Schema constants -----
        private const string TargetEntityName = "tavu_systemsettings";

        /// <summary>
        /// This is a read-only validation that never writes (so it cannot recurse).
        /// Allow it to run at any pipeline depth so the singleton rule is always
        /// enforced, even for programmatic/import-driven creates.
        /// </summary>
        protected override int MaxDepth => 8;

        public SingleRecordGuard() : base(typeof(SingleRecordGuard)) { }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null)
                throw new ArgumentNullException(nameof(localContext));

            localContext.Trace("SingleRecordGuard: ExecuteInternal entered.");

            var ctx = localContext.PluginExecutionContext;

            // Only act on Create.
            if (!string.Equals(ctx.MessageName, "Create", StringComparison.OrdinalIgnoreCase))
            {
                localContext.Trace("Message is '{0}', not Create. Exiting.", ctx.MessageName);
                return;
            }

            // Guard 1: Target must exist and be an Entity.
            if (!(ctx.InputParameters.Contains("Target")
                  && ctx.InputParameters["Target"] is Entity target))
            {
                localContext.Trace("Target is missing or not an Entity. Exiting without changes.");
                return;
            }

            // Guard 2: defensive, registration already filters by entity.
            if (!string.Equals(target.LogicalName, TargetEntityName, StringComparison.Ordinal))
            {
                localContext.Trace(
                    "Unexpected entity '{0}'. Plugin only handles '{1}'. Exiting.",
                    target.LogicalName, TargetEntityName);
                return;
            }

            EnforceSingleRecord(localContext);

            localContext.Trace("SingleRecordGuard: ExecuteInternal exiting.");
        }

        /// <summary>
        /// Blocks the create when a tavu_systemsettings record already exists.
        /// Uses SystemService so the count is accurate regardless of the calling
        /// user's read privileges (a justified configuration-table read).
        /// </summary>
        private void EnforceSingleRecord(LocalPluginContext localContext)
        {
            localContext.Trace("EnforceSingleRecord: checking for an existing record.");

            // We only need to know whether at least one record exists: no columns, top 1.
            var query = new QueryExpression(TargetEntityName)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                NoLock = true
            };

            EntityCollection existing = localContext.SystemService.RetrieveMultiple(query);

            if (existing != null && existing.Entities.Count > 0)
            {
                localContext.Trace("An existing record was found. Blocking create.");
                throw new InvalidPluginExecutionException(
                    "Only one System Settings record is allowed. " +
                    "Open the existing System Settings record and edit it instead of creating a new one.");
            }

            localContext.Trace("No existing record found. Create allowed.");
        }
    }
}
