"use strict";

/**
 * OpenTavu — Account Form Script
 *
 * Form: Account > Main
 * Purpose: Cascading location lookups (custom tavu_country / tavu_stateprovince /
 *          tavu_city). State/Province is filtered to the chosen Country, City to the
 *          chosen State/Province; changing a parent clears its dependent children.
 *          Mirrors the Contact form cascade (sales-model.md §5.7). The Account has no
 *          location parent, so there is no inheritance here — cascade + requirements only.
 *
 * Form registration (designer → handler; pass execution context: yes):
 *   OnLoad                     → OpenTavu.Account.Form.onLoad
 *   tavu_country OnChange      → OpenTavu.Account.Form.onCountryChange
 *   tavu_stateprovince OnChange → OpenTavu.Account.Form.onStateProvinceChange
 *
 * @author  OpenTavu — Gustavo González Villani
 * @license MIT
 * @version 0.1.0
 */

var OpenTavu = OpenTavu || {};
OpenTavu.Account = OpenTavu.Account || {};
OpenTavu.Account.Form = OpenTavu.Account.Form || {};

(function (Form) {

    // ============================================================
    // Constants — location cascade
    // ============================================================

    // Lookup fields on Account.
    var FIELD_COUNTRY = "tavu_country";              // lookup -> tavu_country
    var FIELD_STATEPROVINCE = "tavu_stateprovince";  // lookup -> tavu_stateprovince
    var FIELD_CITY = "tavu_city";                    // lookup -> tavu_city

    // Provenance lookup to tavu_lead, set by Pl.Lead.PromoteLead only when the account was
    // created from a lead. Shown only when it has a value; always read-only.
    var FIELD_ORIGINATING_LEAD = "tavu_originatinglead";

    // Dependent (child) tables and the attribute on each that points to its parent.
    var STATEPROVINCE_TABLE = "tavu_stateprovince";
    var CITY_TABLE = "tavu_city";
    var STATEPROVINCE_PARENT_ATTR = "tavu_country";       // tavu_stateprovince.tavu_country
    var CITY_PARENT_ATTR = "tavu_stateprovince";          // tavu_city.tavu_stateprovince

    // ============================================================
    // Event handlers
    // ============================================================

    /** @param {Xrm.ExecutionContext} executionContext */
    Form.onLoad = function (executionContext) {
        Form.setLocationFieldRequirements(executionContext);
        Form.applyLocationFilters(executionContext);
        Form.applyOriginatingLeadVisibility(executionContext);
    };

    /** OnChange for tavu_country — clears State and City to enforce cascading. */
    Form.onCountryChange = function (executionContext) {
        var formContext = executionContext.getFormContext();
        setAttribute(formContext, FIELD_STATEPROVINCE, null);
        setAttribute(formContext, FIELD_CITY, null);
    };

    /** OnChange for tavu_stateprovince — clears City to enforce cascading. */
    Form.onStateProvinceChange = function (executionContext) {
        var formContext = executionContext.getFormContext();
        setAttribute(formContext, FIELD_CITY, null);
    };

    // ============================================================
    // Core logic — exposed via Form namespace
    // ============================================================

    /** Country required; State/City optional (per sales-model.md §5.1). */
    Form.setLocationFieldRequirements = function (executionContext) {
        var formContext = executionContext.getFormContext();
        setRequired(formContext, FIELD_COUNTRY, "required");
        setRequired(formContext, FIELD_STATEPROVINCE, "none");
        setRequired(formContext, FIELD_CITY, "none");
    };

    /**
     * Cascading lookup filter: State/Province restricted to the chosen Country, City to
     * the chosen State/Province. PreSearch handlers are added ONCE; each reads the live
     * parent value at search time. No parent selected → no filter (shows everything).
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.applyLocationFilters = function (executionContext) {
        var formContext = executionContext.getFormContext();
        wirePreSearch(formContext, FIELD_STATEPROVINCE, STATEPROVINCE_TABLE,
            FIELD_COUNTRY, STATEPROVINCE_PARENT_ATTR);
        wirePreSearch(formContext, FIELD_CITY, CITY_TABLE,
            FIELD_STATEPROVINCE, CITY_PARENT_ATTR);
    };

    /**
     * Shows the Originating Lead lookup only when it has a value (an account promoted from a
     * lead); hides it for directly-created accounts. Always read-only (system-set provenance).
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.applyOriginatingLeadVisibility = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var attr = formContext.getAttribute(FIELD_ORIGINATING_LEAD);
        var value = attr ? attr.getValue() : null;
        var hasValue = !!(value && value.length > 0);
        setControlVisible(formContext, FIELD_ORIGINATING_LEAD, hasValue);
        setControlDisabled(formContext, FIELD_ORIGINATING_LEAD, true);
    };

    // ============================================================
    // Internal helpers
    // ============================================================

    function setAttribute(formContext, schemaName, value) {
        var attr = formContext.getAttribute(schemaName);
        if (attr) attr.setValue(value);
    }

    function setRequired(formContext, schemaName, level) {
        var attr = formContext.getAttribute(schemaName);
        if (attr) attr.setRequiredLevel(level);
    }

    /** Safe control visibility setter. Silent no-op if the field is not on this form. */
    function setControlVisible(formContext, schemaName, visible) {
        var attr = formContext.getAttribute(schemaName);
        if (!attr) return;
        var controls = attr.controls.get();
        if (!controls) return;
        controls.forEach(function (ctrl) {
            if (ctrl && ctrl.setVisible) ctrl.setVisible(visible);
        });
    }

    /** Safe control disable setter. Silent no-op if the field is not on this form. */
    function setControlDisabled(formContext, schemaName, disabled) {
        var attr = formContext.getAttribute(schemaName);
        if (!attr) return;
        var controls = attr.controls.get();
        if (!controls) return;
        controls.forEach(function (ctrl) {
            if (ctrl && ctrl.setDisabled) ctrl.setDisabled(disabled);
        });
    }

    /**
     * Adds a PreSearch filter to a dependent lookup so it only offers child records
     * whose parent link equals the currently selected parent. Added once; the closure
     * reads the live parent value on every search. No-op (shows all) when no parent.
     */
    function wirePreSearch(formContext, childLookupField, childTable, parentField, parentLinkAttr) {
        var control = formContext.getControl(childLookupField);
        if (!control || !control.addPreSearch) return;

        control.addPreSearch(function () {
            var parentAttr = formContext.getAttribute(parentField);
            var parentValue = parentAttr ? parentAttr.getValue() : null;
            if (!parentValue || parentValue.length === 0) return; // no parent → no filter

            var parentId = parentValue[0].id.replace(/[{}]/g, "");
            var filterXml =
                "<filter type='and'>" +
                "<condition attribute='" + parentLinkAttr + "' operator='eq' value='" + parentId + "' />" +
                "</filter>";
            control.addCustomFilter(filterXml, childTable);
        });
    }

})(OpenTavu.Account.Form);
