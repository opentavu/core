"use strict";

/**
 * OpenTavu — Opportunity Main Form
 *
 * Form behavior for tavu_opportunity. Two concerns:
 *   1. Customer field: filter by tenant Customer Mode, mirror Customer to
 *      typed Account/Contact lookups.
 *   2. Close lifecycle: show/hide Close Information section, lock form when
 *      closed, surface header notification.
 *
 * Events to register (form designer):
 *   OnLoad of form        → OpenTavu.Opportunity.MainForm.onLoad
 *   OnSave of form        → OpenTavu.Opportunity.MainForm.onSave
 *   OnChange statecode    → OpenTavu.Opportunity.MainForm.onStateCodeChange
 *   OnChange tavu_customerid → OpenTavu.Opportunity.MainForm.onCustomerChange
 *   (All handlers must pass execution context as first parameter.)
 *
 * Design references: sales-model.md §6, §7.3, §7.4
 *
 * @author  OpenTavu — Gustavo González Villani
 * @license MIT
 * @version 0.3.0
 */

var OpenTavu = OpenTavu || {};
OpenTavu.Opportunity = OpenTavu.Opportunity || {};
OpenTavu.Opportunity.MainForm = OpenTavu.Opportunity.MainForm || {};

(function (MainForm) {

    // ============================================================
    // Constants
    // ============================================================

    var STATE_OPEN = 0;
    var STATE_WON = 1;
    var STATE_LOST = 2;

    var TAB_GENERAL = "tab_general";
    var SECTION_CLOSE_INFORMATION = "SectionCloseInformation";

    // Fields populated by close automation (Plugin/Flow from tavu_opportunityclose).
    // Read-only at all times — users never edit these directly.
    var CLOSE_MANAGED_FIELDS = [
        "tavu_actualrevenue",
        "tavu_actualclosedate",
        "tavu_lostreason",
        "tavu_closenotes"
    ];

    // Controls that stay interactive even after close (change orders, audit trail).
    var INTERACTIVE_WHEN_CLOSED = [
        "SubgridProposal",
        "Timeline"
    ];

    var CUSTOMER_MODE = {
        B2B_ONLY: 576600000,
        B2C_ONLY: 576600001,
        MIXED:    576600002
    };

    var SESSION_CACHE_KEY = "opentavu.customerMode";

    var NOTIF = {
        CLOSED_STATE: "opportunity_closed_state_banner",
        INVALID_CUSTOMER: "opentavu_invalid_customer_type",
        MODE_FETCH_FAILED: "opentavu_mode_fetch_failed"
    };

    // ============================================================
    // Event handlers
    // ============================================================

    /** @param {Xrm.ExecutionContext} executionContext */
    MainForm.onLoad = function (executionContext) {
        MainForm.applyCloseSectionVisibility(executionContext);
        MainForm.enforceReadOnlyFields(executionContext);
        MainForm.applyClosedLockdown(executionContext);
        MainForm.applyClosedStateNotification(executionContext);
        MainForm.applyCustomerModeFilter(executionContext);
    };

    /** Reserved for future save-time validations. */
    MainForm.onSave = function (executionContext) { };

    /** @param {Xrm.ExecutionContext} executionContext */
    MainForm.onStateCodeChange = function (executionContext) {
        MainForm.applyCloseSectionVisibility(executionContext);
        MainForm.applyClosedLockdown(executionContext);
        MainForm.applyClosedStateNotification(executionContext);
    };

    /** @param {Xrm.ExecutionContext} executionContext */
    MainForm.onCustomerChange = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var customerValue = formContext.getAttribute("tavu_customerid").getValue();

        // Customer cleared → clear typed mirrors, clear any prior warning.
        if (!customerValue || customerValue.length === 0) {
            clearCustomerMirrors(formContext);
            formContext.ui.clearFormNotification(NOTIF.INVALID_CUSTOMER);
            return;
        }

        getCustomerMode().then(
            function (mode) {
                if (!isCustomerTypeAllowed(customerValue[0].entityType, mode)) {
                    rejectCustomerSelection(formContext, customerValue[0].entityType, mode);
                    return;
                }
                formContext.ui.clearFormNotification(NOTIF.INVALID_CUSTOMER);
                mirrorCustomerToTypedLookups(formContext, customerValue[0]);
            },
            function () {
                // Mode unavailable → permissive fallback: mirror without validation.
                mirrorCustomerToTypedLookups(formContext, customerValue[0]);
            }
        );
    };

    // ============================================================
    // Public namespace methods (registered or callable from form)
    // ============================================================

    /**
     * Open  → section hidden.
     * Won   → section visible, Lost Reason hidden inside.
     * Lost  → section visible, Actual Revenue hidden inside.
     */
    MainForm.applyCloseSectionVisibility = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var stateCode = getStateCode(formContext);

        if (stateCode === STATE_OPEN || stateCode === null) {
            setSectionVisible(formContext, TAB_GENERAL, SECTION_CLOSE_INFORMATION, false);
            return;
        }

        setSectionVisible(formContext, TAB_GENERAL, SECTION_CLOSE_INFORMATION, true);
        setControlVisible(formContext, "tavu_actualrevenue", stateCode === STATE_WON);
        setControlVisible(formContext, "tavu_lostreason",    stateCode === STATE_LOST);
        setControlVisible(formContext, "tavu_actualclosedate", true);
        setControlVisible(formContext, "tavu_closenotes", true);
    };

    MainForm.enforceReadOnlyFields = function (executionContext) {
        var formContext = executionContext.getFormContext();
        CLOSE_MANAGED_FIELDS.forEach(function (fieldName) {
            setControlDisabled(formContext, fieldName, true);
        });
    };

    /**
     * Locks all controls when closed, except subgrid/timeline (audit trail)
     * and CLOSE_MANAGED_FIELDS (which stay locked even when reopened).
     */
    MainForm.applyClosedLockdown = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var stateCode = getStateCode(formContext);
        var shouldLock = (stateCode === STATE_WON || stateCode === STATE_LOST);

        formContext.ui.controls.forEach(function (ctrl) {
            if (!ctrl || !ctrl.setDisabled) return;
            var ctrlName = ctrl.getName ? ctrl.getName() : null;

            if (ctrlName && INTERACTIVE_WHEN_CLOSED.indexOf(ctrlName) >= 0) {
                ctrl.setDisabled(false);
                return;
            }
            if (ctrlName && CLOSE_MANAGED_FIELDS.indexOf(ctrlName) >= 0) {
                ctrl.setDisabled(true);
                return;
            }
            ctrl.setDisabled(shouldLock);
        });
    };

    MainForm.applyClosedStateNotification = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var stateCode = getStateCode(formContext);

        formContext.ui.clearFormNotification(NOTIF.CLOSED_STATE);
        if (stateCode !== STATE_WON && stateCode !== STATE_LOST) return;

        var label = (stateCode === STATE_WON) ? "Won" : "Lost";
        var closeDate = getFormattedAttributeValue(formContext, "tavu_actualclosedate");
        var dateClause = closeDate ? (" on " + closeDate) : "";
        var message = "This opportunity is closed (" + label + dateClause +
            "). Fields are read-only to preserve the historical record. " +
            "To capture new work, create a new opportunity for this customer.";

        formContext.ui.setFormNotification(message, "INFO", NOTIF.CLOSED_STATE);
    };

    /**
     * Resolves customer mode and applies PreSearch filter to the Customer
     * lookup. Non-blocking: form remains usable while mode resolves.
     */
    MainForm.applyCustomerModeFilter = function (executionContext) {
        var formContext = executionContext.getFormContext();

        getCustomerMode().then(
            function (mode) { applyPreSearchFilter(formContext, mode); },
            function (error) {
                formContext.ui.setFormNotification(
                    "Customer Mode could not be loaded. Customer filtering is disabled. Refresh to retry.",
                    "WARNING",
                    NOTIF.MODE_FETCH_FAILED
                );
                console.warn("[OpenTavu.Opportunity.MainForm] Customer Mode fetch failed:", error);
            }
        );
    };

    // ============================================================
    // Reserved hooks for future modules
    // ============================================================

    /** Reserved for Module 4 — AI Proposal Generator. */
    MainForm.refreshProposalSignals = function (executionContext) { };

    // ============================================================
    // Internal helpers
    // ============================================================

    function getStateCode(formContext) {
        var attr = formContext.getAttribute("statecode");
        if (!attr) return null;
        var value = attr.getValue();
        return (value === null || value === undefined) ? null : value;
    }

    function getFormattedAttributeValue(formContext, schemaName) {
        var attr = formContext.getAttribute(schemaName);
        if (!attr) return null;
        var value = attr.getValue();
        if (value === null || value === undefined) return null;

        if (value instanceof Date) {
            try {
                return new Intl.DateTimeFormat(undefined, {
                    year: "numeric", month: "short", day: "2-digit"
                }).format(value);
            } catch (e) {
                return value.toDateString();
            }
        }
        return String(value);
    }

    function setSectionVisible(formContext, tabName, sectionName, visible) {
        var tab = formContext.ui.tabs.get(tabName);
        if (!tab) return;
        var section = tab.sections.get(sectionName);
        if (section) section.setVisible(visible);
    }

    function setControlVisible(formContext, schemaName, visible) {
        var attr = formContext.getAttribute(schemaName);
        if (!attr) return;
        (attr.controls.get() || []).forEach(function (ctrl) {
            if (ctrl && ctrl.setVisible) ctrl.setVisible(visible);
        });
    }

    function setControlDisabled(formContext, schemaName, disabled) {
        var attr = formContext.getAttribute(schemaName);
        if (!attr) return;
        (attr.controls.get() || []).forEach(function (ctrl) {
            if (ctrl && ctrl.setDisabled) ctrl.setDisabled(disabled);
        });
    }

    // ----- Customer Mode resolution (sessionStorage + WebApi fallback) -----

    /** @returns {Promise<number>} one of CUSTOMER_MODE.* */
    function getCustomerMode() {
        return new Promise(function (resolve, reject) {
            var cached = readModeFromSessionCache();
            if (cached !== null) { resolve(cached); return; }

            fetchCustomerModeFromDataverse().then(
                function (mode) { cacheModeInSessionStorage(mode); resolve(mode); },
                function (error) { reject(error); }
            );
        });
    }

    function readModeFromSessionCache() {
        try {
            var raw = sessionStorage.getItem(SESSION_CACHE_KEY);
            if (raw === null) return null;
            var parsed = parseInt(raw, 10);
            if (isNaN(parsed)) return null;
            if (parsed === CUSTOMER_MODE.B2B_ONLY ||
                parsed === CUSTOMER_MODE.B2C_ONLY ||
                parsed === CUSTOMER_MODE.MIXED) {
                return parsed;
            }
            return null;
        } catch (e) {
            return null;
        }
    }

    function cacheModeInSessionStorage(mode) {
        try { sessionStorage.setItem(SESSION_CACHE_KEY, String(mode)); }
        catch (e) { /* sessionStorage disabled — non-fatal */ }
    }

    /** Reads the singleton row of tavu_systemsettings. */
    function fetchCustomerModeFromDataverse() {
        return Xrm.WebApi.retrieveMultipleRecords(
            "tavu_systemsettings",
            "?$select=tavu_customermode&$top=1"
        ).then(function (result) {
            if (!result.entities || result.entities.length === 0) {
                console.warn("[OpenTavu.Opportunity.MainForm] No tavu_systemsettings row. Defaulting to Mixed.");
                return CUSTOMER_MODE.MIXED;
            }
            var mode = result.entities[0].tavu_customermode;
            return (mode === null || mode === undefined) ? CUSTOMER_MODE.MIXED : mode;
        });
    }

    // ----- Customer lookup filter and mirror -----

    /**
     * In non-Mixed modes, attaches a PreSearch that filters out the blocked
     * entity type. Uses `<condition attribute='X' operator='null'/>` against
     * the primary key — a condition that can never match — as the standard
     * community workaround for the lack of an OOTB API to remove a lookup tab.
     */
    function applyPreSearchFilter(formContext, mode) {
        var control = formContext.getControl("tavu_customerid");
        if (!control || mode === CUSTOMER_MODE.MIXED) return;

        control.addPreSearch(function () {
            if (mode === CUSTOMER_MODE.B2B_ONLY) {
                control.addCustomFilter(
                    "<filter type='and'><condition attribute='contactid' operator='null' /></filter>",
                    "contact"
                );
            } else if (mode === CUSTOMER_MODE.B2C_ONLY) {
                control.addCustomFilter(
                    "<filter type='and'><condition attribute='accountid' operator='null' /></filter>",
                    "account"
                );
            }
        });
    }

    function isCustomerTypeAllowed(entityType, mode) {
        if (mode === CUSTOMER_MODE.MIXED) return true;
        if (mode === CUSTOMER_MODE.B2B_ONLY) return entityType === "account";
        if (mode === CUSTOMER_MODE.B2C_ONLY) return entityType === "contact";
        return true;
    }

    function rejectCustomerSelection(formContext, attemptedEntityType, mode) {
        formContext.getAttribute("tavu_customerid").setValue(null);
        clearCustomerMirrors(formContext);

        var typeLabel = attemptedEntityType === "account" ? "an Account" : "a Contact";
        var modeLabel = mode === CUSTOMER_MODE.B2B_ONLY ? "B2B Only" : "B2C Only";
        var allowedLabel = mode === CUSTOMER_MODE.B2B_ONLY ? "Accounts" : "Contacts";
        var message = "This system is configured in " + modeLabel + " mode and only allows " +
            allowedLabel + " as Customers. The " + typeLabel + " you selected was not saved.";

        formContext.ui.setFormNotification(message, "WARNING", NOTIF.INVALID_CUSTOMER);
    }

    /**
     * Sets the typed lookup (tavu_accountid or tavu_contactid) matching the
     * customer's entity type and clears the opposite.
     */
    function mirrorCustomerToTypedLookups(formContext, customerValue) {
        if (!customerValue) return;
        var mirrored = [{
            id: customerValue.id,
            name: customerValue.name,
            entityType: customerValue.entityType
        }];

        if (customerValue.entityType === "account") {
            setLookupValue(formContext, "tavu_accountid", mirrored);
            setLookupValue(formContext, "tavu_contactid", null);
        } else if (customerValue.entityType === "contact") {
            setLookupValue(formContext, "tavu_contactid", mirrored);
            setLookupValue(formContext, "tavu_accountid", null);
        }
    }

    function clearCustomerMirrors(formContext) {
        setLookupValue(formContext, "tavu_accountid", null);
        setLookupValue(formContext, "tavu_contactid", null);
    }

    function setLookupValue(formContext, attributeName, value) {
        var attr = formContext.getAttribute(attributeName);
        if (attr) attr.setValue(value);
    }

})(OpenTavu.Opportunity.MainForm);