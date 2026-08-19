"use strict";

/**
 * OpenTavu — Proposal Line Form (tavu_proposalline)
 *
 * Shared by the Main form and the Quick Create form.
 *
 * On product selection: default the Unit of Measure (tavu_product.tavu_defaultunit),
 * default Quantity to 1, copy the Tax Rate from the price list, and — unless Override
 * Price is on — auto-fill Price Per Unit from the proposal's price list
 * (tavu_pricelistitem). Override Price is the sticky
 * manual flag for the price, mirroring tavu_probabilityismanual on Opportunity
 * (sales-model.md §6.3bis, §8bis.2.1, §8bis.3.4).
 *
 * Live amount preview: Subtotal / Tax Amount / Total — and Unit Cost / Line Cost — are
 * filled client-side as the seller picks a product and types, so nothing shows empty while
 * creating. The Pl.ProposalLine.Calculator plugin remains the source of truth on save and
 * on every non-form path (import, API, Flow); it re-stamps the unit cost from the product
 * server-side. If cost/margin later become Field-Security-protected, the server stays
 * correct (plugin via SystemService); this preview just won't persist for unauthorized users.
 *
 * Form event registration (designer → handler; pass execution context):
 *   OnLoad                    → OpenTavu.ProposalLine.Form.onLoad
 *   OnChange tavu_productid   → OpenTavu.ProposalLine.Form.onProductChange
 *   OnChange tavu_overrideprice → OpenTavu.ProposalLine.Form.onOverrideChange
 *   OnChange tavu_priceperunit  → OpenTavu.ProposalLine.Form.onPriceChange
 *   OnChange tavu_quantity    → OpenTavu.ProposalLine.Form.onQuantityChange
 *   OnChange tavu_discount    → OpenTavu.ProposalLine.Form.onDiscountChange
 *   OnChange tavu_taxrate     → OpenTavu.ProposalLine.Form.onTaxRateChange
 *
 * QUICK CREATE REQUIREMENT: tavu_proposalid must be on the Quick Create form (it may be
 * hidden). The auto-fill reads the parent proposal to find its price list; if the lookup
 * is not on the form, getAttribute returns null and price auto-fill is silently skipped.
 *
 * @author OpenTavu — Gustavo González Villani
 * SPDX-License-Identifier: MIT
 */

var OpenTavu = OpenTavu || {};
OpenTavu.ProposalLine = OpenTavu.ProposalLine || {};
OpenTavu.ProposalLine.Form = OpenTavu.ProposalLine.Form || {};

(function (Form) {

    // ============================================================
    // Schema constants — VERIFY against the live environment.
    // Doc/schema drift exists (e.g. Opportunity uses tavu_customer, not
    // tavu_customerid). Centralized so a rename is a one-line change.
    // ============================================================

    // --- tavu_proposalline (the line) ---
    var F_PRODUCT   = "tavu_product";       // Lookup -> tavu_product   (confirmed)
    var F_UOM       = "tavu_unitofmeasure"; // Lookup -> tavu_uom        (confirmed)
    var F_QUANTITY  = "tavu_quantity";      // Decimal
    var F_PRICE     = "tavu_priceperunit";  // Currency
    var F_TAXRATE   = "tavu_taxrate";       // Decimal (percent)
    var F_DISCOUNT  = "tavu_discount";      // Currency (amount)
    var F_OVERRIDE  = "tavu_overrideprice"; // Yes/No
    var F_UNITCOST  = "tavu_unitcost";      // Currency (live preview; plugin is source of truth)
    var F_LINECOST  = "tavu_linecost";      // Currency (live preview; plugin is source of truth)
    var F_SUBTOTAL  = "tavu_subtotal";      // Currency (plain, plugin-authoritative)
    var F_TAXAMOUNT = "tavu_taxamount";     // Currency (plain, plugin-authoritative)
    var F_TOTAL     = "tavu_total";         // Currency (plain, plugin-authoritative)
    var F_PROPOSAL  = "tavu_proposal";      // Lookup -> tavu_proposal   (confirmed)

    // --- tavu_proposal (the header) ---
    var PROPOSAL_ENTITY    = "tavu_proposal";
    var PROPOSAL_PRICELIST = "tavu_pricelist";   // Lookup -> tavu_pricelist (confirmed by user)

    // --- tavu_product ---
    var PRODUCT_ENTITY      = "tavu_product";
    var PRODUCT_DEFAULTUNIT = "tavu_defaultunit"; // Lookup -> tavu_uom (doc §8bis.3.2)
    var PRODUCT_COST        = "tavu_cost";        // Currency (internal unit cost)

    // --- tavu_pricelistitem ---
    var PLI_ENTITY    = "tavu_pricelistitem";
    var PLI_PRICELIST = "tavu_pricelist";   // Lookup -> tavu_pricelist  (confirmed)
    var PLI_PRODUCT   = "tavu_product";     // Lookup -> tavu_product    (confirmed)
    var PLI_PRICE     = "tavu_priceperunit";// Currency
    var PLI_TAXRATE   = "tavu_taxrate";     // Decimal (percent)

    var LOG = "[OpenTavu.ProposalLine.Form]";

    var NOTIF = {
        NO_PRICE: "opentavu_pl_no_pricelist_match",
        NO_PRICELIST: "opentavu_pl_no_pricelist_on_header"
    };
    var NOTIF_TRANSIENT_MS = 4000;

    // ============================================================
    // Event handlers
    // ============================================================

    /** @param {Xrm.ExecutionContext} executionContext */
    Form.onLoad = function (executionContext) {
        var formContext = executionContext.getFormContext();

        // On create, seed sensible defaults so the seller types as little as possible.
        if (isCreate(formContext)) {
            if (getNumber(formContext, F_QUANTITY) === null) {
                setValue(formContext, F_QUANTITY, 1);
            }
            if (getValueRaw(formContext, F_OVERRIDE) === null) {
                setValue(formContext, F_OVERRIDE, false);
            }
        }

        recalcAmounts(formContext);
    };

    /** Product chosen: default UoM + quantity, then refresh price (override-aware). */
    Form.onProductChange = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var productRef = getLookupValue(formContext, F_PRODUCT);

        if (!productRef) {
            // Product cleared — leave the rest as-is; nothing to default from.
            return;
        }

        var productId = stripBraces(productRef.id);

        Xrm.WebApi.retrieveRecord(
            PRODUCT_ENTITY, productId,
            "?$select=_" + PRODUCT_DEFAULTUNIT + "_value," + PRODUCT_COST
        ).then(
            function (product) {
                // Default Unit of Measure from the product (only if empty, so we never
                // stomp a UoM the seller deliberately changed).
                if (!getLookupValue(formContext, F_UOM)) {
                    var uom = buildLookup(product, PRODUCT_DEFAULTUNIT, "tavu_uom");
                    if (uom) setValue(formContext, F_UOM, [uom]);
                }
                // Default quantity to 1 if still empty.
                if (getNumber(formContext, F_QUANTITY) === null) {
                    setValue(formContext, F_QUANTITY, 1);
                }
                // Snapshot the unit cost from the product for instant on-form preview.
                // The Pl.ProposalLine.Calculator plugin re-stamps it server-side on save
                // and stays the source of truth for import/API/Flow. No-op on Quick Create
                // (the field is not on that form).
                var cost = product[PRODUCT_COST];
                setValueIfPresent(formContext, F_UNITCOST,
                    (cost === undefined || cost === null) ? null : cost);

                // Pull price (unless overridden) and tax rate from the price list.
                refreshPriceFromList(formContext, productId);
            },
            function (error) {
                console.warn(LOG, "retrieve product failed:", error);
                recalcAmounts(formContext);
            }
        );
    };

    /** Override toggled: No -> re-pull list price; Yes -> keep the manual price. */
    Form.onOverrideChange = function (executionContext) {
        var formContext = executionContext.getFormContext();

        if (isOverride(formContext)) {
            // Manual mode now — respect whatever price is on the line.
            recalcAmounts(formContext);
            return;
        }

        var productRef = getLookupValue(formContext, F_PRODUCT);
        if (!productRef) { recalcAmounts(formContext); return; }
        refreshPriceFromList(formContext, stripBraces(productRef.id));
    };

    /**
     * The seller manually edited Price Per Unit. setValue() does NOT fire OnChange, so
     * this only runs on a genuine user edit (never on our programmatic auto-fill). Flip
     * Override on so the auto-fill stops overwriting their price — the derived-flag path.
     */
    Form.onPriceChange = function (executionContext) {
        var formContext = executionContext.getFormContext();
        if (!isOverride(formContext)) {
            setValue(formContext, F_OVERRIDE, true);
        }
        recalcAmounts(formContext);
    };

    Form.onQuantityChange = function (executionContext) {
        recalcAmounts(executionContext.getFormContext());
    };

    Form.onDiscountChange = function (executionContext) {
        recalcAmounts(executionContext.getFormContext());
    };

    Form.onTaxRateChange = function (executionContext) {
        recalcAmounts(executionContext.getFormContext());
    };

    // ============================================================
    // Core logic
    // ============================================================

    /**
     * Reads the parent proposal's price list, finds the matching price list item for the
     * product, and writes Price Per Unit. Mirrors the plugin's input contract; the plugin
     * stays authoritative on save. Skips quietly when the parent lookup is absent (e.g. a
     * Quick Create form that does not include tavu_proposalid).
     */
    function refreshPriceFromList(formContext, productId) {
        var proposalRef = getLookupValue(formContext, F_PROPOSAL);
        if (!proposalRef) {
            console.warn(LOG, F_PROPOSAL + " not on form — price auto-fill skipped.");
            recalcAmounts(formContext);
            return;
        }

        var proposalId = stripBraces(proposalRef.id);

        Xrm.WebApi.retrieveRecord(
            PROPOSAL_ENTITY, proposalId,
            "?$select=_" + PROPOSAL_PRICELIST + "_value"
        ).then(function (proposal) {
            var priceListId = proposal["_" + PROPOSAL_PRICELIST + "_value"];
            if (!priceListId) {
                notifyTransient(formContext,
                    "This proposal has no price list selected. Enter Price Per Unit manually.",
                    "INFO", NOTIF.NO_PRICELIST);
                recalcAmounts(formContext);
                return;
            }

            var filter =
                "?$select=" + PLI_PRICE + "," + PLI_TAXRATE +
                "&$filter=_" + PLI_PRICELIST + "_value eq " + priceListId +
                " and _" + PLI_PRODUCT + "_value eq " + productId +
                "&$top=1";

            return Xrm.WebApi.retrieveMultipleRecords(PLI_ENTITY, filter).then(function (res) {
                var override = isOverride(formContext);

                if (res.entities && res.entities.length > 0) {
                    var item = res.entities[0];

                    // Price respects the Override flag; tax rate is jurisdictional, not a
                    // price decision, so it is copied whenever the list provides it.
                    if (!override) {
                        var listPrice = item[PLI_PRICE];
                        setValue(formContext, F_PRICE, (listPrice === undefined) ? null : listPrice);
                    }
                    var listTax = item[PLI_TAXRATE];
                    if (listTax !== undefined && listTax !== null) {
                        setValue(formContext, F_TAXRATE, listTax);
                    }
                    formContext.ui.clearFormNotification(NOTIF.NO_PRICE);
                } else if (!override) {
                    // No list price for this product — let the seller type it. Leave tax
                    // rate untouched (no source to copy from).
                    setValue(formContext, F_PRICE, null);
                    notifyTransient(formContext,
                        "No price list entry for this product. Enter Price Per Unit manually.",
                        "INFO", NOTIF.NO_PRICE);
                }
                recalcAmounts(formContext);
            });
        }, function (error) {
            console.warn(LOG, "price list lookup failed:", error);
            recalcAmounts(formContext);
        });
    }

    /**
     * Recomputes the line's money fields for instant on-form feedback:
     *   subtotal  = quantity * priceperunit
     *   taxamount = subtotal * (taxrate / 100)      (tax on subtotal, pre-discount)
     *   total     = subtotal + taxamount - discount (discount is a currency amount)
     * Identical to Pl.ProposalLine.Calculator. Guards every attribute so it is a no-op
     * on forms that omit the computed fields (e.g. Quick Create).
     */
    function recalcAmounts(formContext) {
        var quantity = getNumber(formContext, F_QUANTITY) || 0;
        var price    = getNumber(formContext, F_PRICE) || 0;
        var taxRate  = getNumber(formContext, F_TAXRATE) || 0;
        var discount = getNumber(formContext, F_DISCOUNT) || 0;
        var unitCost = getNumber(formContext, F_UNITCOST) || 0;

        var subtotal  = round2(quantity * price);
        var taxAmount = round2(subtotal * (taxRate / 100));
        var total     = round2(subtotal + taxAmount - discount);
        var lineCost  = round2(quantity * unitCost);

        setValueIfPresent(formContext, F_SUBTOTAL, subtotal);
        setValueIfPresent(formContext, F_TAXAMOUNT, taxAmount);
        setValueIfPresent(formContext, F_TOTAL, total);
        setValueIfPresent(formContext, F_LINECOST, lineCost);
    }

    // ============================================================
    // Helpers
    // ============================================================

    function isCreate(formContext) {
        // 1 = Create, 2 = Update (Xrm form type enum).
        return formContext.ui.getFormType() === 1;
    }

    function isOverride(formContext) {
        return getValueRaw(formContext, F_OVERRIDE) === true;
    }

    function getAttr(formContext, name) {
        return formContext.getAttribute(name) || null;
    }

    function getValueRaw(formContext, name) {
        var attr = getAttr(formContext, name);
        if (!attr) return null;
        var v = attr.getValue();
        return (v === undefined) ? null : v;
    }

    function getNumber(formContext, name) {
        var v = getValueRaw(formContext, name);
        return (v === null || isNaN(v)) ? null : Number(v);
    }

    function getLookupValue(formContext, name) {
        var v = getValueRaw(formContext, name);
        return (v && v.length > 0) ? v[0] : null;
    }

    function setValue(formContext, name, value) {
        var attr = getAttr(formContext, name);
        if (attr && attr.setValue) attr.setValue(value);
    }

    /** Like setValue but a no-op when the attribute is not on the form. */
    function setValueIfPresent(formContext, name, value) {
        var attr = getAttr(formContext, name);
        if (attr && attr.setValue) attr.setValue(value);
    }

    /**
     * Builds a lookup array entry {id, name, entityType} from a retrieved record's
     * _<field>_value plus its OData annotations.
     */
    function buildLookup(record, fieldName, fallbackEntityType) {
        var valueKey = "_" + fieldName + "_value";
        var id = record[valueKey];
        if (!id) return null;
        return {
            id: id,
            name: record[valueKey + "@OData.Community.Display.V1.FormattedValue"] || "",
            entityType: record[valueKey + "@Microsoft.Dynamics.CRM.lookuplogicalname"] || fallbackEntityType
        };
    }

    function stripBraces(guid) {
        return String(guid).replace(/[{}]/g, "");
    }

    function round2(value) {
        return Math.round((Number(value) + Number.EPSILON) * 100) / 100;
    }

    function notifyTransient(formContext, message, level, id) {
        formContext.ui.setFormNotification(message, level, id);
        setTimeout(function () {
            formContext.ui.clearFormNotification(id);
        }, NOTIF_TRANSIENT_MS);
    }

})(OpenTavu.ProposalLine.Form);
