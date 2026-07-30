using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.Proposal.LifecycleTracker
{
	/// <summary>
	/// Enforces the deterministic proposal lifecycle on tavu_proposal:
	///
	///   - Create: defaults tavu_version to "v1" when not supplied, and inherits the customer
	///     context (customer / account / contact / discovery notes) + a suggested Name from
	///     the parent opportunity — fill-if-empty, so user-chosen values are never overwritten.
	///   - Update:
	///       * Transition guard — a proposal can only reach Approved/Rejected after it
	///         has been Sent (best practice: "don't approve a draft"); terminal statuses
	///         (Approved / Superseded / Withdrawn) cannot change further.
	///       * Lock — once the proposal is Sent to Client / Awaiting Decision or closed,
	///         its fields are immutable; only the lifecycle (statecode/statuscode) and
	///         tavu_sentdate may change. To edit a locked proposal, create a new version.
	///       * Single winner — only one proposal per opportunity may be Approved by Client.
	///
	/// The line-side lock (blocking edits to tavu_proposalline when the parent is locked)
	/// lives in Pl.ProposalLine.Calculator, which owns the tavu_proposalline trigger.
	/// "Create New Version" is the tavu_CloneProposalVersion Custom API.
	/// </summary>
	/// <remarks>
	/// Plugin Registration (Plugin Registration Tool) — TWO steps, Synchronous / Server /
	/// Primary Entity = tavu_proposal:
	///
	///   1. Create — Stage 20 (Pre-operation). No image, no filtering attributes.
	///   2. Update — Stage 20 (Pre-operation). No filtering attributes (must fire on ANY
	///      field change so the lock can catch edits to non-lifecycle fields).
	///      Pre-Image "PreImg": statecode, statuscode, tavu_opportunity.
	///
	/// Pre-Operation: the version default modifies the Target in place; the guards throw
	/// (InvalidPluginExecutionException) to block invalid writes before commit. No extra
	/// Update, no recursion. MaxDepth = 1.
	/// </remarks>
	public class LifecycleTracker : PluginBase
	{
		// ===== Schema constants =====
		private const string TargetEntityName = "tavu_proposal";

		private const string AttrStatusCode = "statuscode";
		private const string AttrStateCode = "statecode";
		private const string AttrOpportunity = "tavu_opportunity";
		private const string AttrVersion = "tavu_version";
		private const string AttrSentDate = "tavu_sentdate";
		private const string AttrName = "tavu_name";
		private const string AttrCustomer = "tavu_customer";
		private const string AttrAccount = "tavu_account";
		private const string AttrContact = "tavu_contact";
		private const string AttrDiscoveryNotes = "tavu_discoverynotes";

		// Parent opportunity (source of the inherited customer context + suggested name).
		private const string OpportunityEntityName = "tavu_opportunity";
		private const string OppAttrTopic = "tavu_topic";

		private const string PreImageName = "PreImg";

		// tavu_proposal statuscode values (sales-model.md §8.2).
		private const int StatusDraft = 576600001;
		private const int StatusAiGenerated = 576600002;
		private const int StatusUnderReview = 576600003;
		private const int StatusSentToClient = 576600004;
		private const int StatusAwaitingDecision = 576600005;
		private const int StatusApproved = 576600006;
		private const int StatusRejected = 576600007;
		private const int StatusSuperseded = 576600008;
		private const int StatusWithdrawn = 576600009;

		private const int StateInactive = 1;

		// Fields that may still change while the proposal is locked.
		private static readonly HashSet<string> LockAllowedAttributes =
			new HashSet<string>(StringComparer.Ordinal)
			{
				AttrStateCode, AttrStatusCode, AttrSentDate
			};

		public LifecycleTracker() : base(typeof(LifecycleTracker)) { }

		protected override void ExecuteInternal(LocalPluginContext localContext)
		{
			if (localContext == null)
				throw new ArgumentNullException(nameof(localContext));

			var ctx = localContext.PluginExecutionContext;
			localContext.Trace("Proposal.LifecycleTracker: entered. Message={0}.", ctx.MessageName);

			if (!(ctx.InputParameters.Contains("Target") && ctx.InputParameters["Target"] is Entity target))
			{
				localContext.Trace("Target missing or not an Entity. Exiting.");
				return;
			}

			if (!string.Equals(target.LogicalName, TargetEntityName, StringComparison.Ordinal))
			{
				localContext.Trace(
					"Unexpected entity '{0}'. Plugin only handles '{1}'. Exiting.",
					target.LogicalName, TargetEntityName);
				return;
			}

			bool isCreate = string.Equals(ctx.MessageName, "Create", StringComparison.OrdinalIgnoreCase);

			if (isCreate)
			{
				DefaultVersion(localContext, target);
				InheritFromOpportunity(localContext, target);
				return;
			}

			// Update path.
			Entity preImage = ctx.PreEntityImages.Contains(PreImageName)
				? ctx.PreEntityImages[PreImageName]
				: null;

			GuardLockedImmutability(localContext, target, preImage);
			GuardStatusTransition(localContext, target, preImage);
			GuardSingleApproved(localContext, target, preImage);

			localContext.Trace("Proposal.LifecycleTracker: exiting.");
		}

		/// <summary>Stamps tavu_version = "v1" on Create when the caller did not supply one.</summary>
		private void DefaultVersion(LocalPluginContext localContext, Entity target)
		{
			if (!target.Contains(AttrVersion)
				|| string.IsNullOrWhiteSpace(target.GetAttributeValue<string>(AttrVersion)))
			{
				target[AttrVersion] = "v1";
				localContext.Trace("DefaultVersion: tavu_version defaulted to v1.");
			}
		}

		/// <summary>
		/// On Create, inherits the customer context (customer / account / contact / discovery
		/// notes) from the parent opportunity and defaults the proposal Name from the
		/// opportunity Topic — but ONLY for fields the caller left empty, so a value the user
		/// already chose is never overwritten. The client-side form pre-fill
		/// (tavu_proposal_form.js onLoad) sets these for immediate UX on the main form; this
		/// handler is the server-side backstop for any path that skips the form (quick create,
		/// API, import). Reads the opportunity under UserService so it respects the creating
		/// user's privileges (they are creating a child of an opportunity they can see).
		/// </summary>
		private void InheritFromOpportunity(LocalPluginContext localContext, Entity target)
		{
			var oppRef = target.GetAttributeValue<EntityReference>(AttrOpportunity);
			if (oppRef == null)
			{
				localContext.Trace("InheritFromOpportunity: no parent opportunity on create. Skipping.");
				return;
			}

			Entity opp;
			try
			{
				opp = localContext.UserService.Retrieve(
					OpportunityEntityName, oppRef.Id,
					new ColumnSet(OppAttrTopic, AttrCustomer, AttrAccount, AttrContact, AttrDiscoveryNotes));
			}
			catch (Exception ex)
			{
				localContext.Trace(
					"InheritFromOpportunity: could not read opportunity {0}: {1}. Skipping.",
					oppRef.Id, ex.Message);
				return;
			}

			CopyLookupIfEmpty(localContext, target, opp, AttrCustomer);
			CopyLookupIfEmpty(localContext, target, opp, AttrAccount);
			CopyLookupIfEmpty(localContext, target, opp, AttrContact);
			CopyStringIfEmpty(localContext, target, opp, AttrDiscoveryNotes);

			// Suggested name: "<Opportunity Topic> — Proposal <version>", only when empty.
			if (!target.Contains(AttrName)
				|| string.IsNullOrWhiteSpace(target.GetAttributeValue<string>(AttrName)))
			{
				var topic = opp.GetAttributeValue<string>(OppAttrTopic);
				if (!string.IsNullOrWhiteSpace(topic))
				{
					var version = target.GetAttributeValue<string>(AttrVersion);
					if (string.IsNullOrWhiteSpace(version)) version = "v1";
					target[AttrName] = topic + " — Proposal " + version;
					localContext.Trace("InheritFromOpportunity: defaulted name from opportunity topic.");
				}
			}
		}

		/// <summary>Copies a lookup from source to target only when the target left it empty.</summary>
		private static void CopyLookupIfEmpty(LocalPluginContext ctx, Entity target, Entity source, string attr)
		{
			if (target.Contains(attr) && target[attr] != null) return; // user set it — respect it
			var val = source.GetAttributeValue<EntityReference>(attr);
			if (val != null)
			{
				target[attr] = val;
				ctx.Trace("InheritFromOpportunity: inherited {0} from opportunity.", attr);
			}
		}

		/// <summary>Copies a text value from source to target only when the target left it empty.</summary>
		private static void CopyStringIfEmpty(LocalPluginContext ctx, Entity target, Entity source, string attr)
		{
			if (target.Contains(attr) && !string.IsNullOrWhiteSpace(target.GetAttributeValue<string>(attr))) return;
			var val = source.GetAttributeValue<string>(attr);
			if (!string.IsNullOrWhiteSpace(val))
			{
				target[attr] = val;
				ctx.Trace("InheritFromOpportunity: inherited {0} from opportunity.", attr);
			}
		}

		/// <summary>
		/// Once the proposal is locked (Sent / Awaiting Decision / any closed status), only
		/// the lifecycle fields and tavu_sentdate may change. Any other field in the Target
		/// is rejected. Lock is decided from the Pre-Image (the committed state), so the very
		/// transition that sends the proposal is still allowed to touch fields.
		/// </summary>
		private void GuardLockedImmutability(LocalPluginContext localContext, Entity target, Entity preImage)
		{
			if (preImage == null)
			{
				localContext.Trace("GuardLockedImmutability: no Pre-Image; cannot evaluate lock. Skipping.");
				return;
			}

			if (!IsLocked(preImage))
			{
				localContext.Trace("GuardLockedImmutability: proposal is editable. Skipping.");
				return;
			}

			foreach (var attr in target.Attributes.Keys)
			{
				if (LockAllowedAttributes.Contains(attr)) continue;
				// The primary key can appear in the Target; it is not an edit.
				if (string.Equals(attr, TargetEntityName + "id", StringComparison.Ordinal)) continue;
				// Only custom business fields are subject to the lock. Ignore system /
				// standard attributes (modifiedon, modifiedby, ownerid, statecode/statuscode,
				// etc.) that ride along on an update without being a user edit.
				if (!attr.StartsWith("tavu_", StringComparison.Ordinal)) continue;

				localContext.Trace("GuardLockedImmutability: blocked edit to '{0}' on a locked proposal.", attr);
				throw new InvalidPluginExecutionException(
					"This proposal is locked because it has been sent to the client (or closed). " +
					"Create a new version to make changes.");
			}

			localContext.Trace("GuardLockedImmutability: only lifecycle fields changed. Allowed.");
		}

		/// <summary>
		/// Blocks the two invalid transition classes: (1) leaving a terminal status
		/// (Approved / Superseded / Withdrawn), and (2) reaching Approved/Rejected without
		/// having been Sent first. Everything else is permitted.
		/// </summary>
		private void GuardStatusTransition(LocalPluginContext localContext, Entity target, Entity preImage)
		{
			if (!target.Contains(AttrStatusCode)) return;

			var newStatusOsv = target.GetAttributeValue<OptionSetValue>(AttrStatusCode);
			if (newStatusOsv == null) return;
			int newStatus = newStatusOsv.Value;

			int? oldStatus = preImage?.GetAttributeValue<OptionSetValue>(AttrStatusCode)?.Value;
			if (oldStatus == null || oldStatus.Value == newStatus) return;

			bool oldIsTerminal =
				oldStatus.Value == StatusApproved ||
				oldStatus.Value == StatusSuperseded ||
				oldStatus.Value == StatusWithdrawn;

			if (oldIsTerminal)
			{
				localContext.Trace("GuardStatusTransition: blocked change from terminal status {0}.", oldStatus.Value);
				throw new InvalidPluginExecutionException(
					"This proposal is in a final status and its status can no longer change.");
			}

			bool needsSentFirst = newStatus == StatusApproved || newStatus == StatusRejected;
			bool wasSent = oldStatus.Value == StatusSentToClient;

			if (needsSentFirst && !wasSent)
			{
				localContext.Trace("GuardStatusTransition: blocked {0} -> {1} (not sent yet).", oldStatus.Value, newStatus);
				throw new InvalidPluginExecutionException(
					"A proposal must be Sent to the client before it can be Approved or Rejected.");
			}

			localContext.Trace("GuardStatusTransition: {0} -> {1} allowed.", oldStatus.Value, newStatus);
		}

		/// <summary>
		/// Ensures a single winning proposal per opportunity: if this update sets the status
		/// to Approved by Client, no other proposal for the same opportunity may already be
		/// Approved. Reads under SystemService so the check is consistent on every path.
		/// </summary>
		private void GuardSingleApproved(LocalPluginContext localContext, Entity target, Entity preImage)
		{
			var newStatusOsv = target.GetAttributeValue<OptionSetValue>(AttrStatusCode);
			if (newStatusOsv == null || newStatusOsv.Value != StatusApproved) return;

			EntityReference oppRef =
				target.GetAttributeValue<EntityReference>(AttrOpportunity)
				?? preImage?.GetAttributeValue<EntityReference>(AttrOpportunity);

			if (oppRef == null)
			{
				localContext.Trace("GuardSingleApproved: no parent opportunity; skipping.");
				return;
			}

			var query = new QueryExpression(TargetEntityName)
			{
				ColumnSet = new ColumnSet(false),
				TopCount = 1,
				Criteria = new FilterExpression()
			};
			query.Criteria.AddCondition(AttrOpportunity, ConditionOperator.Equal, oppRef.Id);
			query.Criteria.AddCondition(AttrStatusCode, ConditionOperator.Equal, StatusApproved);
			query.Criteria.AddCondition(
				TargetEntityName + "id", ConditionOperator.NotEqual,
				localContext.PluginExecutionContext.PrimaryEntityId);

			var existing = localContext.SystemService.RetrieveMultiple(query);
			if (existing.Entities.Count > 0)
			{
				localContext.Trace(
					"GuardSingleApproved: another Approved proposal exists for opportunity {0}.", oppRef.Id);
				throw new InvalidPluginExecutionException(
					"Another proposal is already Approved for this opportunity. " +
					"Only one winning proposal is allowed — supersede or reject the other first.");
			}

			localContext.Trace("GuardSingleApproved: no conflicting Approved proposal. Allowed.");
		}

		private static bool IsLocked(Entity image)
		{
			var state = image.GetAttributeValue<OptionSetValue>(AttrStateCode);
			if (state != null && state.Value == StateInactive) return true;

			var status = image.GetAttributeValue<OptionSetValue>(AttrStatusCode);
			return status != null && status.Value == StatusSentToClient;
		}
	}
}
