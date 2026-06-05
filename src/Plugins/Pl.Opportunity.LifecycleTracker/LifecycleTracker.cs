using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.Opportunity.LifecycleTracker
{
	/// <summary>
	/// Maintains derived lifecycle fields on tavu_opportunity records.
	///
	/// Currently handles:
	///   - tavu_stagechangedate: stamped whenever statuscode changes.
	///   - tavu_probability:     defaulted from the selected Sales Stage's
	///                           tavu_defaultprobability, honoring the manual
	///                           override flag (tavu_probabilityismanual).
	///
	/// Designed to grow: additional lifecycle handlers (first activity stamping,
	/// stalled-deal flagging, reopen tracking) can be added as private methods
	/// invoked from ExecuteInternal without changing the registration.
	/// </summary>
	/// <remarks>
	/// Registration (Plugin Registration Tool) — TWO steps share this assembly:
	///
	///   Step 1 — Update
	///     Message:              Update
	///     Primary Entity:       tavu_opportunity
	///     Filtering Attributes: statuscode, tavu_salesstage,
	///                           tavu_probability, tavu_probabilityismanual
	///     Stage:                20 (Pre-operation)
	///     Execution Mode:       Synchronous
	///     Deployment:           Server
	///     Pre-Image "PreImg":   tavu_salesstage, tavu_probability,
	///                           tavu_probabilityismanual
	///
	///   Step 2 — Create
	///     Message:              Create
	///     Primary Entity:       tavu_opportunity
	///     Stage:                20 (Pre-operation)
	///     Execution Mode:       Synchronous
	///     Deployment:           Server
	///     (No filtering attributes / no Pre-Image on Create.)
	///
	/// Why Pre-Operation: modifications to the Target entity are persisted by the
	/// same database write that the user/system originally triggered. No extra
	/// Update call, no transaction overhead, no recursion risk.
	///
	/// Probability defaulting is the server-side safety net for entry paths that
	/// do not run form scripts (bulk edit, data import, Power Automate, API). The
	/// form JS (OpenTavu.Opportunity.MainForm) handles the interactive path and
	/// always writes the manual flag explicitly, so on the form path this handler
	/// is deterministic. Reference: sales-model.md §6.3bis.
	/// </remarks>
	public class LifecycleTracker : PluginBase
	{
		// ----- Schema constants -----
		// Centralized here so any future schema rename is a single-line change.
		private const string TargetEntityName = "tavu_opportunity";
		private const string AttrStatusCode = "statuscode";
		private const string AttrStageChangeDate = "tavu_stagechangedate";

		// Probability defaulting
		private const string AttrSalesStage = "tavu_salesstage";
		private const string AttrProbability = "tavu_probability";
		private const string AttrProbabilityIsManual = "tavu_probabilityismanual";
		private const string StageEntityName = "tavu_salesstage";
		private const string AttrStageDefaultProbability = "tavu_defaultprobability";
		private const string PreImageName = "PreImg";

		public LifecycleTracker() : base(typeof(LifecycleTracker)) { }

		protected override void ExecuteInternal(LocalPluginContext localContext)
		{
			if (localContext == null)
				throw new ArgumentNullException(nameof(localContext));

			localContext.Trace("LifecycleTracker: ExecuteInternal entered.");

			// Guard 1: Target must exist and be an Entity (Create/Update contract).
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
			ApplyProbabilityDefault(localContext, target);

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

		/// <summary>
		/// Defaults tavu_probability from the selected Sales Stage's
		/// tavu_defaultprobability, while honoring a consultant's manual override
		/// (tavu_probabilityismanual).
		///
		/// JS/plugin contract:
		///   - Form path:     probability AND manual flag both arrive explicitly,
		///                    so no inference is needed.
		///   - Non-form path: the flag is absent. An explicitly provided
		///                    probability is treated as a deliberate (manual) value.
		///
		/// Sticky override: once manual is true, a stage change does NOT overwrite
		/// the probability. The "Reset to Stage Default" ribbon button (which sets
		/// the flag back to false) is the only path back to auto mode.
		/// </summary>
		private void ApplyProbabilityDefault(LocalPluginContext localContext,
											 Entity target)
		{
			localContext.Trace("ApplyProbabilityDefault: entered.");

			var context = localContext.PluginExecutionContext;

			// SystemService (not UserService): tavu_salesstage is configuration data.
			// Reading it under SYSTEM privileges guarantees the default resolves on
			// every path — including imports or integrations run by low-privilege
			// users who may lack Read on tavu_salesstage. No data exposure: we only
			// read a default probability value.
			var service = localContext.SystemService;

			bool probProvided = target.Contains(AttrProbability);
			bool stageProvided = target.Contains(AttrSalesStage);
			bool flagProvided = target.Contains(AttrProbabilityIsManual);
			bool isUpdate = string.Equals(
				context.MessageName, "Update", StringComparison.OrdinalIgnoreCase);

			Entity preImage = (isUpdate && context.PreEntityImages.Contains(PreImageName))
				? context.PreEntityImages[PreImageName]
				: null;

			// Non-form path (bulk edit, import, Flow, API): an explicit probability
			// with no flag is a deliberate value. The form JS always writes the flag,
			// so this branch only fires off-form. Treat as manual and respect it.
			if (probProvided && !flagProvided)
			{
				localContext.Trace(
					"Explicit probability with no manual flag. Marking as manual override.");
				target[AttrProbabilityIsManual] = true;
				return;
			}

			// Effective manual flag: Target wins, else Pre-Image, else false.
			bool manual = flagProvided
				? target.GetAttributeValue<bool>(AttrProbabilityIsManual)
				: (preImage != null && preImage.GetAttributeValue<bool>(AttrProbabilityIsManual));

			if (manual)
			{
				localContext.Trace("Manual override active. Leaving probability untouched.");
				return;
			}

			// Auto mode. On Update, only act when the stage actually changed (it is
			// in Target); on Create, act for whatever stage was provided.
			if (isUpdate && !stageProvided)
			{
				localContext.Trace("Auto mode but stage unchanged. Nothing to do.");
				return;
			}

			EntityReference stageRef = stageProvided
				? target.GetAttributeValue<EntityReference>(AttrSalesStage)
				: (preImage != null ? preImage.GetAttributeValue<EntityReference>(AttrSalesStage) : null);

			if (stageRef == null)
			{
				localContext.Trace("No Sales Stage present. Leaving probability untouched.");
				return;
			}

			var stage = service.Retrieve(
				StageEntityName, stageRef.Id, new ColumnSet(AttrStageDefaultProbability));
			int? def = stage.GetAttributeValue<int?>(AttrStageDefaultProbability);

			if (def.HasValue)
			{
				localContext.Trace("Applying stage default probability = {0}.", def.Value);
				target[AttrProbability] = def.Value;
				target[AttrProbabilityIsManual] = false; // keep the flag coherent in auto mode
			}
			else
			{
				localContext.Trace(
					"Stage has no default probability configured. Leaving probability as-is.");
			}
		}
	}
}