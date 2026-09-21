using System;
using Microsoft.Xrm.Sdk;
using OpenTavu.Dataverse.Common;

namespace Pl.SalesTarget.KeyStamp
{
	/// <summary>
	/// Stamps the machine dedup key tavu_targetkey on tavu_salestarget from the record's
	/// own dimension fields, so it is never hand-typed. The key is the alternate key that
	/// prevents duplicate targets for the same (goal type, scope, rep, team, business line,
	/// period) and lets the quota grid and the forecast flows upsert safely.
	///
	/// Format (pipe-joined, empty slot when a dimension does not apply):
	///   goaltype | scope | user | team | businessline | period
	/// using each lookup's record id (GUID, "D" format) and the scope option value.
	///
	/// Reference: docs/forecasting-build-plan.md §3.3, docs/forecasting-step-a-guide.md §3.
	/// </summary>
	/// <remarks>
	/// Registration (Plugin Registration Tool) — TWO steps share this assembly:
	///
	///   Step 1 — Create
	///     Message:              Create
	///     Primary Entity:       tavu_salestarget
	///     Stage:                20 (Pre-operation)
	///     Execution Mode:       Synchronous
	///     Deployment:           Server
	///     (No filtering attributes / no Pre-Image on Create.)
	///
	///   Step 2 — Update
	///     Message:              Update
	///     Primary Entity:       tavu_salestarget
	///     Filtering Attributes: tavu_goaltype, tavu_scope, tavu_salesrep, tavu_team,
	///                           tavu_businessline, tavu_salesperiod
	///     Stage:                20 (Pre-operation)
	///     Execution Mode:       Synchronous
	///     Deployment:           Server
	///     Pre-Image "PreImg":   tavu_goaltype, tavu_scope, tavu_salesrep, tavu_team,
	///                           tavu_businessline, tavu_salesperiod
	///
	/// Pre-Operation so the key is persisted by the same write, no extra Update, no recursion.
	/// </remarks>
	public class KeyStamp : PluginBase
	{
		private const string TargetEntityName = "tavu_salestarget";
		private const string AttrTargetKey = "tavu_targetkey";
		private const string AttrGoalType = "tavu_goaltype";
		private const string AttrScope = "tavu_scope";
		private const string AttrUser = "tavu_salesrep";
		private const string AttrTeam = "tavu_team";
		private const string AttrBusinessLine = "tavu_businessline";
		private const string AttrSalesPeriod = "tavu_salesperiod";
		private const string PreImageName = "PreImg";

		public KeyStamp() : base(typeof(KeyStamp)) { }

		protected override void ExecuteInternal(LocalPluginContext localContext)
		{
			if (localContext == null)
				throw new ArgumentNullException(nameof(localContext));

			localContext.Trace("KeyStamp: ExecuteInternal entered.");

			if (!(localContext.PluginExecutionContext.InputParameters.Contains("Target")
				  && localContext.PluginExecutionContext.InputParameters["Target"] is Entity target))
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

			var context = localContext.PluginExecutionContext;
			Entity preImage = context.PreEntityImages.Contains(PreImageName)
				? context.PreEntityImages[PreImageName]
				: null;

			// Build the composite key from the effective values (Target wins, else Pre-Image).
			string goal = RefId(GetEffective<EntityReference>(target, preImage, AttrGoalType));
			string scope = OptionValue(GetEffective<OptionSetValue>(target, preImage, AttrScope));
			string user = RefId(GetEffective<EntityReference>(target, preImage, AttrUser));
			string team = RefId(GetEffective<EntityReference>(target, preImage, AttrTeam));
			string line = RefId(GetEffective<EntityReference>(target, preImage, AttrBusinessLine));
			string period = RefId(GetEffective<EntityReference>(target, preImage, AttrSalesPeriod));

			string key = string.Join("|", goal, scope, user, team, line, period);
			target[AttrTargetKey] = key;

			localContext.Trace("tavu_targetkey stamped: {0}", key);
			localContext.Trace("KeyStamp: ExecuteInternal exiting.");
		}

		/// <summary>Lookup id in lowercase "D" format, or empty string when null.</summary>
		private static string RefId(EntityReference reference)
		{
			return reference == null ? string.Empty : reference.Id.ToString("D").ToLowerInvariant();
		}

		/// <summary>Choice value as text, or empty string when null.</summary>
		private static string OptionValue(OptionSetValue option)
		{
			return option == null
				? string.Empty
				: option.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		/// <summary>Target value if present, else Pre-Image value, else default(T).</summary>
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
