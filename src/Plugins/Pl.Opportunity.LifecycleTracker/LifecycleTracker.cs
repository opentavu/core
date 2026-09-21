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
	///   - tavu_stagechangedate:  stamped whenever the Sales Stage is set or changed
	///                            (keyed off tavu_salesstage, not statuscode, so a
	///                            close/reopen does not corrupt tavu_daysinstage).
	///   - Close (Won/Lost):      validates the required close inputs (Actual Revenue
	///                            for Won, Lost Reason for Lost), defaults the close
	///                            date, forces tavu_probability to 100 / 0, and forces
	///                            tavu_forecastcategory to Closed.
	///   - Reopen (to Open):      re-applies the Sales Stage default probability AND
	///                            forecast category, and clears both manual flags.
	///   - tavu_probability:      otherwise defaulted from the selected Sales Stage's
	///                            tavu_defaultprobability, honoring the manual override
	///                            flag (tavu_probabilityismanual).
	///   - tavu_forecastcategory: otherwise defaulted from the selected Sales Stage's
	///                            tavu_forecastcategory, honoring its own manual override
	///                            flag (tavu_forecastcategoryismanual). Same pattern as
	///                            probability; see sales-model.md §6.3bis.
	///   - tavu_businessline:     defaulted on Create from
	///                            tavu_systemsettings.tavu_defaultbusinessline when the
	///                            seller did not pick one (feeds per-business-line
	///                            forecasting; single-practice firms get the seeded line).
	///
	/// The Post-Operation side effects of a close (historical close log, customer
	/// status marking) live in the separate Pl.Opportunity.CloseOrchestrator
	/// assembly. The forecasting target stamp (tavu_salestarget) lives in the separate
	/// Pl.Opportunity.ForecastStamp assembly so this plugin stays decoupled from the
	/// forecasting aggregate tables. This plugin only maintains fields on the
	/// opportunity itself.
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
	///                           tavu_probability, tavu_probabilityismanual,
	///                           tavu_forecastcategory, tavu_forecastcategoryismanual
	///     Stage:                20 (Pre-operation)
	///     Execution Mode:       Synchronous
	///     Deployment:           Server
	///     Pre-Image "PreImg":   tavu_salesstage, tavu_probability,
	///                           tavu_probabilityismanual, tavu_forecastcategory,
	///                           tavu_forecastcategoryismanual, statecode,
	///                           tavu_actualrevenue, tavu_lostreason
	///
	///   Step 2 — Create
	///     Message:              Create
	///     Primary Entity:       tavu_opportunity
	///     Stage:                20 (Pre-operation)
	///     Execution Mode:       Synchronous
	///     Deployment:           Server
	///     (No filtering attributes / no Pre-Image on Create. The business-line
	///      default runs on Create only.)
	///
	/// Why Pre-Operation: modifications to the Target entity are persisted by the
	/// same database write that the user/system originally triggered. No extra
	/// Update call, no transaction overhead, no recursion risk.
	///
	/// Probability and forecast-category defaulting are the server-side safety net for
	/// entry paths that do not run form scripts (bulk edit, data import, Power Automate,
	/// API). The form JS (OpenTavu.Opportunity.MainForm) handles the interactive path and
	/// always writes the manual flags explicitly, so on the form path this handler is
	/// deterministic. Reference: sales-model.md §6.3bis.
	///
	/// PREREQUISITES (Step A schema, forecasting build plan §3): this assembly reads and
	/// writes tavu_forecastcategory + tavu_forecastcategoryismanual + tavu_businessline on
	/// tavu_opportunity, and tavu_defaultbusinessline on tavu_systemsettings. Those columns
	/// must exist before build/register. The tavu_forecastcategory Choice on the opportunity
	/// must bind to the SAME global choice as the stage (tavu_forecastcategory:
	/// Pipeline 576600000, Best Case 576600001, Committed 576600002, Closed 576600003;
	/// add Omitted 576600004). tavu_businessline reuses the EXISTING tavu_businessline table.
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

		// Forecast-category defaulting (global choice tavu_forecastcategory, shared with the stage)
		private const string AttrForecastCategory = "tavu_forecastcategory";
		private const string AttrForecastCategoryIsManual = "tavu_forecastcategoryismanual";
		private const string AttrStageForecastCategory = "tavu_forecastcategory"; // on tavu_salesstage
		private const int FORECAST_CATEGORY_CLOSED = 576600003;

		// Business-line default (reuses the existing tavu_businessline table)
		private const string AttrBusinessLine = "tavu_businessline";
		private const string SettingsEntityName = "tavu_systemsettings";
		private const string AttrDefaultBusinessLine = "tavu_defaultbusinessline";

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

			// A close (Won/Lost) or reopen owns probability AND forecast category
			// explicitly, so it short-circuits the normal stage-based defaulting.
			bool transition = HandleCloseAndReopen(localContext, target);
			if (!transition)
			{
				ApplyProbabilityDefault(localContext, target);
				ApplyForecastCategoryDefault(localContext, target);
			}

			// Business-line default runs regardless of transition (Create only, guarded inside).
			ApplyBusinessLineDefault(localContext, target);

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
		/// Defaults tavu_forecastcategory from the selected Sales Stage's
		/// tavu_forecastcategory, honoring the seller's manual override
		/// (tavu_forecastcategoryismanual). Exact mirror of ApplyProbabilityDefault:
		/// the forecast category is a Choice (OptionSetValue) rather than an int, and
		/// both fields bind to the SAME global choice, so the stage value is copied
		/// verbatim (including Omitted, if a stage maps to it).
		///
		/// Mature commit-forecasting model (sales-model §6.3bis): the category defaults
		/// from the stage but the seller can commit or hold back a specific deal without
		/// changing its stage. "Reset Forecast Category to Stage Default" (which sets the
		/// flag back to false) is the only path back to auto mode.
		/// </summary>
		private void ApplyForecastCategoryDefault(LocalPluginContext localContext,
												  Entity target)
		{
			localContext.Trace("ApplyForecastCategoryDefault: entered.");

			var context = localContext.PluginExecutionContext;
			var service = localContext.SystemService;

			bool catProvided = target.Contains(AttrForecastCategory);
			bool stageProvided = target.Contains(AttrSalesStage);
			bool flagProvided = target.Contains(AttrForecastCategoryIsManual);
			bool isUpdate = string.Equals(
				context.MessageName, "Update", StringComparison.OrdinalIgnoreCase);

			Entity preImage = (isUpdate && context.PreEntityImages.Contains(PreImageName))
				? context.PreEntityImages[PreImageName]
				: null;

			// Non-form path: explicit category with no flag is a deliberate (manual) value.
			if (catProvided && !flagProvided)
			{
				localContext.Trace(
					"Explicit forecast category with no manual flag. Marking as manual override.");
				target[AttrForecastCategoryIsManual] = true;
				return;
			}

			bool manual = flagProvided
				? target.GetAttributeValue<bool>(AttrForecastCategoryIsManual)
				: (preImage != null && preImage.GetAttributeValue<bool>(AttrForecastCategoryIsManual));

			if (manual)
			{
				localContext.Trace("Manual override active. Leaving forecast category untouched.");
				return;
			}

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
				localContext.Trace("No Sales Stage present. Leaving forecast category untouched.");
				return;
			}

			var stage = service.Retrieve(
				StageEntityName, stageRef.Id, new ColumnSet(AttrStageForecastCategory));
			var cat = stage.GetAttributeValue<OptionSetValue>(AttrStageForecastCategory);

			if (cat != null)
			{
				localContext.Trace("Applying stage default forecast category = {0}.", cat.Value);
				target[AttrForecastCategory] = new OptionSetValue(cat.Value);
				target[AttrForecastCategoryIsManual] = false; // keep the flag coherent in auto mode
			}
			else
			{
				localContext.Trace(
					"Stage has no forecast category configured. Leaving forecast category as-is.");
			}
		}

		/// <summary>
		/// Defaults tavu_businessline on Create from the single tavu_systemsettings
		/// record's tavu_defaultbusinessline, when the seller did not choose one. This
		/// feeds per-business-line forecasting; single-practice firms get the seeded
		/// "General Practice" line automatically. Update is left untouched: changing a
		/// deal's business line is a deliberate seller action.
		/// </summary>
		private void ApplyBusinessLineDefault(LocalPluginContext localContext,
											  Entity target)
		{
			var context = localContext.PluginExecutionContext;
			bool isCreate = string.Equals(
				context.MessageName, "Create", StringComparison.OrdinalIgnoreCase);
			if (!isCreate)
				return;

			localContext.Trace("ApplyBusinessLineDefault: entered (Create).");

			// Already chosen by the seller?
			if (target.Contains(AttrBusinessLine)
				&& target.GetAttributeValue<EntityReference>(AttrBusinessLine) != null)
			{
				localContext.Trace("Business Line already provided. Skipping default.");
				return;
			}

			// Read the single System Settings record's default business line (SYSTEM:
			// config data a low-privilege creator may not have Read on).
			var query = new QueryExpression(SettingsEntityName)
			{
				ColumnSet = new ColumnSet(AttrDefaultBusinessLine),
				TopCount = 1
			};

			var settings = localContext.SystemService.RetrieveMultiple(query);
			if (settings.Entities.Count == 0)
			{
				localContext.Trace("No System Settings record found. Skipping business-line default.");
				return;
			}

			var def = settings.Entities[0].GetAttributeValue<EntityReference>(AttrDefaultBusinessLine);
			if (def != null)
			{
				target[AttrBusinessLine] = def;
				localContext.Trace("Business Line defaulted from System Settings: {0}.", def.Id);
			}
			else
			{
				localContext.Trace("System Settings has no default business line. Leaving empty.");
			}
		}

		/// <summary>
		/// Handles the two lifecycle transitions that own probability AND forecast
		/// category explicitly:
		///   - Close (Won/Lost): validates the required close inputs, defaults the
		///     close date if missing, forces probability to 100 (Won) / 0 (Lost), and
		///     forces forecast category to Closed.
		///   - Reopen (back to Open from a closed state): re-applies the current Sales
		///     Stage default probability and forecast category, and clears both flags.
		/// Returns true when it handled a transition, so the caller skips the normal
		/// stage-based defaulting for both fields.
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

				// A closed deal is in the Closed forecast category, whatever it was before.
				target[AttrForecastCategory] = new OptionSetValue(FORECAST_CATEGORY_CLOSED);
				target[AttrForecastCategoryIsManual] = true; // system-forced; block auto override
				localContext.Trace("Forecast category forced to Closed ({0}).", FORECAST_CATEGORY_CLOSED);
				return true;
			}

			if (status.Value == OPP_STATUS_OPEN && preImage != null)
			{
				var prevState = preImage.GetAttributeValue<OptionSetValue>(AttrStateCode);
				if (prevState != null && prevState.Value == STATE_INACTIVE)
				{
					localContext.Trace("Reopen detected. Re-applying stage default probability + category.");
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
		/// Re-applies the Sales Stage default probability AND forecast category on reopen
		/// and clears both manual flags so the opportunity returns to auto mode. Uses the
		/// stage on the Target if the reopen also set one, otherwise the stage from the
		/// Pre-Image.
		/// </summary>
		private void ReapplyStageDefault(LocalPluginContext localContext, Entity target,
										 Entity preImage)
		{
			EntityReference stageRef = target.Contains(AttrSalesStage)
				? target.GetAttributeValue<EntityReference>(AttrSalesStage)
				: preImage.GetAttributeValue<EntityReference>(AttrSalesStage);

			if (stageRef == null)
			{
				localContext.Trace("Reopen: no Sales Stage to resolve. Leaving fields as-is.");
				return;
			}

			var stage = localContext.SystemService.Retrieve(
				StageEntityName, stageRef.Id,
				new ColumnSet(AttrStageDefaultProbability, AttrStageForecastCategory));

			int? def = stage.GetAttributeValue<int?>(AttrStageDefaultProbability);
			if (def.HasValue)
			{
				target[AttrProbability] = def.Value;
				target[AttrProbabilityIsManual] = false;
				localContext.Trace("Reopen: probability reset to stage default = {0}.", def.Value);
			}

			var cat = stage.GetAttributeValue<OptionSetValue>(AttrStageForecastCategory);
			if (cat != null)
			{
				target[AttrForecastCategory] = new OptionSetValue(cat.Value);
				target[AttrForecastCategoryIsManual] = false;
				localContext.Trace("Reopen: forecast category reset to stage default = {0}.", cat.Value);
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
