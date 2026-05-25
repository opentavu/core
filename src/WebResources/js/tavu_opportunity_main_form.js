"use strict";

/**
 * OpenTavu — Opportunity Main Form Script
 *
 * Form: Opportunity > Main
 * Purpose: Adaptive form logic that switches the visibility of the Close
 *          Information section, locks down the entire form when the
 *          opportunity is closed, and surfaces a header notification
 *          describing the close state.
 *
 *          - OPEN  (statecode = 0)  → Close Information section HIDDEN,
 *                                     all editable fields editable.
 *          - WON   (statecode = 1)  → Close Information section VISIBLE,
 *                                     Lost Reason hidden inside the section,
 *                                     all other deal fields locked read-only,
 *                                     header notification displayed.
 *          - LOST  (statecode = 2)  → Close Information section VISIBLE,
 *                                     Actual Revenue hidden inside the section,
 *                                     all other deal fields locked read-only,
 *                                     header notification displayed.
 *
 * Rationale (see sales-model.md §6, §7.3, §7.4):
 *   - Close Information fields (tavu_actualrevenue, tavu_actualclosedate,
 *     tavu_lostreason, tavu_closenotes) live in tavu_opportunity as
 *     "mirror fields" of the tavu_opportunityclose Activity.
 *   - They are populated by a Plugin/Power Automate that syncs from the
 *     Activity record created by the "Close Won" / "Close Lost" Ribbon buttons.
 *   - For Open opportunities, these fields are conceptually empty and
 *     should not appear on the form at all.
 *   - Inside a closed opportunity, only the fields relevant to the close
 *     type are shown: Won → revenue (no reason), Lost → reason (no revenue).
 *   - Once an opportunity is closed, deal fields are locked to preserve
 *     historical accuracy. Subgrids (Proposals, Timeline) remain interactive
 *     so that change orders, audit notes, and follow-up communications can
 *     still be linked to the closed opportunity.
 *
 * Read-only fields enforced by this script (these are populated by the close
 * automation, not by users — read-only at ALL times regardless of state):
 *   - tavu_actualrevenue    ← Plugin/Flow on Close Won
 *   - tavu_actualclosedate  ← Plugin/Flow on Close Won/Lost
 *   - tavu_lostreason       ← Plugin/Flow on Close Lost
 *   - tavu_closenotes       ← Plugin/Flow on Close Won/Lost
 *
 * Form registration:
 *   OnLoad → OpenTavu.Opportunity.MainForm.onLoad   (pass execution context: yes)
 *   OnSave → OpenTavu.Opportunity.MainForm.onSave   (pass execution context: yes)
 *
 *   statecode OnChange → OpenTavu.Opportunity.MainForm.onStateCodeChange
 *
 * Header fields (configured in form designer, not by this script):
 *   tavu_daysinstage, tavu_daysinpipeline, tavu_lastactivitydate
 *
 * @author  OpenTavu — Gustavo González Villani
 * @license MIT
 * @version 0.2.0
 */

var OpenTavu = OpenTavu || {};
OpenTavu.Opportunity = OpenTavu.Opportunity || {};
OpenTavu.Opportunity.MainForm = OpenTavu.Opportunity.MainForm || {};

(function (MainForm) {

    // ============================================================
    // Constants
    // ============================================================

    // statecode values (standard Dataverse opportunity state model)
    var STATE_OPEN = 0;
    var STATE_WON = 1;
    var STATE_LOST = 2;

    // Tab and section names (must match the names defined in the form designer)
    var TAB_GENERAL = "tab_general";
    var SECTION_CLOSE_INFORMATION = "SectionCloseInformation";

    // Fields managed by the close automation — always read-only on the form,
    // regardless of statecode.
    var CLOSE_MANAGED_FIELDS = [
        "tavu_actualrevenue",
        "tavu_actualclosedate",
        "tavu_lostreason",
        "tavu_closenotes"
    ];

    // Controls that MUST remain interactive even when the opportunity is closed.
    // Subgrid and section names that should NOT be disabled by the lockdown.
    // Reason: a closed opportunity may still receive change orders (Proposals
    // subgrid) and audit notes / follow-up activities (Timeline).
    var INTERACTIVE_WHEN_CLOSED = [
        "SubgridProposal",   // tavu_proposal subgrid
        "Timeline"     // OOTB timeline / notesControl
    ];

    // Form notification IDs
    var NOTIF_CLOSED_STATE = "opportunity_closed_state_banner";

    // ============================================================
    // Event handlers
    // ============================================================

    /**
     * Form OnLoad — orchestrates close-section visibility, read-only enforcement,
     * lockdown when closed, and header notification.
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.onLoad = function (executionContext) {
        MainForm.applyCloseSectionVisibility(executionContext);
        MainForm.enforceReadOnlyFields(executionContext);
        MainForm.applyClosedLockdown(executionContext);
        MainForm.applyClosedStateNotification(executionContext);
    };

    /**
     * Form OnSave handler. Reserved for future validations.
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.onSave = function (executionContext) {
        // Reserved for future save-time validations.
    };

    /**
     * OnChange for statecode — re-evaluates close section visibility, lockdown,
     * and the header notification when the state transitions (Open → Won/Lost,
     * or Won/Lost → Open via reopen).
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.onStateCodeChange = function (executionContext) {
        MainForm.applyCloseSectionVisibility(executionContext);
        MainForm.applyClosedLockdown(executionContext);
        MainForm.applyClosedStateNotification(executionContext);
    };

    // ============================================================
    // Core logic — exposed via MainForm namespace
    // ============================================================

    /**
     * Shows or hides the Close Information section based on the opportunity
     * statecode, and within the section hides the field that does not apply
     * to the current close type:
     *   - Open  → section hidden
     *   - Won   → section visible, Lost Reason hidden
     *   - Lost  → section visible, Actual Revenue hidden
     *
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.applyCloseSectionVisibility = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var stateCode = getStateCode(formContext);

        if (stateCode === STATE_OPEN || stateCode === null) {
            // Opportunity is open (or state not yet available) — hide section entirely.
            setSectionVisible(formContext, TAB_GENERAL, SECTION_CLOSE_INFORMATION, false);
            return;
        }

        // Closed (Won or Lost) — show the section and toggle inner fields.
        setSectionVisible(formContext, TAB_GENERAL, SECTION_CLOSE_INFORMATION, true);

        if (stateCode === STATE_WON) {
            setControlVisible(formContext, "tavu_actualrevenue", true);
            setControlVisible(formContext, "tavu_lostreason", false);
        } else if (stateCode === STATE_LOST) {
            setControlVisible(formContext, "tavu_actualrevenue", false);
            setControlVisible(formContext, "tavu_lostreason", true);
        }

        // tavu_actualclosedate and tavu_closenotes apply to both Won and Lost
        // — they remain visible whenever the section is visible.
        setControlVisible(formContext, "tavu_actualclosedate", true);
        setControlVisible(formContext, "tavu_closenotes", true);
    };

    /**
     * Enforces read-only policy on close-managed fields. These are populated
     * by the close automation (Plugin/Flow from tavu_opportunityclose Activity),
     * not by users — read-only at ALL times regardless of statecode.
     *
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.enforceReadOnlyFields = function (executionContext) {
        var formContext = executionContext.getFormContext();
        CLOSE_MANAGED_FIELDS.forEach(function (fieldName) {
            setControlDisabled(formContext, fieldName, true);
        });
    };

    /**
     * Locks down all controls on the form when the opportunity is closed
     * (Won or Lost), preserving the historical record. Excludes the controls
     * listed in INTERACTIVE_WHEN_CLOSED (Proposals subgrid, Timeline), which
     * remain editable so that change orders and audit notes can still be
     * captured after close.
     *
     * If the opportunity is reopened (statecode returns to Open), the lockdown
     * is released and all standard controls become editable again — except
     * those in CLOSE_MANAGED_FIELDS, which remain read-only permanently.
     *
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.applyClosedLockdown = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var stateCode = getStateCode(formContext);
        var shouldLock = (stateCode === STATE_WON || stateCode === STATE_LOST);

        formContext.ui.controls.forEach(function (ctrl) {
            if (!ctrl || !ctrl.setDisabled) return;

            var ctrlName = ctrl.getName ? ctrl.getName() : null;
            if (ctrlName && INTERACTIVE_WHEN_CLOSED.indexOf(ctrlName) >= 0) {
                // Always keep interactive — never lock these.
                ctrl.setDisabled(false);
                return;
            }

            // Close-managed fields are handled by enforceReadOnlyFields and
            // must remain disabled regardless of lockdown state.
            if (ctrlName && isCloseManagedField(ctrlName)) {
                ctrl.setDisabled(true);
                return;
            }

            ctrl.setDisabled(shouldLock);
        });
    };

    /**
     * Displays a header notification when the opportunity is closed, clearly
     * communicating its state and the actual close date. Cleared when the
     * opportunity is open (or reopened).
     *
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.applyClosedStateNotification = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var stateCode = getStateCode(formContext);

        // Always clear first — guarantees a clean state on reopen and on
        // state transitions within the same session.
        formContext.ui.clearFormNotification(NOTIF_CLOSED_STATE);

        if (stateCode !== STATE_WON && stateCode !== STATE_LOST) return;

        var label = (stateCode === STATE_WON) ? "Won" : "Lost";
        var closeDate = getFormattedAttributeValue(formContext, "tavu_actualclosedate");
        var dateClause = closeDate ? (" on " + closeDate) : "";
        var message = "This opportunity is closed (" + label + dateClause +
            "). Fields are read-only to preserve the historical record. " +
            "To capture new work, create a new opportunity for this customer.";

        formContext.ui.setFormNotification(message, "INFO", NOTIF_CLOSED_STATE);
    };

    // ============================================================
    // Future hooks — reserved for upcoming AI modules
    // ============================================================

    /**
     * Reserved for the future AI Proposal Generator. Will surface suggestions
     * or warnings on the opportunity form once Module 4 (AI Proposal Generator)
     * is live.
     * @param {Xrm.ExecutionContext} executionContext
     */
    MainForm.refreshProposalSignals = function (executionContext) {
        // TODO: implement when AI Proposal Generator is live.
    };

    // ============================================================
    // Internal helpers — NOT exposed on the MainForm namespace
    // ============================================================

    /**
     * Reads the current statecode value from the form.
     * @param {object} formContext
     * @returns {number|null} statecode value or null if attribute not available
     */
    function getStateCode(formContext) {
        var stateAttr = formContext.getAttribute("statecode");
        if (!stateAttr) return null;
        var value = stateAttr.getValue();
        return (value === null || value === undefined) ? null : value;
    }

    /**
     * Returns true if the given control/field name is in CLOSE_MANAGED_FIELDS.
     */
    function isCloseManagedField(name) {
        return CLOSE_MANAGED_FIELDS.indexOf(name) >= 0;
    }

    /**
     * Reads an attribute's value and returns it as a locale-formatted string,
     * suitable for embedding in form notifications. Returns null if the
     * attribute is missing or empty.
     */
    function getFormattedAttributeValue(formContext, schemaName) {
        var attr = formContext.getAttribute(schemaName);
        if (!attr) return null;
        var value = attr.getValue();
        if (value === null || value === undefined) return null;

        if (value instanceof Date) {
            try {
                var formatter = new Intl.DateTimeFormat(undefined, {
                    year: "numeric", month: "short", day: "2-digit"
                });
                return formatter.format(value);
            } catch (e) {
                return value.toDateString();
            }
        }
        return String(value);
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
     * Safe control visibility setter. Iterates over all controls of a given field
     * (a field can have multiple controls on the same form: header + body).
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

})(OpenTavu.Opportunity.MainForm);
