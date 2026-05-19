"use strict";

/**
 * OpenTavu — Contact Main Form Script
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
 *   OnLoad → OpenTavu.Contact.MainForm.onLoad                  (pass execution context: yes)
 *   OnSave → OpenTavu.Contact.MainForm.onSave                  (pass execution context: yes)
 *
 *   parentcustomerid OnChange         → OpenTavu.Contact.MainForm.onParentCustomerChange
 *   tavu_addresscountry OnChange      → OpenTavu.Contact.MainForm.onCountryChange
 *   tavu_addressstateprovince OnChange → OpenTavu.Contact.MainForm.onStateProvinceChange
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
 * @version 0.2.0
 */

var OpenTavu = OpenTavu || {};
OpenTavu.Contact = OpenTavu.Contact || {};
OpenTavu.Contact.MainForm = OpenTavu.Contact.MainForm || {};

(function (MainForm) {

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
        "tavu_lastengagementdate"
    ];

    // ============================================================
    // Event handlers
    // ============================================================

    /**
     * Form OnLoad — orchestrates mode detection, layout adaptation,
     * field requirements, and read-only enforcement.
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.onLoad = function (executionContext) {
        MainForm.applyAdaptiveLayout(executionContext);
        MainForm.setLocationFieldRequirements(executionContext);
        MainForm.enforceReadOnlyFields(executionContext);
    };

    /**
     * Form OnSave handler. Reserved for future validations.
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.onSave = function (executionContext) {
        // Reserved for future save-time validations.
    };

    /**
     * OnChange for parentcustomerid — re-evaluates mode, refreshes layout,
     * inherits location, and re-enforces read-only policy for Customer Tier.
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.onParentCustomerChange = function (executionContext) {
        MainForm.applyAdaptiveLayout(executionContext);
        MainForm.inheritLocationFromAccount(executionContext);
        MainForm.enforceReadOnlyFields(executionContext);
    };

    /**
     * OnChange for tavu_addresscountry. Clears State and City to enforce cascading.
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.onCountryChange = function (executionContext) {
        var formContext = executionContext.getFormContext();
        setAttribute(formContext, "tavu_addressstateprovince", null);
        setAttribute(formContext, "tavu_addresscity", null);
    };

    /**
     * OnChange for tavu_addressstateprovince. Clears City to enforce cascading.
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.onStateProvinceChange = function (executionContext) {
        var formContext = executionContext.getFormContext();
        setAttribute(formContext, "tavu_addresscity", null);
    };

    // ============================================================
    // Core logic — exposed via MainForm namespace
    // ============================================================

    /**
     * Detects the current mode and applies section visibility, subgrid filters,
     * and an informational banner for new (unsaved) records.
     *
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.applyAdaptiveLayout = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var mode = detectMode(formContext);

        formContext.ui.clearFormNotification(NOTIF_UNSET_MODE);

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
    MainForm.enforceReadOnlyFields = function (executionContext) {
        var formContext = executionContext.getFormContext();

        SYSTEM_MANAGED_FIELDS.forEach(function (fieldName) {
            setControlDisabled(formContext, fieldName, true);
        });

        var mode = detectMode(formContext);
        var tierDisabled = (mode === MODE_B2B);
        setControlDisabled(formContext, "tavu_customertier", tierDisabled);
    };

    /**
     * In B2B mode, inherits Country/State/City from the parent Account when
     * those fields on Contact are empty. Convenience for the consultant —
     * they can always override.
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.inheritLocationFromAccount = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var mode = detectMode(formContext);
        if (mode !== MODE_B2B) return;

        var parentAttr = formContext.getAttribute("parentcustomerid");
        if (!parentAttr || !parentAttr.getValue()) return;

        var parentValue = parentAttr.getValue()[0];
        if (parentValue.entityType !== "account") return;

        var countryAttr = formContext.getAttribute("tavu_addresscountry");
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
            "?$select=_tavu_addresscountry_value,_tavu_addressstateprovince_value,_tavu_addresscity_value"
        ).then(
            function success(result) {
                formContext.ui.clearFormNotification(NOTIF_LOCATION_INHERIT);

                applyLookupFromODataResult(formContext, "tavu_addresscountry",
                    result, "_tavu_addresscountry_value", "tavu_country");
                applyLookupFromODataResult(formContext, "tavu_addressstateprovince",
                    result, "_tavu_addressstateprovince_value", "tavu_stateprovince");
                applyLookupFromODataResult(formContext, "tavu_addresscity",
                    result, "_tavu_addresscity_value", "tavu_city");
            },
            function error(err) {
                formContext.ui.clearFormNotification(NOTIF_LOCATION_INHERIT);
                console.error("[OpenTavu.Contact.MainForm.inheritLocationFromAccount] " + err.message);
            }
        );
    };

    /**
     * Sets requirement levels for location fields per sales-model.md §5.1.
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.setLocationFieldRequirements = function (executionContext) {
        var formContext = executionContext.getFormContext();
        setRequired(formContext, "tavu_addresscountry", "required");
        setRequired(formContext, "tavu_addressstateprovince", "none");
        setRequired(formContext, "tavu_addresscity", "none");
    };

    // ============================================================
    // Future hooks — reserved for Module 1 and Module 3
    // ============================================================

    /**
     * Reserved for Module 3 (Activity Capture). Will refresh engagement
     * timestamps when Module 3 pushes new email/meeting signals.
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.refreshEngagementMetrics = function (executionContext) {
        // TODO: implement when Module 3 is live.
    };

    /**
     * Reserved for richer engagement evaluation (e.g. sentiment-weighted)
     * once Module 1 provides sentiment signals.
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.evaluateEngagementStatus = function (executionContext) {
        // TODO: extend when Module 1 sentiment signals are available.
    };

    // ============================================================
    // Internal helpers — NOT exposed on the MainForm namespace
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
            console.warn("[OpenTavu.Contact.MainForm] Could not filter " + contextLabel + " subgrid: " + e.message);
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

})(OpenTavu.Contact.MainForm);