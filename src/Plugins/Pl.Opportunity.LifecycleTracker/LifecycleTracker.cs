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
	///   - tavu_stagechangedate: stamped whenever the Sales Stage is set or changed
	///                           (keyed off tavu_salesstage, not statuscode, so a
	///                           close/reopen does not corrupt tavu_daysinstage).
	///   - Close (Won/Lost):     validates the required close inputs (Actual Revenue
	///                           for Won, Lost Reason for Lost), defaults the close
	///                           date, and forces tavu_probability to 100 / 0.
	///   - Reopen (to Open):     re-applies the Sales Stage default probability and
	///                           clears the manual flag.
	///   - tavu_probability:     otherwise defaulted from the selected Sales Stage's
	///                           tavu_defaultprobability, honoring the manual
	///                           override flag (tavu_probabilityismanual).
	///
	/// The Post-Operation side effects of a close (historical close log, customer
	/// status marking) live in the separate Pl.Opportunity.CloseOrchestrator
	/// assembly. This plugin only maintains fields on the opportunity itself.
	///
	/// Designed to grow: additional lifecycle handlers (first activity stamping,
	/// stalled-deal flagging) can be added as private methods invoked from
	/// ExecuteInternal without changing the registration.
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
	///                           tavu_probabilityismanual, statecode,
	///                           tavu_actualrevenue, tavu_lostreason
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

		// Lifecycle transitions (close / reopen)
		private const string AttrStateCode = "statecode";
		private const string AttrActualCloseDate = "tavu_actualclosedate";
		private const string AttrActualRevenue = "tavu_actualrevenue";
		private const string AttrLostReason = "tavu_lostreason";
		private const int OPP_STATUS_OPEN = 576600001;
		private const int OPP_STATUS_WON = 576600005;
		private const int OPP_STATUS_LOST = 576600006;
		private const int STATE_INACTIVE = 1;

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

			// A close (Won/Lost) or reopen owns the probability explicitly, so it
			// short-circuits the normal stage-based probability defaulting.
			if (!HandleCloseAndReopen(localContext, target))
			{
				ApplyProbabilityDefault(localContext, target);
			}

			// Future handlers go here:
			//   HandleFirstActivityStamping(localContext, target);
			//   HandleStalledFlagging(localContext, target);

			localContext.Trace("LifecycleTracker: ExecuteInternal exiting.");
		}

		/// <summary>
		/// Stamps tavu_stagechangedate with the current UTC time when the Sales Stage
		/// is set or changed (present in Target on Create, or changed on Update).
		/// Writes directly to the Target so the change is persisted by the in-flight
		/// Dataverse write (Pre-Op pattern).
		///
		/// Deliberately keyed off tavu_salesstage, NOT statuscode: this field feeds
		/// tavu_daysinstage, so a close or reopen (which changes statuscode but not
		/// the stage) must not reset it and corrupt the time-in-stage metric.
		/// </summary>
		private void HandleStageChangeDate(LocalPluginContext localContext,
										   Entity target)
		{
			localContext.Trace("HandleStageChangeDate: entered.");

			if (!target.Contains(AttrSalesStage))
			{
				localContext.Trace(
					"tavu_salesstage not present in Target. Skipping stage-date stamp.");
				return;
			}

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

		/// <summary>
		/// Handles the two lifecycle transitions that own probability explicitly:
		///   - Close (Won/Lost): validates the required close inputs, defaults the
		///     close date if missing, and forces probability to 100 (Won) or 0 (Lost).
		///   - Reopen (back to Open from a closed state): re-applies the current Sales
		///     Stage default probability and clears the manual flag.
		/// Returns true when it handled a transition, so the caller skips the normal
		/// stage-based probability defaulting.
		/// </summary>
		private bool HandleCloseAndReopen(LocalPluginContext localContext, Entity target)
		{
			if (!target.Contains(AttrStatusCode)) return false;

			var status = target.GetAttributeValue<OptionSetValue>(AttrStatusCode);
			if (status == null) return false;

			var context = localContext.PluginExecutionContext;
			Entity preImage = context.PreEntityImages.Contains(PreImageName)
				? context.PreEntityImages[PreImageName]
				: null;

			if (status.Value == OPP_STATUS_WON || status.Value == OPP_STATUS_LOST)
			{
				bool isWon = status.Value == OPP_STATUS_WON;
				localContext.Trace("Close transition detected. Outcome={0}.",
					isWon ? "Won" : "Lost");

				ValidateCloseInputs(localContext, target, preImage, isWon);

				// Default the close date if the caller did not supply one.
				if (GetEffective<DateTime?>(target, preImage, AttrActualCloseDate) == null)
				{
					target[AttrActualCloseDate] = DateTime.UtcNow;
					localContext.Trace("Actual close date defaulted to now.");
				}

				target[AttrProbability] = isWon ? 100 : 0;
				target[AttrProbabilityIsManual] = true; // system-forced; block auto override
				localContext.Trace("Probability forced to {0}.", isWon ? 100 : 0);
				return true;
			}

			if (status.Value == OPP_STATUS_OPEN && preImage != null)
			{
				var prevState = preImage.GetAttributeValue<OptionSetValue>(AttrStateCode);
				if (prevState != null && prevState.Value == STATE_INACTIVE)
				{
					localContext.Trace("Reopen detected. Re-applying stage default probability.");
					ReapplyStageDefault(localContext, target, preImage);
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Enforces the minimum close contract regardless of entry path (form, Flow,
		/// import, API): a Won close needs an Actual Revenue greater than zero, a Lost
		/// close needs a Lost Reason. Values may arrive in the Target (typical) or
		/// already exist on the record (Pre-Image).
		/// </summary>
		private void ValidateCloseInputs(LocalPluginContext localContext, Entity target,
										 Entity preImage, bool isWon)
		{
			if (isWon)
			{
				var revenue = GetEffective<Money>(target, preImage, AttrActualRevenue);
				if (revenue == null || revenue.Value <= 0m)
				{
					throw new InvalidPluginExecutionException(
						"Closing this opportunity as Won requires an Actual Revenue greater than zero.");
				}
			}
			else
			{
				var lostReason = GetEffective<OptionSetValue>(target, preImage, AttrLostReason);
				if (lostReason == null)
				{
					throw new InvalidPluginExecutionException(
						"Closing this opportunity as Lost requires a Lost Reason.");
				}
			}
		}

		/// <summary>
		/// Re-applies the Sales Stage default probability on reopen and clears the
		/// manual flag so the opportunity returns to auto mode. Uses the stage on the
		/// Target if the reopen also set one, otherwise the stage from the Pre-Image.
		/// </summary>
		private void ReapplyStageDefault(LocalPluginContext localContext, Entity target,
										 Entity preImage)
		{
			EntityReference stageRef = target.Contains(AttrSalesStage)
				? target.GetAttributeValue<EntityReference>(AttrSalesStage)
				: preImage.GetAttributeValue<EntityReference>(AttrSalesStage);

			if (stageRef == null)
			{
				localContext.Trace("Reopen: no Sales Stage to resolve. Leaving probability as-is.");
				return;
			}

			var stage = localContext.SystemService.Retrieve(
				StageEntityName, stageRef.Id, new ColumnSet(AttrStageDefaultProbability));
			int? def = stage.GetAttributeValue<int?>(AttrStageDefaultProbability);

			if (def.HasValue)
			{
				target[AttrProbability] = def.Value;
				target[AttrProbabilityIsManual] = false;
				localContext.Trace("Reopen: probability reset to stage default = {0}.", def.Value);
			}
		}

		/// <summary>
		/// Returns the attribute value from the Target if present, otherwise from the
		/// Pre-Image, otherwise default(T).
		/// </summary>
		private static T GetEffective<T>(Entity target, Entity preImage, string attribute)
		{
			if (target.Contains(attribute))
				return target.GetAttributeValue<T>(attribute);
			if (preImage != null && preImage.Contains(attribute))
				return preImage.GetAttributeValue<T>(attribute);
			return default(T);
		}
	}
}