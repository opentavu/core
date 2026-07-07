using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.CaseInteraction.CaseSync
{
	/// <summary>
	/// Propagates derived fields onto the parent case when a Case Interaction is created.
	/// Router pattern: each private handler self-filters. Registered on Create of
	/// tavu_caseinteraction, Post-Operation, Synchronous, Server.
	///
	/// Increment 1 — First Response Date: the first Outbound interaction (agent replying to the
	/// customer) stamps tavu_firstresponsedate on the case if unset. Internal notes and inbound
	/// messages do not count as a response.
	/// </summary>
	/// <remarks>
	/// Plugin Registration Tool:
	///   Message:        Create
	///   Primary Entity: tavu_caseinteraction
	///   Stage:          40 (Post-operation)
	///   Execution Mode: Synchronous
	///   Deployment:     Server
	/// </remarks>
	public class CaseSync : PluginBase
	{
		private const string InteractionEntity = "tavu_caseinteraction";
		private const string IxDirection = "tavu_direction"; // Choice
		private const string IxCase = "tavu_case";      // lookup -> tavu_case

		private const string CaseEntity = "tavu_case";
		private const string CaseFirstResponse = "tavu_firstresponsedate"; // DateTime (derived/audit)

		private const int DirOutbound = 576600001; // agent -> customer (counts as a "response")

		public CaseSync() : base(typeof(CaseSync)) { }

		protected override void ExecuteInternal(LocalPluginContext localContext)
		{
			if (localContext == null) throw new ArgumentNullException(nameof(localContext));

			var ctx = localContext.PluginExecutionContext;
			if (!(ctx.InputParameters.Contains("Target") && ctx.InputParameters["Target"] is Entity target))
			{
				localContext.Trace("Target missing or not an Entity. Exiting.");
				return;
			}
			if (!string.Equals(target.LogicalName, InteractionEntity, StringComparison.Ordinal))
			{
				localContext.Trace("Unexpected entity '{0}'. Exiting.", target.LogicalName);
				return;
			}

			StampFirstResponse(localContext, target);
		}

		/// <summary>First Outbound interaction stamps the case's First Response Date (if unset).</summary>
		private void StampFirstResponse(LocalPluginContext localContext, Entity target)
		{
			var dir = target.GetAttributeValue<OptionSetValue>(IxDirection);
			if (dir == null || dir.Value != DirOutbound)
			{
				localContext.Trace("Not an Outbound interaction; not a response. Exiting.");
				return;
			}

			var caseRef = target.GetAttributeValue<EntityReference>(IxCase);
			if (caseRef == null)
			{
				localContext.Trace("Interaction has no parent case; nothing to stamp.");
				return;
			}

			// SystemService: tavu_firstresponsedate is a derived/audit field the agent must not write directly.
			IOrganizationService svc = localContext.SystemService;

			var c = svc.Retrieve(CaseEntity, caseRef.Id, new ColumnSet(CaseFirstResponse));
			if (c.Contains(CaseFirstResponse) && c[CaseFirstResponse] != null)
			{
				localContext.Trace("First Response Date already set; leaving unchanged.");
				return;
			}

			var upd = new Entity(CaseEntity, caseRef.Id);
			upd[CaseFirstResponse] = DateTime.UtcNow;
			svc.Update(upd);
			localContext.Trace("First Response Date stamped on case {0}.", caseRef.Id);
		}
	}
}