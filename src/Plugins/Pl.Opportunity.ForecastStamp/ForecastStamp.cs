using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.Opportunity.ForecastStamp
{
	/// <summary>
	/// Stamps the system-managed tavu_salestarget lookup on tavu_opportunity, linking each
	/// deal to the one Sales Target whose (User scope, owner, period, Revenue goal type,
	/// business line) it belongs to. That link is the 1:N relationship the native rollups on
	/// tavu_salestarget aggregate over, so without this stamp the attainment numbers stay 0.
	///
	/// Matching (forecasting build plan §6c):
	///   - effective date = actual close date when Won, else estimated close date.
	///   - period = the active tavu_salesperiod of the firm's cadence
	///     (tavu_systemsettings.tavu_forecastperiodtype, Month/Quarter) that contains that date.
	///   - target = the active User-scope, Revenue tavu_salestarget for (owner, period), preferring
	///     the one whose business line matches the deal's, else the one with no business line.
	/// If nothing matches (no period, no quota row yet, team-owned, or no date), the stamp is cleared.
	///
	/// Deliberately a separate assembly from Pl.Opportunity.LifecycleTracker so the core lifecycle
	/// plugin stays decoupled from the forecasting aggregate tables.
	/// </summary>
	/// <remarks>
	/// PRESCRIBED OPTION VALUES (forecasting-step-a-guide.md §3.0, verify in the environment):
	///   tavu_scope.User = 576600000; tavu_periodtype Quarter = 576600001 (fallback);
	///   opportunity statuscode Won = 576600005.
	///
	/// Registration (Plugin Registration Tool) — TWO steps share this assembly. Set the Execution
	/// Order AFTER Pl.Opportunity.LifecycleTracker so the business-line default is already on the
	/// Target when this reads it.
	///
	///   Step 1 — Create
	///     Message: Create; Primary Entity: tavu_opportunity; Stage: 20 (Pre-operation);
	///     Mode: Synchronous; Deployment: Server. (No Pre-Image on Create.)
	///
	///   Step 2 — Update
	///     Message: Update; Primary Entity: tavu_opportunity; Stage: 20 (Pre-operation);
	///     Mode: Synchronous; Deployment: Server.
	///     Filtering Attributes: ownerid, tavu_estimatedclosedate, tavu_actualclosedate,
	///                           tavu_businessline, statuscode, statecode
	///     Pre-Image "PreImg":   ownerid, tavu_estimatedclosedate, tavu_actualclosedate,
	///                           tavu_businessline, statuscode, statecode
	///
	/// NOTE: a pure reassignment fires the Assign message, not Update, so it is not caught here.
	/// The weekly snapshot flow re-stamps open and recently-won deals as the backstop (or add an
	/// Assign step later). SystemService is used: these are config/system reads + a derived write.
	/// </remarks>
	public class ForecastStamp : PluginBase
	{
		// Opportunity
		private const string TargetEntityName = "tavu_opportunity";
		private const string AttrSalesTarget = "tavu_salestarget";
		private const string AttrOwner = "ownerid";
		private const string AttrEstClose = "tavu_estimatedclosedate";
		private const string AttrActClose = "tavu_actualclosedate";
		private const string AttrBusinessLine = "tavu_businessline";
		private const string AttrStatusCode = "statuscode";
		private const string PreImageName = "PreImg";
		private const int OPP_STATUS_WON = 576600005;

		// System Settings
		private const string SettingsEntityName = "tavu_systemsettings";
		private const string AttrForecastPeriodType = "tavu_forecastperiodtype";
		private const int PERIODTYPE_QUARTER = 576600001; // fallback if System Settings is blank

		// Sales Period
		private const string PeriodEntityName = "tavu_salesperiod";
		private const string AttrPeriodType = "tavu_periodtype";
		private const string AttrPeriodStart = "tavu_startdate";
		private const string AttrPeriodEnd = "tavu_enddate";

		// Goal Type (Phase 1 target = Revenue)
		private const string GoalTypeEntityName = "tavu_goaltype";
		private const string AttrName = "tavu_name";
		private const string RevenueGoalTypeName = "Revenue";

		// Sales Target
		private const string SalesTargetEntityName = "tavu_salestarget";
		private const string AttrScope = "tavu_scope";
		private const string AttrTargetUser = "tavu_salesrep";
		private const string AttrTargetPeriod = "tavu_salesperiod";
		private const string AttrTargetGoalType = "tavu_goaltype";
		private const string AttrTargetBusinessLine = "tavu_businessline";
		private const int SCOPE_USER = 576600000;

		private const int STATE_ACTIVE = 0;

		public ForecastStamp() : base(typeof(ForecastStamp)) { }

		protected override void ExecuteInternal(LocalPluginContext localContext)
		{
			if (localContext == null)
				throw new ArgumentNullException(nameof(localContext));

			localContext.Trace("ForecastStamp: ExecuteInternal entered.");

			if (!(localContext.PluginExecutionContext.InputParameters.Contains("Target")
				  && localContext.PluginExecutionContext.InputParameters["Target"] is Entity target))
			{
				localContext.Trace("Target is missing or not an Entity. Exiting.");
				return;
			}

			if (!string.Equals(target.LogicalName, TargetEntityName, StringComparison.Ordinal))
			{
				localContext.Trace("Unexpected entity '{0}'. Exiting.", target.LogicalName);
				return;
			}

			var context = localContext.PluginExecutionContext;
			var service = localContext.SystemService; // config reads + derived write
			Entity preImage = context.PreEntityImages.Contains(PreImageName)
				? context.PreEntityImages[PreImageName]
				: null;

			// 1) Owner must be a user (Phase 1 stamps to User-scope targets only).
			var ownerRef = GetEffective<EntityReference>(target, preImage, AttrOwner);
			if (ownerRef == null || !string.Equals(ownerRef.LogicalName, "systemuser", StringComparison.Ordinal))
			{
				localContext.Trace("Owner is not a user (or missing). Clearing stamp.");
				target[AttrSalesTarget] = null;
				return;
			}

			// 2) Effective close date: actual when Won, else estimated.
			var status = GetEffective<OptionSetValue>(target, preImage, AttrStatusCode);
			bool won = status != null && status.Value == OPP_STATUS_WON;
			DateTime? effDate = won
				? GetEffective<DateTime?>(target, preImage, AttrActClose)
				: GetEffective<DateTime?>(target, preImage, AttrEstClose);
			if (!effDate.HasValue)
				effDate = GetEffective<DateTime?>(target, preImage, AttrEstClose); // last resort

			if (!effDate.HasValue)
			{
				localContext.Trace("No effective close date. Clearing stamp.");
				target[AttrSalesTarget] = null;
				return;
			}

			// 3) Firm cadence period type (Month/Quarter), from System Settings, Quarter fallback.
			int periodType = ResolvePeriodType(service, localContext);

			// 4) Find the active period that contains the effective date.
			Entity period = RetrieveFirst(service, new QueryExpression(PeriodEntityName)
			{
				ColumnSet = new ColumnSet(false),
				TopCount = 1,
				Criteria = Filter(
					Cond(AttrPeriodType, ConditionOperator.Equal, periodType),
					Cond(AttrPeriodStart, ConditionOperator.LessEqual, effDate.Value),
					Cond(AttrPeriodEnd, ConditionOperator.GreaterEqual, effDate.Value),
					Cond("statecode", ConditionOperator.Equal, STATE_ACTIVE))
			});
			if (period == null)
			{
				localContext.Trace("No active sales period contains {0:d}. Clearing stamp.", effDate.Value);
				target[AttrSalesTarget] = null;
				return;
			}

			// 5) Resolve the Revenue goal type id.
			Entity goalType = RetrieveFirst(service, new QueryExpression(GoalTypeEntityName)
			{
				ColumnSet = new ColumnSet(false),
				TopCount = 1,
				Criteria = Filter(
					Cond(AttrName, ConditionOperator.Equal, RevenueGoalTypeName),
					Cond("statecode", ConditionOperator.Equal, STATE_ACTIVE))
			});
			if (goalType == null)
			{
				localContext.Trace("No active 'Revenue' goal type found. Clearing stamp.");
				target[AttrSalesTarget] = null;
				return;
			}

			// 6) Candidate User-scope Revenue targets for (owner, period); choose by business line.
			var blRef = GetEffective<EntityReference>(target, preImage, AttrBusinessLine);
			Guid? oppBusinessLine = blRef == null ? (Guid?)null : blRef.Id;

			var query = new QueryExpression(SalesTargetEntityName)
			{
				ColumnSet = new ColumnSet(AttrTargetBusinessLine),
				TopCount = 50,
				Criteria = Filter(
					Cond(AttrScope, ConditionOperator.Equal, SCOPE_USER),
					Cond(AttrTargetUser, ConditionOperator.Equal, ownerRef.Id),
					Cond(AttrTargetPeriod, ConditionOperator.Equal, period.Id),
					Cond(AttrTargetGoalType, ConditionOperator.Equal, goalType.Id),
					Cond("statecode", ConditionOperator.Equal, STATE_ACTIVE))
			};

			var candidates = service.RetrieveMultiple(query).Entities;

			Entity chosen = null;
			Entity nullLineFallback = null;
			foreach (var t in candidates)
			{
				var bl = t.GetAttributeValue<EntityReference>(AttrTargetBusinessLine);
				if (oppBusinessLine.HasValue && bl != null && bl.Id == oppBusinessLine.Value)
				{
					chosen = t; // exact business-line match wins
					break;
				}
				if (bl == null && nullLineFallback == null)
					nullLineFallback = t; // remember the no-business-line target as fallback
			}
			if (chosen == null)
				chosen = nullLineFallback;

			if (chosen != null)
			{
				target[AttrSalesTarget] = new EntityReference(SalesTargetEntityName, chosen.Id);
				localContext.Trace("Stamped tavu_salestarget = {0}.", chosen.Id);
			}
			else
			{
				target[AttrSalesTarget] = null;
				localContext.Trace("No matching Sales Target (rep has no quota row yet). Cleared stamp.");
			}

			localContext.Trace("ForecastStamp: ExecuteInternal exiting.");
		}

		private int ResolvePeriodType(IOrganizationService service, LocalPluginContext localContext)
		{
			var settings = service.RetrieveMultiple(new QueryExpression(SettingsEntityName)
			{
				ColumnSet = new ColumnSet(AttrForecastPeriodType),
				TopCount = 1
			});
			if (settings.Entities.Count > 0)
			{
				var pt = settings.Entities[0].GetAttributeValue<OptionSetValue>(AttrForecastPeriodType);
				if (pt != null) return pt.Value;
			}
			localContext.Trace("Forecast period type not set; defaulting to Quarter.");
			return PERIODTYPE_QUARTER;
		}

		private static Entity RetrieveFirst(IOrganizationService service, QueryExpression query)
		{
			var result = service.RetrieveMultiple(query);
			return result.Entities.Count > 0 ? result.Entities[0] : null;
		}

		private static ConditionExpression Cond(string attribute, ConditionOperator op, object value)
		{
			return new ConditionExpression(attribute, op, value);
		}

		private static FilterExpression Filter(params ConditionExpression[] conditions)
		{
			var filter = new FilterExpression(LogicalOperator.And);
			foreach (var c in conditions)
				filter.AddCondition(c);
			return filter;
		}

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
