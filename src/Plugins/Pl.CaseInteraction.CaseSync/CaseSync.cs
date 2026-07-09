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
	/// Increment 2 — Auto-resume: an Inbound interaction on a case that is on hold clears
	/// tavu_slaonhold (when tavu_slaautoresume = Yes), letting SlaAssignment resume the SLA.
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
		private const string CaseStatus = "tavu_status";                  // lookup -> tavu_casestatus

		private const string StatusEntity = "tavu_casestatus";
		private const string StatusPausesSla = "tavu_pausessla";          // Yes/No
		private const string StatusIsResumeTarget = "tavu_isresumetarget"; // Yes/No — status to move to when resuming

		private const string SettingsEntity = "tavu_systemsettings";
		private const string SettingsAutoResume = "tavu_slaautoresume";   // Yes/No — resume SLA on customer reply

		private const int DirOutbound = 576600001; // agent -> customer (counts as a "response")
		private const int DirInbound = 576600000;  // customer -> agent (a reply)

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
			AutoResumeOnInbound(localContext, target);
		}

		/// <summary>
		/// Auto-resume: when a customer reply (Inbound) arrives on a case that is on hold, and the org has
		/// tavu_systemsettings.tavu_slaautoresume = Yes, clear tavu_slaonhold so SlaAssignment resumes the SLA.
		/// If auto-resume is off, the reply just lands in the thread and the agent resumes manually.
		/// </summary>
		private void AutoResumeOnInbound(LocalPluginContext localContext, Entity target)
		{
			var dir = target.GetAttributeValue<OptionSetValue>(IxDirection);
			if (dir == null || dir.Value != DirInbound)
			{
				localContext.Trace("Not an Inbound interaction; no auto-resume.");
				return;
			}

			var caseRef = target.GetAttributeValue<EntityReference>(IxCase);
			if (caseRef == null) return;

			IOrganizationService svc = localContext.SystemService;

			if (!IsAutoResumeEnabled(svc))
			{
				localContext.Trace("Auto-resume disabled in system settings; leaving the case paused.");
				return;
			}

			// Only resume if the case is currently in a pausing status.
			var c = svc.Retrieve(CaseEntity, caseRef.Id, new ColumnSet(CaseStatus));
			var statusRef = c.GetAttributeValue<EntityReference>(CaseStatus);
			if (statusRef == null || !StatusPauses(svc, statusRef))
			{
				localContext.Trace("Case is not in a pausing status; nothing to resume.");
				return;
			}

			var resumeStatusId = GetResumeTargetStatusId(svc);
			if (resumeStatusId == Guid.Empty)
			{
				localContext.Trace("No status flagged IsResumeTarget; cannot auto-resume.");
				return;
			}

			var upd = new Entity(CaseEntity, caseRef.Id);
			upd[CaseStatus] = new EntityReference(StatusEntity, resumeStatusId); // triggers SlaAssignment resume
			svc.Update(upd);
			localContext.Trace("Auto-resumed case {0} to the resume-target status (inbound reply).", caseRef.Id);
		}

		private bool StatusPauses(IOrganizationService svc, EntityReference statusRef)
		{
			var s = svc.Retrieve(StatusEntity, statusRef.Id, new ColumnSet(StatusPausesSla));
			return s.GetAttributeValue<bool>(StatusPausesSla);
		}

		private Guid GetResumeTargetStatusId(IOrganizationService svc)
		{
			var q = new QueryExpression(StatusEntity)
			{
				ColumnSet = new ColumnSet(false),
				NoLock = true,
				TopCount = 1
			};
			q.Criteria.AddCondition(StatusIsResumeTarget, ConditionOperator.Equal, true);
			var res = svc.RetrieveMultiple(q);
			return res.Entities.Count > 0 ? res.Entities[0].Id : Guid.Empty;
		}

		private bool IsAutoResumeEnabled(IOrganizationService svc)
		{
			var q = new QueryExpression(SettingsEntity)
			{
				ColumnSet = new ColumnSet(SettingsAutoResume),
				NoLock = true,
				TopCount = 1
			};
			var res = svc.RetrieveMultiple(q);
			return res.Entities.Count > 0 && res.Entities[0].GetAttributeValue<bool>(SettingsAutoResume);
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