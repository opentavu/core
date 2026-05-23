using System;
using Microsoft.Xrm.Sdk;
using OpenTavu.Dataverse.Common;

namespace Pl.Opportunity.LifecycleTracker
{
	/// <summary>
	/// Maintains derived lifecycle fields on tavu_opportunity records.
	///
	/// Currently handles:
	///   - tavu_stagechangedate: stamped whenever statuscode changes.
	///
	/// Designed to grow: additional lifecycle handlers (first activity stamping,
	/// stalled-deal flagging, reopen tracking) can be added as private methods
	/// invoked from ExecuteInternal without changing the registration.
	/// </summary>
	/// <remarks>
	/// Registration (Plugin Registration Tool):
	///   Message:              Update
	///   Primary Entity:       tavu_opportunity
	///   Filtering Attributes: statuscode
	///   Stage:                20 (Pre-operation)
	///   Execution Mode:       Synchronous
	///   Deployment:           Server
	///
	/// Why Pre-Operation: modifications to the Target entity are persisted by the
	/// same database write that the user/system originally triggered. No extra
	/// Update call, no transaction overhead, no recursion risk.
	/// </remarks>
	public class LifecycleTracker : PluginBase
	{
		// ----- Schema constants -----
		// Centralized here so any future schema rename is a single-line change.
		private const string TargetEntityName = "tavu_opportunity";
		private const string AttrStatusCode = "statuscode";
		private const string AttrStageChangeDate = "tavu_stagechangedate";

		public LifecycleTracker() : base(typeof(LifecycleTracker)) { }

		protected override void ExecuteInternal(LocalPluginContext localContext)
		{
			if (localContext == null)
				throw new ArgumentNullException(nameof(localContext));

			localContext.Trace("LifecycleTracker: ExecuteInternal entered.");

			// Guard 1: Target must exist and be an Entity (Update message contract).
			if (!(localContext.PluginExecutionContext.InputParameters.Contains("Target")
				  && localContext.PluginExecutionContext
								 .InputParameters["Target"] is Entity target))
			{
				localContext.Trace(
					"Target is missing or not an Entity. Exiting without changes.");
				return;
			}

			// Guard 2: defensive — registration already filters by entity, but if
			// someone misconfigures the step in the future, fail loudly in the trace
			// rather than mutating the wrong entity.
			if (!string.Equals(target.LogicalName, TargetEntityName,
							   StringComparison.Ordinal))
			{
				localContext.Trace(
					"Unexpected entity '{0}'. Plugin only handles '{1}'. Exiting.",
					target.LogicalName, TargetEntityName);
				return;
			}

			localContext.Trace(
				"Target acquired. Id={0}", target.Id);

			// Dispatch to handlers. Each handler is responsible for deciding
			// whether the current change is relevant to its own concern.
			HandleStageChangeDate(localContext, target);

			// Future handlers go here:
			//   HandleFirstActivityStamping(localContext, target);
			//   HandleStalledFlagging(localContext, target);
			//   HandleReopenTracking(localContext, target);

			localContext.Trace("LifecycleTracker: ExecuteInternal exiting.");
		}

		/// <summary>
		/// Stamps tavu_stagechangedate with the current UTC time when statuscode
		/// is part of the update. Writes directly to the Target entity so the
		/// change is persisted by the in-flight Dataverse write (Pre-Op pattern).
		/// </summary>
		private void HandleStageChangeDate(LocalPluginContext localContext,
										   Entity target)
		{
			localContext.Trace("HandleStageChangeDate: entered.");

			// Filtering attribute on the step guarantees this, but we re-check
			// because someone could later add other filtering attrs to the step.
			if (!target.Contains(AttrStatusCode))
			{
				localContext.Trace(
					"statuscode not present in Target attributes. Skipping.");
				return;
			}

			var newStatus = target.GetAttributeValue<OptionSetValue>(AttrStatusCode);
			localContext.Trace(
				"statuscode found in Target. New value: {0}",
				newStatus?.Value.ToString() ?? "null");

			// Edge case: if statuscode is being cleared (set to null) we still
			// record the timestamp — it reflects a real lifecycle event.
			var now = DateTime.UtcNow;
			target[AttrStageChangeDate] = now;

			localContext.Trace(
				"tavu_stagechangedate stamped on Target: {0:O}", now);
		}
	}
}