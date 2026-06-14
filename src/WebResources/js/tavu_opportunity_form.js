"use strict";

/**
 * OpenTavu — Opportunity Main Form (tavu_opportunity)
 *
 * Customer field: filter by tenant Customer Mode + mirror to typed Account/Contact.
 * Lifecycle (Option A): Sales Stage drives while Open; Won/Lost takes over when
 * Closed. Rationale & schema: sales-model.md §6, §6.3bis, §7.3–§7.4.
 *
 * Form event registration (designer → handler; pass execution context):
 *   OnLoad                   → OpenTavu.Opportunity.MainForm.onLoad
 *   OnSave                   → OpenTavu.Opportunity.MainForm.onSave
 *   OnChange statecode       → OpenTavu.Opportunity.MainForm.onStateCodeChange
 *   OnChange statuscode      → OpenTavu.Opportunity.MainForm.onStatusReasonChange
 *   OnChange tavu_customerid → OpenTavu.Opportunity.MainForm.onCustomerChange
 *
 * Command bar registration (Main form → Run JavaScript, param PrimaryControl):
 *   "Reset Probability"      → OpenTavu.Opportunity.MainForm.resetProbability
 *
 * @author OpenTavu — Gustavo González Villani
 * SPDX-License-Identifier: MIT
 */

var OpenTavu = OpenTavu || {};
OpenTavu.Opportunity = OpenTavu.Opportunity || {};
OpenTavu.Opportunity.MainForm = OpenTavu.Opportunity.MainForm || {};

(function (MainForm) {

    // ============================================================
    // Constants
    // ============================================================

    // statecode — on a CUSTOM table this is ALWAYS Active/Inactive only.
    // Won/Lost is NOT here; it lives in statuscode (see below).
    var STATE_ACTIVE = 0;
    var STATE_INACTIVE = 1;

    // statuscode (Status Reason).
    // "Open" lives under the Active state; "Won" and "Lost" live under Inactive.
    var STATUS_OPEN = 576600001;
    var STATUS_WON  = 576600005;   
    var STATUS_LOST = 576600006;

    var TAB_GENERAL = "tab_general";
    var SECTION_CLOSE_INFORMATION = "SectionCloseInformation";

    var FIELD_STATUS_REASON = "statuscode";
    var FIELD_SALES_STAGE   = "tavu_salesstage";

    // Probability defaulting (mirrors LifecycleTracker plugin contract).
    var FIELD_PROBABILITY           = "tavu_probability";
    var FIELD_PROBABILITY_IS_MANUAL = "tavu_probabilityismanual";

    // Sales Stage config row — the single source of the default probability.
    var STAGE_ENTITY                   = "tavu_salesstage";
    var STAGE_ATTR_DEFAULT_PROBABILITY = "tavu_defaultprobability";

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
        MODE_FETCH_FAILED: "opentavu_mode_fetch_failed",
        PROBABILITY_RESET: "opentavu_probability_reset"
    };

    var NOTIF_TRANSIENT_MS = 4000;

    // ============================================================
    // Event handlers
    // ============================================================

    /** @param {Xrm.ExecutionContext} executionContext */
    MainForm.onLoad = function (executionContext) {
        refreshLifecycleUi(executionContext);
        MainForm.enforceReadOnlyFields(executionContext);
        MainForm.applyCustomerModeFilter(executionContext);
    };

    /** Reserved for future save-time validations. */
    MainForm.onSave = function (executionContext) { };

    /** @param {Xrm.ExecutionContext} executionContext */
    MainForm.onStateCodeChange = function (executionContext) {
        refreshLifecycleUi(executionContext);
    };

    /** @param {Xrm.ExecutionContext} executionContext */
    MainForm.onStatusReasonChange = function (executionContext) {
        refreshLifecycleUi(executionContext);
    };

    /** @param {Xrm.ExecutionContext} executionContext */
    MainForm.onCustomerChange = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var customerValue = formContext.getAttribute("tavu_customerid").getValue();

        if (!customerValue || customerValue.length === 0) {
            formContext.ui.clearFormNotification(NOTIF.INVALID_CUSTOMER);
            return;
        }

        getCustomerMode().then(
            function (mode) {
                if (!isCustomerTypeAllowed(customerValue[0].entityType, mode)) {
                    // Immediate feedback. The plugin will also reject it, but we
                    // surface the message up front so the user can fix it without waiting for save.
                     rejectCustomerSelection(formContext, customerValue[0].entityType, mode);
                    return;
                }
                formContext.ui.clearFormNotification(NOTIF.INVALID_CUSTOMER);
                // NOTE: the Account/Contact mirror is handled server-side by the
                // Pl.Opportunity.CustomerSync plugin, so it applies across every entry
                // path (UI, import, Power Automate, API). Here we only do visual validation.
            },
            function () {
                // Mode unavailable → permissive fallback. The server-side plugin would do the same.            
                }
        );
    };

    // ============================================================
    // Public namespace methods (registered or callable from form)
    // ============================================================

    /**
     * Option A lifecycle emphasis — the core of the no-friction reading.
     *
     *  Open    → Sales Stage is the protagonist (visible + business required).
     *            Status Reason hidden: while Open it only reads "Open", so it
     *            adds no information and competes with Sales Stage for attention.
     *  Closed  → Sales Stage stays visible as historical reference but becomes
     *            read-only (handled by applyClosedLockdown) and its requirement
     *            is relaxed so legacy/migrated records can be re-saved.
     *            Status Reason shown read-only — now meaningful (Won/Lost).
     */
    MainForm.applyOpenClosedEmphasis = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var closed = isClosed(formContext);

        // Status Reason: only meaningful once the opportunity is closed.
        setControlVisible(formContext, FIELD_STATUS_REASON, closed);

        // Sales Stage: always visible; required only while Open.
        setControlVisible(formContext, FIELD_SALES_STAGE, true);
        setFieldRequired(formContext, FIELD_SALES_STAGE, !closed);
    };

    /**
     * Open  → section hidden.
     * Won   → section visible, Lost Reason hidden inside.
     * Lost  → section visible, Actual Revenue hidden inside.
     */
    MainForm.applyCloseSectionVisibility = function (executionContext) {
        var formContext = executionContext.getFormContext();

        if (!isClosed(formContext)) {
            setSectionVisible(formContext, TAB_GENERAL, SECTION_CLOSE_INFORMATION, false);
            return;
        }

        var status = getStatusReason(formContext);
        setSectionVisible(formContext, TAB_GENERAL, SECTION_CLOSE_INFORMATION, true);
        setControlVisible(formContext, "tavu_actualrevenue",   status === STATUS_WON);
        setControlVisible(formContext, "tavu_lostreason",      status === STATUS_LOST);
        setControlVisible(formContext, "tavu_actualclosedate", true);
        setControlVisible(formContext, "tavu_closenotes",      true);
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
        var shouldLock = isClosed(formContext);

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

        formContext.ui.clearFormNotification(NOTIF.CLOSED_STATE);
        if (!isClosed(formContext)) return;

        var status = getStatusReason(formContext);
        var label = (status === STATUS_WON) ? "Won"
                  : (status === STATUS_LOST) ? "Lost"
                  : "Closed";
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

    /**
     * Ribbon command — "Reset Probability".
     *
     * Returns the opportunity to AUTO probability mode: clears the manual
     * override flag and re-applies the current Sales Stage's configured
     * default (tavu_salesstage.tavu_defaultprobability).
     *
     * Why client-side: per the LifecycleTracker contract (sales-model.md
     * §6.3bis), the form path sends probability AND the manual flag explicitly;
     * the server plugin is only the safety net for non-form paths. We read the
     * default from the SAME config row the plugin reads, so the stage row stays
     * the single source of truth — no duplicated stage→probability mapping here.
     *
     * AI-first path: today "reset" = stage default. Under AI-Assisted
     * Forecasting this same command recalculates from the per-firm conversion
     * model — the button and its contract (clear manual flag, recompute) survive.
     *
     * Registered on the Main form command bar via Run JavaScript, passing
     * PrimaryControl. Auto-saves on success so the reset applies immediately;
     * the save commits the WHOLE form (any other pending edits go with it).
     *
     * REQUIRES tavu_probabilityismanual to be present on the form (hidden is
     * fine). Without it the flag cannot be cleared, and the plugin's
     * "explicit probability + no flag = manual" rule would freeze the value.
     *
     * @param {Xrm.FormContext|Xrm.ExecutionContext} primaryControl
     */
    MainForm.resetProbability = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        // A closed opportunity is historical/read-only — never touch it.
        if (isClosed(formContext)) {
            notifyTransient(formContext,
                "This opportunity is closed. Probability cannot be reset.",
                "INFO");
            return;
        }

        // The manual-override flag MUST be writable from the form. If it is not on
        // the form, getAttribute returns null and we cannot clear it — and saving a
        // probability WITHOUT the flag makes the plugin (LifecycleTracker, the
        // "explicit probability + no flag = manual" rule) stamp manual = true,
        // freezing the value. Abort loudly here BEFORE touching probability rather
        // than silently poisoning the flag.
        var manualAttr = formContext.getAttribute(FIELD_PROBABILITY_IS_MANUAL);
        if (!manualAttr) {
            console.error(
                "[OpenTavu.Opportunity.MainForm] resetProbability: '" +
                FIELD_PROBABILITY_IS_MANUAL + "' is not on the form. Add it (hidden is fine) and publish.");
            notifyTransient(formContext,
                "Reset is unavailable: the override flag field is missing from this form. " +
                "Add it (it can be hidden) and publish, then try again.",
                "ERROR");
            return;
        }

        var stageAttr = formContext.getAttribute(FIELD_SALES_STAGE);
        var stageRef = stageAttr ? stageAttr.getValue() : null;
        if (!stageRef || stageRef.length === 0) {
            notifyTransient(formContext,
                "Select a Sales Stage first — the default probability comes from the stage.",
                "WARNING");
            return;
        }

        var stageId = stageRef[0].id.replace(/[{}]/g, "");

        Xrm.WebApi.retrieveRecord(
            STAGE_ENTITY, stageId, "?$select=" + STAGE_ATTR_DEFAULT_PROBABILITY
        ).then(
            function (stage) {
                var def = stage[STAGE_ATTR_DEFAULT_PROBABILITY];
                if (def === null || def === undefined) {
                    notifyTransient(formContext,
                        "This Sales Stage has no default probability configured.",
                        "WARNING");
                    return;
                }

                // Order matters: setValue does NOT fire OnChange, so writing the
                // probability will not re-flip the manual flag. We then clear the
                // flag explicitly to land in auto mode.
                setAttributeValue(formContext, FIELD_PROBABILITY, def);
                setAttributeValue(formContext, FIELD_PROBABILITY_IS_MANUAL, false);

                // Auto-save so the reset takes effect immediately. This commits
                // the whole form, including any other pending edits on it.
                formContext.data.save().then(
                    function () {
                        notifyTransient(formContext,
                            "Probability reset to the stage default (" + def + "%).",
                            "INFO");
                    },
                    function (saveError) {
                        console.error(
                            "[OpenTavu.Opportunity.MainForm] resetProbability save failed:", saveError);
                        notifyTransient(formContext,
                            "Probability was reset on the form but the save failed. Save manually to apply.",
                            "ERROR");
                    }
                );
            },
            function (error) {
                console.error(
                    "[OpenTavu.Opportunity.MainForm] resetProbability failed:", error);
                notifyTransient(formContext,
                    "Could not read the stage default probability. Try again.",
                    "ERROR");
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

    /**
     * Bundles every lifecycle-driven UI rule into one ordered pass.
     * Order matters: emphasis sets visibility/requirement first, then section
     * visibility, then the lockdown disables controls when closed, then the
     * header notification is refreshed.
     */
    function refreshLifecycleUi(executionContext) {
        MainForm.applyOpenClosedEmphasis(executionContext);
        MainForm.applyCloseSectionVisibility(executionContext);
        MainForm.applyClosedLockdown(executionContext);
        MainForm.applyClosedStateNotification(executionContext);
    }

    /**
     * Command-bar handlers receive PrimaryControl (a FormContext); form-event
     * handlers receive an ExecutionContext. Normalize to a FormContext so the
     * same helpers work from either entry point.
     */
    function resolveFormContext(arg) {
        if (!arg) return null;
        return (typeof arg.getFormContext === "function") ? arg.getFormContext() : arg;
    }

    function setAttributeValue(formContext, schemaName, value) {
        var attr = formContext.getAttribute(schemaName);
        if (attr && attr.setValue) attr.setValue(value);
    }

    /** Form notification that auto-clears, so it does not linger on the form. */
    function notifyTransient(formContext, message, level) {
        formContext.ui.setFormNotification(message, level, NOTIF.PROBABILITY_RESET);
        setTimeout(function () {
            formContext.ui.clearFormNotification(NOTIF.PROBABILITY_RESET);
        }, NOTIF_TRANSIENT_MS);
    }

    function getStateCode(formContext) {
        var attr = formContext.getAttribute("statecode");
        if (!attr) return null;
        var value = attr.getValue();
        return (value === null || value === undefined) ? null : value;
    }

    function getStatusReason(formContext) {
        var attr = formContext.getAttribute("statuscode");
        if (!attr) return null;
        var value = attr.getValue();
        return (value === null || value === undefined) ? null : value;
    }

    /** Open vs Closed is decided by statecode (Active/Inactive) on a custom table. */
    function isClosed(formContext) {
        return getStateCode(formContext) === STATE_INACTIVE;
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

    function setFieldRequired(formContext, schemaName, required) {
        var attr = formContext.getAttribute(schemaName);
        if (attr && attr.setRequiredLevel) {
            attr.setRequiredLevel(required ? "required" : "none");
        }
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

        var typeLabel = attemptedEntityType === "account" ? "an Account" : "a Contact";
        var modeLabel = mode === CUSTOMER_MODE.B2B_ONLY ? "B2B Only" : "B2C Only";
        var allowedLabel = mode === CUSTOMER_MODE.B2B_ONLY ? "Accounts" : "Contacts";
        var message = "This system is configured in " + modeLabel + " mode and only allows " +
            allowedLabel + " as Customers. The " + typeLabel + " you selected was not saved.";

        formContext.ui.setFormNotification(message, "WARNING", NOTIF.INVALID_CUSTOMER);
    }

})(OpenTavu.Opportunity.MainForm);