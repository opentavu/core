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
