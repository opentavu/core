"use strict";

/**
 * OpenTavu — Contact Form Script
 *
 * Form: Contact > Main
 * Purpose: Adaptive form logic that switches between three modes:
 *          - UNSET (new record, not saved yet)
 *          - B2B Interlocutor (parentcustomerid → Account)
 *          - B2C Direct Client (parentcustomerid empty after save)
 *
 * Mode detection rules (see sales-model.md §5):
 *   - Record is new (no id) → MODE_UNSET
 *   - parentcustomerid populated AND points to Account → MODE_B2B
 *   - Otherwise → MODE_B2C
 *
 * Read-only fields enforced by this script (these are populated by automation,
 * not by users):
 *   - tavu_iscustomer        ← Plugin/Flow on opportunity Won close
 *   - tavu_customersince     ← Plugin/Flow on opportunity Won close
 *   - tavu_lastengagementdate ← Module 3 (Activity Capture)
 *
 * Customer Tier policy:
 *   - B2C: editable (manual assignment by user)
 *   - B2B: read-only (inherited visually from parent Account via Quick View Form)
 *   - UNSET: editable
 *
 * Form registration:
 *   OnLoad → OpenTavu.Contact.Form.onLoad                  (pass execution context: yes)
 *   OnSave → OpenTavu.Contact.Form.onSave                  (pass execution context: yes)
 *
 *   parentcustomerid OnChange         → OpenTavu.Contact.Form.onParentCustomerChange
 *   tavu_country OnChange      → OpenTavu.Contact.Form.onCountryChange
 *   tavu_stateprovince OnChange → OpenTavu.Contact.Form.onStateProvinceChange
 *
 * Header fields (configured in form designer, not by this script):
 *   tavu_engagementstatus, tavu_opencasescount, tavu_lastemaildate, tavu_lastmeetingdate
 *
 * Quick View Forms used in sections (configured in form designer):
 *   - SectionParentAccountCard: QVF "Account: Parent Card" (B2B only)
 *   - SectionKeyMetricsB2B:     QVF "Account: Key Metrics" (B2B only)
 *
 * @author  OpenTavu — Gustavo González Villani
 * @license MIT
 * @version 0.3.0
 *
 * v0.3.0: the editable Company Name (parentcustomerid) lookup is now kept visible in every mode
 *         (B2C/UNSET included), so a direct client can be attached to an account and promoted to
 *         B2B. Previously it was only reachable in B2B, which trapped B2C contacts.
 */

var OpenTavu = OpenTavu || {};
OpenTavu.Contact = OpenTavu.Contact || {};
OpenTavu.Contact.Form = OpenTavu.Contact.Form || {};

(function (Form) {

    // ============================================================
    // Constants
    // ============================================================

    var MODE_UNSET = "UNSET";
    var MODE_B2B = "B2B";
    var MODE_B2C = "B2C";

    // tavu_engagementstatus choice values (must match Choice definition in Dataverse)
    var ENGAGEMENT_COLD = 1;
    var ENGAGEMENT_ENGAGED = 2;
    var ENGAGEMENT_INACTIVE = 3;

    // Defaults — used if tavu_systemsettings is unavailable
    var DEFAULT_INACTIVITY_THRESHOLD_DAYS = 90;
    var DEFAULT_ENGAGED_WINDOW_DAYS = 7;

    // Form notification IDs
    var NOTIF_UNSET_MODE = "contact_unset_mode_hint";
    var NOTIF_LOCATION_INHERIT = "contact_location_inherited";

    // Tab and section names (must match the names defined in the form designer)
    var TAB_SUMMARY = "tab_summary";
    var SECTION_CLIENT_STATUS = "SectionClientStatus";
    var SECTION_PARENT_ACCOUNT_CARD = "SectionParentAccountCard";
    var SECTION_COLLEAGUES = "SectionColleagues";
    var SECTION_KEY_METRICS_B2B = "SectionKeyMetricsB2B";
    var SECTION_KEY_METRICS_B2C = "SectionKeyMetricsB2C";

    // Subgrid control names (must match form designer)
    var SUBGRID_OPPORTUNITIES = "Opportunities";
    var SUBGRID_CASES = "Cases";

    // Fields that are always read-only (system-managed)
    var SYSTEM_MANAGED_FIELDS = [
        "tavu_iscustomer",
        "tavu_customersince",
        "tavu_lastengagementdate",
        "tavu_originatinglead"
    ];

    // Provenance lookup to tavu_lead, set by Pl.Lead.PromoteLead only when the record was
    // created from a lead. Shown only when it has a value (hidden for directly-created records).
    var FIELD_ORIGINATING_LEAD = "tavu_originatinglead";

    // Editable parent-account link (Company Name). This is the lookup the user sets to attach the
    // contact to an Account (B2B). It must be placed in an ALWAYS-VISIBLE section of the form (e.g.
    // the main Contact Information section), NOT inside SectionParentAccountCard (that section is the
    // read-only B2B-only Quick View card). This script keeps its control visible in every mode so a
    // B2C or brand-new contact can be linked to an account and promoted to B2B (no chicken-and-egg).
    var FIELD_PARENT_ACCOUNT = "parentcustomerid";

    // Location cascade — custom lookups on Contact/Account, and the parent-link
    // attribute on each child table used to filter the dependent lookup.
    var FIELD_COUNTRY = "tavu_country";              // lookup -> tavu_country
    var FIELD_STATEPROVINCE = "tavu_stateprovince";  // lookup -> tavu_stateprovince
    var FIELD_CITY = "tavu_city";                    // lookup -> tavu_city
    var STATEPROVINCE_TABLE = "tavu_stateprovince";
    var CITY_TABLE = "tavu_city";
    var STATEPROVINCE_PARENT_ATTR = "tavu_country";       // tavu_stateprovince.tavu_country
    var CITY_PARENT_ATTR = "tavu_stateprovince";          // tavu_city.tavu_stateprovince

    // ============================================================
    // Event handlers
    // ============================================================

    /**
     * Form OnLoad — orchestrates mode detection, layout adaptation,
     * field requirements, and read-only enforcement.
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.onLoad = function (executionContext) {
        Form.applyAdaptiveLayout(executionContext);
        Form.setLocationFieldRequirements(executionContext);
        Form.applyLocationFilters(executionContext);
        Form.enforceReadOnlyFields(executionContext);
        Form.applyOriginatingLeadVisibility(executionContext);
    };

    /**
     * Form OnSave handler. Reserved for future validations.
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.onSave = function (executionContext) {
        // Reserved for future save-time validations.
    };

    /**
     * OnChange for parentcustomerid — re-evaluates mode, refreshes layout,
     * inherits location, and re-enforces read-only policy for Customer Tier.
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.onParentCustomerChange = function (executionContext) {
        Form.applyAdaptiveLayout(executionContext);
        Form.inheritLocationFromAccount(executionContext);
        Form.enforceReadOnlyFields(executionContext);
    };

    /**
     * OnChange for tavu_country. Clears State and City to enforce cascading.
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.onCountryChange = function (executionContext) {
        var formContext = executionContext.getFormContext();
        setAttribute(formContext, FIELD_STATEPROVINCE, null);
        setAttribute(formContext, FIELD_CITY, null);
    };

    /**
     * OnChange for tavu_stateprovince. Clears City to enforce cascading.
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.onStateProvinceChange = function (executionContext) {
        var formContext = executionContext.getFormContext();
        setAttribute(formContext, FIELD_CITY, null);
    };

    // ============================================================
    // Core logic — exposed via Form namespace
    // ============================================================

    /**
     * Detects the current mode and applies section visibility, subgrid filters,
     * and an informational banner for new (unsaved) records.
     *
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.applyAdaptiveLayout = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var mode = detectMode(formContext);

        formContext.ui.clearFormNotification(NOTIF_UNSET_MODE);

        // The editable Company Name (parentcustomerid) lookup is ALWAYS reachable, in every mode,
        // so a B2C or new contact can be attached to an account (and thereby promoted to B2B).
        // Only the read-only Parent Account *card* (Quick View) is mode-gated below.
        setControlVisible(formContext, FIELD_PARENT_ACCOUNT, true);

        if (mode === MODE_UNSET) {
            // New record — hide all mode-specific sections.
            // User cannot decide B2B vs B2C until parentcustomerid is set
            // (or explicitly left empty by saving).
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_PARENT_ACCOUNT_CARD, false);
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_COLLEAGUES, false);
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_CLIENT_STATUS, false);
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_KEY_METRICS_B2B, false);
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_KEY_METRICS_B2C, false);

            formContext.ui.setFormNotification(
                "Set a Parent Account to treat this contact as a B2B interlocutor, or leave empty and save to treat as a direct client (B2C).",
                "INFO",
                NOTIF_UNSET_MODE
            );
            return;
        }

        if (mode === MODE_B2B) {
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_PARENT_ACCOUNT_CARD, true);
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_COLLEAGUES, true);
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_CLIENT_STATUS, false);
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_KEY_METRICS_B2B, true);
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_KEY_METRICS_B2C, false);
            applyB2BSubgridFilters(formContext);
        } else {
            // MODE_B2C
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_PARENT_ACCOUNT_CARD, false);
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_COLLEAGUES, false);
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_CLIENT_STATUS, true);
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_KEY_METRICS_B2B, false);
            setSectionVisible(formContext, TAB_SUMMARY, SECTION_KEY_METRICS_B2C, true);
            applyB2CSubgridFilters(formContext);
        }
    };

    /**
     * Enforces read-only policy on system-managed fields and on Customer Tier
     * according to mode:
     *   - SYSTEM_MANAGED_FIELDS: always disabled
     *   - tavu_customertier: disabled in B2B (inherited from Account), editable otherwise
     *
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.enforceReadOnlyFields = function (executionContext) {
        var formContext = executionContext.getFormContext();

        SYSTEM_MANAGED_FIELDS.forEach(function (fieldName) {
            setControlDisabled(formContext, fieldName, true);
        });

        var mode = detectMode(formContext);
        var tierDisabled = (mode === MODE_B2B);
        setControlDisabled(formContext, "tavu_customertier", tierDisabled);
    };

    /**
     * Shows the Originating Lead lookup only when it has a value (a record promoted from a
     * lead); hides it for directly-created records so the form is not cluttered with an empty
     * provenance field. The field itself is always read-only (see SYSTEM_MANAGED_FIELDS).
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.applyOriginatingLeadVisibility = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var attr = formContext.getAttribute(FIELD_ORIGINATING_LEAD);
        var value = attr ? attr.getValue() : null;
        var hasValue = !!(value && value.length > 0);
        setControlVisible(formContext, FIELD_ORIGINATING_LEAD, hasValue);
    };

    /**
     * In B2B mode, inherits Country/State/City from the parent Account when
     * those fields on Contact are empty. Convenience for the consultant —
     * they can always override.
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.inheritLocationFromAccount = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var mode = detectMode(formContext);
        if (mode !== MODE_B2B) return;

        var parentAttr = formContext.getAttribute("parentcustomerid");
        if (!parentAttr || !parentAttr.getValue()) return;

        var parentValue = parentAttr.getValue()[0];
        if (parentValue.entityType !== "account") return;

        var countryAttr = formContext.getAttribute("tavu_country");
        if (!countryAttr) return;

        if (countryAttr.getValue() !== null) return; // Do not overwrite manual entry

        var accountId = parentValue.id.replace("{", "").replace("}", "");

        formContext.ui.setFormNotification(
            "Inheriting location from parent Account...",
            "INFO",
            NOTIF_LOCATION_INHERIT
        );

        Xrm.WebApi.retrieveRecord(
            "account",
            accountId,
            "?$select=_tavu_country_value,_tavu_stateprovince_value,_tavu_city_value"
        ).then(
            function success(result) {
                formContext.ui.clearFormNotification(NOTIF_LOCATION_INHERIT);

                applyLookupFromODataResult(formContext, "tavu_country",
                    result, "_tavu_country_value", "tavu_country");
                applyLookupFromODataResult(formContext, "tavu_stateprovince",
                    result, "_tavu_stateprovince_value", "tavu_stateprovince");
                applyLookupFromODataResult(formContext, "tavu_city",
                    result, "_tavu_city_value", "tavu_city");
            },
            function error(err) {
                formContext.ui.clearFormNotification(NOTIF_LOCATION_INHERIT);
                console.error("[OpenTavu.Contact.Form.inheritLocationFromAccount] " + err.message);
            }
        );
    };

    /**
     * Sets requirement levels for location fields per sales-model.md §5.1.
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.setLocationFieldRequirements = function (executionContext) {
        var formContext = executionContext.getFormContext();
        setRequired(formContext, FIELD_COUNTRY, "required");
        setRequired(formContext, FIELD_STATEPROVINCE, "none");
        setRequired(formContext, FIELD_CITY, "none");
    };

    /**
     * Cascading lookup filter: State/Province is restricted to the chosen Country,
     * and City to the chosen State/Province. PreSearch handlers are added ONCE; each
     * runs at search time and reads the current parent value, so changing the Country
     * and reopening the State lookup automatically reflects the new scope. When no
     * parent is selected, no filter is applied (the lookup shows everything).
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
    // Future hooks — reserved for Module 1 and Module 3
    // ============================================================

    /**
     * Reserved for Module 3 (Activity Capture). Will refresh engagement
     * timestamps when Module 3 pushes new email/meeting signals.
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.refreshEngagementMetrics = function (executionContext) {
        // TODO: implement when Module 3 is live.
    };

    /**
     * Reserved for richer engagement evaluation (e.g. sentiment-weighted)
     * once Module 1 provides sentiment signals.
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.evaluateEngagementStatus = function (executionContext) {
        // TODO: extend when Module 1 sentiment signals are available.
    };

    // ============================================================
    // Internal helpers — NOT exposed on the Form namespace
    // ============================================================

    /**
     * @param {object} formContext
     * @returns {string} MODE_UNSET, MODE_B2B, or MODE_B2C
     */
    function detectMode(formContext) {
        // If the record has no id, it is unsaved (create form).
        var recordId = formContext.data.entity.getId();
        if (!recordId || recordId === "") {
            return MODE_UNSET;
        }

        var parentAttr = formContext.getAttribute("parentcustomerid");
        if (!parentAttr) return MODE_B2C;

        var parentValue = parentAttr.getValue();
        if (!parentValue || parentValue.length === 0) return MODE_B2C;

        if (parentValue[0].entityType === "account") return MODE_B2B;
        return MODE_B2C;
    }

    /**
     * Safe section visibility setter — silent no-op if tab or section is missing
     * (allows the script to run on forms with minor configuration variations).
     */
    function setSectionVisible(formContext, tabName, sectionName, visible) {
        var tab = formContext.ui.tabs.get(tabName);
        if (!tab) return;
        var section = tab.sections.get(sectionName);
        if (section) section.setVisible(visible);
    }

    /**
     * Safe attribute setter — silent no-op if attribute is missing.
     */
    function setAttribute(formContext, schemaName, value) {
        var attr = formContext.getAttribute(schemaName);
        if (attr) attr.setValue(value);
    }

    /**
     * Safe required-level setter.
     */
    function setRequired(formContext, schemaName, level) {
        var attr = formContext.getAttribute(schemaName);
        if (attr) attr.setRequiredLevel(level);
    }

    /**
     * Safe control disable setter. Iterates over all controls of a given field
     * (a field can have multiple controls on the same form: header + body).
     */
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
     * Safe control visibility setter. Iterates over all controls of a field and shows/hides
     * them. Silent no-op if the attribute is missing (field not on this form).
     */
    function setControlVisible(formContext, schemaName, visible) {
        var attr = formContext.getAttribute(schemaName);
        if (!attr) return;
        var controls = attr.controls.get();
        if (!controls) return;
        controls.forEach(function (ctrl) {
            if (ctrl && ctrl.setVisible) ctrl.setVisible(visible);
        });
    }

    /**
     * Applies subgrid filters for B2B mode.
     * B2B subgrids are scoped to Opportunities/Cases where this Contact is
     * primary contact (not all opps/cases of the parent Account).
     */
    function applyB2BSubgridFilters(formContext) {
        var contactId = formContext.data.entity.getId();
        if (!contactId) return;
        contactId = contactId.replace("{", "").replace("}", "");

        var filter = "<filter type='and'>" +
            "<condition attribute='tavu_primarycontact' operator='eq' value='" + contactId + "' />" +
            "</filter>";

        applySubgridFilter(formContext, SUBGRID_OPPORTUNITIES, filter, "B2B Opportunities");
        applySubgridFilter(formContext, SUBGRID_CASES, filter, "B2B Cases");
    }

    /**
     * Applies subgrid filters for B2C mode.
     * B2C subgrids filter by tavu_contact (auto-populated typed lookup field).
     */
    function applyB2CSubgridFilters(formContext) {
        var contactId = formContext.data.entity.getId();
        if (!contactId) return;
        contactId = contactId.replace("{", "").replace("}", "");

        var filter = "<filter type='and'>" +
            "<condition attribute='tavu_contact' operator='eq' value='" + contactId + "' />" +
            "</filter>";

        applySubgridFilter(formContext, SUBGRID_OPPORTUNITIES, filter, "B2C Opportunities");
        applySubgridFilter(formContext, SUBGRID_CASES, filter, "B2C Cases");
    }

    /**
     * Generic safe subgrid filter applier — wraps setFilterXml in try/catch
     * because subgrid controls may not be ready at OnLoad time.
     */
    function applySubgridFilter(formContext, gridName, filterXml, contextLabel) {
        try {
            var grid = formContext.getControl(gridName);
            if (!grid) return;
            if (grid.setFilterXml) grid.setFilterXml(filterXml);
            if (grid.refresh) grid.refresh();
        } catch (e) {
            console.warn("[OpenTavu.Contact.Form] Could not filter " + contextLabel + " subgrid: " + e.message);
        }
    }

    /**
     * Applies a lookup value extracted from an OData WebApi result onto a form
     * attribute. Handles the OData-formatted-value annotation for the display name.
     */
    function applyLookupFromODataResult(formContext, targetAttr, result, idKey, entityType) {
        var attr = formContext.getAttribute(targetAttr);
        if (!attr) return;

        var id = result[idKey];
        if (!id) return;

        var name = result[idKey + "@OData.Community.Display.V1.FormattedValue"];
        attr.setValue([{ id: id, name: name, entityType: entityType }]);
    }

    /**
     * Adds a PreSearch filter to a dependent lookup so it only offers child records
     * whose parent link equals the currently selected parent. Added once; the closure
     * reads the live parent value on every search. No-op (shows all) when no parent.
     *
     * @param {object} formContext
     * @param {string} childLookupField  the dependent lookup field on this form
     * @param {string} childTable        logical name of the dependent table (for addCustomFilter)
     * @param {string} parentField       the parent lookup field on this form
     * @param {string} parentLinkAttr    attribute on the child table that points to the parent
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

})(OpenTavu.Contact.Form);