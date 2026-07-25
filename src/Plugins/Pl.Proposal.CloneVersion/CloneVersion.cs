using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.Proposal.CloneVersion
{
	/// <summary>
	/// Implements the tavu_CloneProposalVersion Custom API ("Create New Version").
	///
	/// Given a source proposal, it:
	///   1. Creates a new Draft proposal copying the source's business fields, with
	///      tavu_version incremented (v1 -> v2 -> ...).
	///   2. Copies every active proposal line to the new proposal.
	///   3. Marks the source proposal as Superseded.
	///   4. Returns the new proposal id in the NewProposalId output parameter.
	///
	/// IMPORTANT — depth behaviour: this plugin runs on the Custom API message (depth 1),
	/// so its service.Create/Update calls run at depth 2, where Pl.ProposalLine.Calculator
	/// and Pl.Proposal.LifecycleTracker auto-abort (MaxDepth = 1). That is intentional: the
	/// clone must be self-sufficient, so it copies the ALREADY-COMPUTED money fields (line
	/// subtotals/tax/total/cost and the header totals) and sets version/status explicitly,
	/// rather than relying on those plugins to recompute. When the consultant later edits a
	/// line on the new Draft (a depth-1 user action), Calculator runs normally.
	/// </summary>
	/// <remarks>
	/// Registration: this is the plugin type bound to the Custom API "tavu_CloneProposalVersion"
	/// (Binding Type = Global / unbound, Is Function = No).
	///   Request  parameter:  ProposalId    (String, required) — source proposal GUID.
	///   Response property:   NewProposalId (String)           — created proposal GUID.
	/// No Create/Update SDK step; the Custom API's Plugin Type is the entry point.
	///
	/// UserService is used so the new records are owned by the calling user and respect
	/// their privileges (this is a user-initiated action, not a system/audit write).
	/// </remarks>
	public class CloneVersion : PluginBase
	{
		// ===== Custom API parameters =====
		private const string InProposalId = "ProposalId";
		private const string OutNewProposalId = "NewProposalId";

		// ===== Schema constants (VERIFY line uom/overrideprice names vs live schema) =====
		private const string ProposalEntity = "tavu_proposal";
		private const string LineEntity = "tavu_proposalline";

		private const string AttrStatusCode = "statuscode";
		private const string AttrStateCode = "statecode";
		private const string AttrVersion = "tavu_version";
		private const string AttrCurrency = "transactioncurrencyid";

		// Proposal statuscodes / statecodes.
		private const int StatusDraft = 576600001;
		private const int StatusSuperseded = 576600008;
		private const int StateActive = 0;
		private const int StateInactive = 1;

		// Header business fields copied to the new version.
		private static readonly string[] HeaderCopyFields =
		{
			"tavu_name", "tavu_opportunity", "tavu_customer", "tavu_account", "tavu_contact",
			"tavu_primarycontact", "tavu_pricelist", AttrCurrency, "tavu_proposalcontent",
			"tavu_discoverynotes", "tavu_effectivefrom", "tavu_effectiveto",
			"tavu_expecteddecisiondate",
			// already-computed totals — copied so the clone is correct without Calculator.
			"tavu_subtotal", "tavu_total", "tavu_totaltax", "tavu_totalcost", "tavu_grossmargin"
			// NOT copied: tavu_sentdate, statecode/statuscode, tavu_version (set explicitly).
		};

		// Line fields copied to each cloned line.
		private static readonly string[] LineCopyFields =
		{
			"tavu_product", "tavu_quantity", "tavu_priceperunit", "tavu_unitcost",
			"tavu_taxrate", "tavu_discount", AttrCurrency,
			"tavu_unitofmeasure", "tavu_overrideprice",   // VERIFY these two names
			// already-computed line money fields — copied (Calculator aborts at depth 2).
			"tavu_subtotal", "tavu_taxamount", "tavu_total", "tavu_linecost"
			// tavu_proposal is set to the NEW proposal, not copied.
		};

		private const string AttrLineProposal = "tavu_proposal";

		public CloneVersion() : base(typeof(CloneVersion)) { }

		protected override void ExecuteInternal(LocalPluginContext localContext)
		{
			if (localContext == null)
				throw new ArgumentNullException(nameof(localContext));

			var ctx = localContext.PluginExecutionContext;
			var service = localContext.UserService;

			localContext.Trace("CloneVersion: entered.");

			// --- Read and validate the input parameter ---
			if (!ctx.InputParameters.Contains(InProposalId)
				|| !(ctx.InputParameters[InProposalId] is string rawId)
				|| string.IsNullOrWhiteSpace(rawId))
			{
				throw new InvalidPluginExecutionException("ProposalId is required.");
			}

			if (!Guid.TryParse(rawId, out Guid sourceId))
			{
				throw new InvalidPluginExecutionException("ProposalId is not a valid GUID.");
			}

			localContext.Trace("CloneVersion: source proposal = {0}", sourceId);

			// --- Retrieve the source proposal (business fields + current version) ---
			var headerCols = new List<string>(HeaderCopyFields) { AttrVersion };
			Entity source = service.Retrieve(ProposalEntity, sourceId, new ColumnSet(headerCols.ToArray()));

			// --- Build the new Draft version ---
			var newProposal = new Entity(ProposalEntity);
			foreach (var attr in HeaderCopyFields)
				CopyIfPresent(source, newProposal, attr);

			newProposal[AttrVersion] = NextVersion(source.GetAttributeValue<string>(AttrVersion));
			newProposal[AttrStatusCode] = new OptionSetValue(StatusDraft); // Active/Draft

			Guid newId = service.Create(newProposal);
			localContext.Trace("CloneVersion: new proposal {0} created as {1}.",
				newId, newProposal[AttrVersion]);

			// --- Copy the active lines ---
			int copied = CopyLines(localContext, service, sourceId, newId);
			localContext.Trace("CloneVersion: {0} line(s) copied.", copied);

			// --- Supersede the source (set BOTH statecode and statuscode) ---
			var supersede = new Entity(ProposalEntity, sourceId)
			{
				[AttrStateCode] = new OptionSetValue(StateInactive),
				[AttrStatusCode] = new OptionSetValue(StatusSuperseded)
			};
			service.Update(supersede);
			localContext.Trace("CloneVersion: source {0} marked Superseded.", sourceId);

			// --- Return the new id ---
			ctx.OutputParameters[OutNewProposalId] = newId.ToString();
			localContext.Trace("CloneVersion: exiting. NewProposalId={0}.", newId);
		}

		/// <summary>Copies every active line of the source proposal onto the new proposal.</summary>
		private int CopyLines(LocalPluginContext localContext, IOrganizationService service,
							   Guid sourceProposalId, Guid newProposalId)
		{
			var query = new QueryExpression(LineEntity)
			{
				ColumnSet = new ColumnSet(LineCopyFields),
				Criteria = new FilterExpression()
			};
			query.Criteria.AddCondition(AttrLineProposal, ConditionOperator.Equal, sourceProposalId);
			query.Criteria.AddCondition(AttrStateCode, ConditionOperator.Equal, StateActive);

			var lines = service.RetrieveMultiple(query).Entities;

			foreach (var line in lines)
			{
				var newLine = new Entity(LineEntity);
				foreach (var attr in LineCopyFields)
					CopyIfPresent(line, newLine, attr);

				newLine[AttrLineProposal] = new EntityReference(ProposalEntity, newProposalId);
				service.Create(newLine);
			}

			return lines.Count;
		}

		/// <summary>Copies an attribute from source to target only if the source has it set.</summary>
		private static void CopyIfPresent(Entity source, Entity target, string attribute)
		{
			if (source.Contains(attribute) && source[attribute] != null)
				target[attribute] = source[attribute];
		}

		/// <summary>
		/// Computes the next version label from the current one: "v1" -> "v2", "v3" -> "v4".
		/// Falls back to "v2" when the current value is missing or unparseable.
		/// </summary>
		private static string NextVersion(string current)
		{
			if (!string.IsNullOrWhiteSpace(current))
			{
				var digits = new StringBuilder();
				foreach (char c in current)
					if (char.IsDigit(c)) digits.Append(c);

				if (digits.Length > 0 && int.TryParse(digits.ToString(), out int n))
					return "v" + (n + 1);
			}
			return "v2";
		}
	}
}
