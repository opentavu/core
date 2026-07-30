using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.CompanyProfile.SingleRecordGuard
{
	/// <summary>
	/// Enforces the singleton rule for tavu_companyprofile: at most one record may exist.
	/// The seller's own company profile (logo, branding, address, terms) is one record per
	/// tenant; any attempt to create a second is blocked. Mirrors Pl.SystemSettings.SingleRecordGuard.
	/// </summary>
	/// <remarks>
	/// Plugin Registration: Message=Create, Primary Entity=tavu_companyprofile,
	/// Stage=20 (Pre-operation), Mode=Synchronous, Deployment=Server.
	/// </remarks>
	public class SingleRecordGuard : PluginBase
	{
		private const string TargetEntityName = "tavu_companyprofile";

		// Read-only validation (never writes → cannot recurse); enforce at any depth,
		// including programmatic/import creates.
		protected override int MaxDepth => 8;

		public SingleRecordGuard() : base(typeof(SingleRecordGuard)) { }

		protected override void ExecuteInternal(LocalPluginContext localContext)
		{
			if (localContext == null) throw new ArgumentNullException(nameof(localContext));
			localContext.Trace("SingleRecordGuard: ExecuteInternal entered.");

			var ctx = localContext.PluginExecutionContext;

			if (!string.Equals(ctx.MessageName, "Create", StringComparison.OrdinalIgnoreCase))
			{
				localContext.Trace("Message is '{0}', not Create. Exiting.", ctx.MessageName);
				return;
			}

			if (!(ctx.InputParameters.Contains("Target") && ctx.InputParameters["Target"] is Entity target))
			{
				localContext.Trace("Target is missing or not an Entity. Exiting.");
				return;
			}

			if (!string.Equals(target.LogicalName, TargetEntityName, StringComparison.Ordinal))
			{
				localContext.Trace("Unexpected entity '{0}'. Plugin only handles '{1}'. Exiting.",
					target.LogicalName, TargetEntityName);
				return;
			}

			EnforceSingleRecord(localContext);
			localContext.Trace("SingleRecordGuard: ExecuteInternal exiting.");
		}

		/// <summary>Blocks the create when a tavu_companyprofile record already exists.</summary>
		private void EnforceSingleRecord(LocalPluginContext localContext)
		{
			localContext.Trace("EnforceSingleRecord: checking for an existing record.");

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
					"Only one Company Profile record is allowed. " +
					"Open the existing Company Profile record and edit it instead of creating a new one.");
			}

			localContext.Trace("No existing record found. Create allowed.");
		}
	}
}