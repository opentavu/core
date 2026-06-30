using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.ProposalLine.Calculator
{
	/// <summary>
	/// Quotation calculator for tavu_proposalline and its parent tavu_proposal header.
	///
	/// Two concerns, routed by pipeline stage inside one assembly:
	///   - Pre-Operation (Create/Update): computes the line's own money fields
	///     (subtotal, tax amount, total, line cost) in-place on the Target, so they
	///     persist with the same write the user triggered. No extra Update, no recursion.
	///   - Post-Operation (Create/Update/Delete): re-aggregates ALL active sibling
	///     lines of the parent proposal and writes the header totals + gross margin.
	///     Runs Post-Op because the triggering line must already be committed before
	///     we sum the siblings.
	///
	/// Formulas (sales-model.md §8bis.2.1 line, §8.3 header):
	///   line.subtotal   = quantity * priceperunit
	///   line.taxamount  = subtotal * (taxrate / 100)        // tax on subtotal, pre-discount
	///   line.total      = subtotal + taxamount - discount   // discount is a currency amount
	///   line.linecost   = quantity * unitcost
	///   header.subtotal = SUM(line.subtotal)
	///   header.totaltax = SUM(line.taxamount)
	///   header.total    = SUM(line.total)                   // see note below
	///   header.totalcost= SUM(line.linecost)
	///   header.grossmargin (%) = subtotal == 0 ? 0 : ((subtotal - totalcost) / subtotal) * 100
	///
	/// NOTE on header.total: §8.3 documents it as "subtotal + totaltax (Plugin)", which
	/// silently drops per-line discounts. This implementation uses SUM(line.total) so the
	/// header always equals the sum of what each line shows (discounts included). If you
	/// prefer the literal §8.3 formula, change AggregateHeader accordingly — but the doc
	/// formula and the line.total formula disagree whenever any line has a discount.
	///
	/// Cost & margin (unit cost, line cost, total cost, gross margin) are open to all by
	/// default for now; a Field Security Profile can be added later. The rollup already
	/// runs under SystemService, so it keeps working if/when those fields become FSP-protected.
	/// Unit cost is snapshotted from tavu_product.tavu_cost on the line when the product is set.
	/// </summary>
	/// <remarks>
	/// Plugin Registration (Plugin Registration Tool) — FIVE steps share this assembly,
	/// all Synchronous / Server / Primary Entity = tavu_proposalline:
	///
	///   1. Create  — Stage 20 (Pre-Operation)  — no image.
	///   2. Create  — Stage 40 (Post-Operation) — no image (parent id is on Target).
	///   3. Update  — Stage 20 (Pre-Operation)  — Pre-Image "PreImg":
	///        tavu_quantity, tavu_priceperunit, tavu_unitcost, tavu_taxrate, tavu_discount
	///      Filtering attributes: tavu_quantity, tavu_priceperunit, tavu_unitcost,
	///        tavu_taxrate, tavu_discount, tavu_product
	///        (tavu_product triggers a unit-cost re-snapshot).
	///   4. Update  — Stage 40 (Post-Operation) — Pre-Image "PreImg":
	///        tavu_proposal (+ the calc fields are harmless to include)
	///      Filtering attributes: tavu_quantity, tavu_priceperunit, tavu_unitcost,
	///        tavu_taxrate, tavu_discount, tavu_product, tavu_proposal.
	///   5. Delete  — Stage 40 (Post-Operation) — Pre-Image "PreImg": tavu_proposal.
	///
	/// The header Update issued by the rollup runs in its own pipeline (depth 2) and does
	/// NOT re-enter this plugin, which is registered on tavu_proposalline only. MaxDepth=1.
	/// </remarks>
	public class Calculator : PluginBase
	{
		// ===== Schema constants =====
		// VERIFY these logical names against the live environment before registering.
		// The repo has no exported solution, and CustomerSync shows the tenant uses
		// "tavu_customer" (not the doc's "tavu_customerid"), so doc/schema drift exists.
		// Highest-risk constant: AttrLineProposal (the line -> header lookup). If it is
		// wrong, the rollup silently aggregates nothing.

		// --- tavu_proposalline (the line) ---
		private const string LineEntity = "tavu_proposalline";
		private const string AttrLineProposal = "tavu_proposal";     // lookup -> tavu_proposal  (confirmed)
		private const string AttrProduct = "tavu_product";           // lookup -> tavu_product
		private const string AttrQuantity = "tavu_quantity";         // Decimal
		private const string AttrPricePerUnit = "tavu_priceperunit"; // Currency
		private const string AttrUnitCost = "tavu_unitcost";         // Currency  (CREATE if missing)
		private const string AttrTaxRate = "tavu_taxrate";           // Decimal (percent)
		private const string AttrDiscount = "tavu_discount";         // Currency (amount, not %)
		private const string AttrLineSubtotal = "tavu_subtotal";     // Currency (plain, written here)
		private const string AttrLineTaxAmount = "tavu_taxamount";   // Currency (plain, written here)
		private const string AttrLineTotal = "tavu_total";           // Currency (plain, written here)
		private const string AttrLineCost = "tavu_linecost";         // Currency (plain, FSP-protected)

		// --- tavu_proposal (the header) ---
		private const string HeaderEntity = "tavu_proposal";
		private const string AttrHeaderSubtotal = "tavu_subtotal";   // Currency (plain)
		private const string AttrHeaderTotalTax = "tavu_totaltax";   // Currency (plain)
		private const string AttrHeaderTotal = "tavu_total";         // Currency (plain)
		private const string AttrHeaderTotalCost = "tavu_totalcost"; // Currency (plain, FSP)  (CREATE if missing)
		private const string AttrHeaderGrossMargin = "tavu_grossmargin"; // Decimal

		// --- tavu_product (catalog, read for the unit-cost snapshot) ---
		private const string ProductEntity = "tavu_product";
		private const string ProductCostAttr = "tavu_cost";          // Currency

		private const string AttrStateCode = "statecode";
		private const int StateActive = 0;

		private const string PreImageName = "PreImg";

		// Pipeline stages
		private const int StagePreOperation = 20;
		private const int StagePostOperation = 40;

		public Calculator() : base(typeof(Calculator)) { }

		protected override void ExecuteInternal(LocalPluginContext localContext)
		{
			if (localContext == null)
				throw new ArgumentNullException(nameof(localContext));

			var ctx = localContext.PluginExecutionContext;
			localContext.Trace(
				"Calculator: ExecuteInternal entered. Message={0}, Stage={1}.",
				ctx.MessageName, ctx.Stage);

			// Route by stage. Each handler self-filters and traces what it does.
			if (ctx.Stage == StagePreOperation)
			{
				ComputeLine(localContext);
			}
			else if (ctx.Stage == StagePostOperation)
			{
				RollUpHeader(localContext);
			}
			else
			{
				localContext.Trace("Stage {0} not handled by this plugin. Exiting.", ctx.Stage);
			}

			localContext.Trace("Calculator: ExecuteInternal exiting.");
		}

		/// <summary>
		/// Pre-Operation: compute the line's own money fields and write them onto Target,
		/// so they are persisted by the in-flight Create/Update write (no extra Update).
		/// Effective input = Target value if present, else Pre-Image value, else 0. This
		/// makes a single-field edit (e.g. only Discount changed) compute correctly.
		/// </summary>
		private void ComputeLine(LocalPluginContext localContext)
		{
			localContext.Trace("ComputeLine: entered.");
			var ctx = localContext.PluginExecutionContext;

			if (!(ctx.InputParameters.Contains("Target")
				  && ctx.InputParameters["Target"] is Entity target))
			{
				localContext.Trace("Target missing or not an Entity. Skipping.");
				return;
			}

			if (!string.Equals(target.LogicalName, LineEntity, StringComparison.Ordinal))
			{
				localContext.Trace(
					"Unexpected entity '{0}'. Plugin only handles '{1}'. Skipping.",
					target.LogicalName, LineEntity);
				return;
			}

			Entity preImage = ctx.PreEntityImages.Contains(PreImageName)
				? ctx.PreEntityImages[PreImageName]
				: null;

			decimal quantity = GetDecimal(target, preImage, AttrQuantity);
			decimal price = GetMoney(target, preImage, AttrPricePerUnit);
			decimal unitCost = GetMoney(target, preImage, AttrUnitCost);
			decimal taxRate = GetDecimal(target, preImage, AttrTaxRate);
			decimal discount = GetMoney(target, preImage, AttrDiscount);

			// Snapshot the unit cost from the product whenever the product is set or
			// changed (Create, or Update where tavu_product is in Target). The cost is
			// frozen on the line at quote time, so a later catalog change does not
			// retroactively alter an existing proposal's margin (sales-model.md §8bis).
			// A quantity/price-only edit leaves tavu_product out of Target, so the frozen
			// unit cost (read above from Target/Pre-Image) is kept untouched.
			// SystemService: tavu_product is catalog data — read it under SYSTEM so the
			// snapshot resolves on every path (form, import, API), like LifecycleTracker
			// reads tavu_salesstage.
			if (target.Contains(AttrProduct))
			{
				var productRef = target.GetAttributeValue<EntityReference>(AttrProduct);
				if (productRef != null)
				{
					var product = localContext.SystemService.Retrieve(
						ProductEntity, productRef.Id, new ColumnSet(ProductCostAttr));
					unitCost = product.GetAttributeValue<Money>(ProductCostAttr)?.Value ?? 0m;
					target[AttrUnitCost] = new Money(unitCost);
					localContext.Trace(
						"Unit cost snapshot from product {0}: {1}", productRef.Id, unitCost);
				}
			}

			decimal subtotal = Round(quantity * price);
			decimal taxAmount = Round(subtotal * (taxRate / 100m));
			decimal total = Round(subtotal + taxAmount - discount);
			decimal lineCost = Round(quantity * unitCost);

			target[AttrLineSubtotal] = new Money(subtotal);
			target[AttrLineTaxAmount] = new Money(taxAmount);
			target[AttrLineTotal] = new Money(total);
			target[AttrLineCost] = new Money(lineCost);

			localContext.Trace(
				"ComputeLine: qty={0}, price={1}, tax%={2}, discount={3} => " +
				"subtotal={4}, taxAmount={5}, total={6}, lineCost={7}.",
				quantity, price, taxRate, discount, subtotal, taxAmount, total, lineCost);
		}

		/// <summary>
		/// Post-Operation: re-aggregate all active sibling lines of the parent proposal
		/// and write the header totals + gross margin. Runs under SystemService because
		/// these are derived fields the seller must not hand-edit, and to stay working
		/// if cost/margin later become Field-Security-protected.
		/// </summary>
		private void RollUpHeader(LocalPluginContext localContext)
		{
			localContext.Trace("RollUpHeader: entered.");

			Guid proposalId = ResolveProposalId(localContext);
			if (proposalId == Guid.Empty)
			{
				localContext.Trace("No parent proposal resolved. Nothing to roll up.");
				return;
			}

			var service = localContext.SystemService;

			var query = new QueryExpression(LineEntity)
			{
				ColumnSet = new ColumnSet(
					AttrLineSubtotal, AttrLineTaxAmount, AttrLineTotal, AttrLineCost),
				Criteria = new FilterExpression()
			};
			query.Criteria.AddCondition(AttrLineProposal, ConditionOperator.Equal, proposalId);
			query.Criteria.AddCondition(AttrStateCode, ConditionOperator.Equal, StateActive);

			var lines = service.RetrieveMultiple(query).Entities;

			decimal sumSubtotal = 0m, sumTax = 0m, sumTotal = 0m, sumCost = 0m;
			foreach (var line in lines)
			{
				sumSubtotal += MoneyVal(line, AttrLineSubtotal);
				sumTax += MoneyVal(line, AttrLineTaxAmount);
				sumTotal += MoneyVal(line, AttrLineTotal);
				sumCost += MoneyVal(line, AttrLineCost);
			}

			decimal grossMargin = sumSubtotal == 0m
				? 0m
				: Round(((sumSubtotal - sumCost) / sumSubtotal) * 100m);

			var header = new Entity(HeaderEntity, proposalId);
			header[AttrHeaderSubtotal] = new Money(Round(sumSubtotal));
			header[AttrHeaderTotalTax] = new Money(Round(sumTax));
			header[AttrHeaderTotal] = new Money(Round(sumTotal));
			header[AttrHeaderTotalCost] = new Money(Round(sumCost));
			header[AttrHeaderGrossMargin] = grossMargin;

			service.Update(header);

			localContext.Trace(
				"RollUpHeader: proposal={0}, lines={1} => subtotal={2}, tax={3}, " +
				"total={4}, cost={5}, margin={6}%.",
				proposalId, lines.Count, sumSubtotal, sumTax, sumTotal, sumCost, grossMargin);
		}

		/// <summary>
		/// Resolves the parent proposal id across Create/Update/Delete:
		/// Target (Create, or Update where the lookup changed) wins, else Pre-Image
		/// (Update where the lookup was untouched, and Delete where Target is only a ref).
		/// </summary>
		private Guid ResolveProposalId(LocalPluginContext localContext)
		{
			var ctx = localContext.PluginExecutionContext;

			if (ctx.InputParameters.Contains("Target")
				&& ctx.InputParameters["Target"] is Entity target)
			{
				var fromTarget = target.GetAttributeValue<EntityReference>(AttrLineProposal);
				if (fromTarget != null) return fromTarget.Id;
			}

			if (ctx.PreEntityImages.Contains(PreImageName))
			{
				var fromImage = ctx.PreEntityImages[PreImageName]
					.GetAttributeValue<EntityReference>(AttrLineProposal);
				if (fromImage != null) return fromImage.Id;
			}

			return Guid.Empty;
		}

		// ===== Helpers =====

		/// <summary>Effective Decimal: Target, else Pre-Image, else 0.</summary>
		private static decimal GetDecimal(Entity target, Entity preImage, string attr)
		{
			if (target.Contains(attr)) return target.GetAttributeValue<decimal>(attr);
			if (preImage != null && preImage.Contains(attr)) return preImage.GetAttributeValue<decimal>(attr);
			return 0m;
		}

		/// <summary>Effective Money value: Target, else Pre-Image, else 0.</summary>
		private static decimal GetMoney(Entity target, Entity preImage, string attr)
		{
			if (target.Contains(attr))
			{
				var m = target.GetAttributeValue<Money>(attr);
				return m?.Value ?? 0m;
			}
			if (preImage != null && preImage.Contains(attr))
			{
				var m = preImage.GetAttributeValue<Money>(attr);
				return m?.Value ?? 0m;
			}
			return 0m;
		}

		private static decimal MoneyVal(Entity e, string attr)
		{
			var m = e.GetAttributeValue<Money>(attr);
			return m?.Value ?? 0m;
		}

		private static decimal Round(decimal value)
		{
			return Math.Round(value, 2, MidpointRounding.AwayFromZero);
		}
	}
}
