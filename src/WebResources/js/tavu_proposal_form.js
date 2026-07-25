"use strict";

/**
 * OpenTavu — Proposal Form (tavu_proposal)
 *
 * Ribbon commands:
 *   "Create New Version" → OpenTavu.Proposal.Form.createNewVersion
 *   "Mark as Approved"   → OpenTavu.Proposal.Form.markApproved   (show when Sent/Awaiting)
 *   "Mark as Lost"       → OpenTavu.Proposal.Form.markRejected   (show when Sent/Awaiting)
 *      Recommended visibility (app-scoped Power Fx): show when the proposal is
 *      locked or closed, i.e. NOT an editable draft —
 *        Self.Selected.Item.'Status Reason' <> 'Status Reason (Proposals)'.Draft
 *      (or simply always show; the server still enforces the rules).
 *
 * Form event registration (designer → handler; pass execution context):
 *   OnLoad              → OpenTavu.Proposal.Form.onLoad             (visual lock when Sent/closed)
 *   OnChange statuscode → OpenTavu.Proposal.Form.onStatusReasonChange
 *
 * Reserved for the next iteration: Approved→Won (copy total to opportunity + offer Close as Won).
 *
 * @author OpenTavu — Gustavo González Villani
 * SPDX-License-Identifier: MIT
 */

var OpenTavu = OpenTavu || {};
OpenTavu.Proposal = OpenTavu.Proposal || {};
OpenTavu.Proposal.Form = OpenTavu.Proposal.Form || {};

(function (Form) {

    var CLONE_API = "tavu_CloneProposalVersion";

    // Proposal statuscode values that mean "locked" (sent to client / awaiting).
    var STATUS_SENT_TO_CLIENT = 576600004;
    var STATUS_AWAITING_DECISION = 576600005;
    var STATUS_APPROVED = 576600006;
    var STATUS_REJECTED = 576600007;
    var STATE_INACTIVE = 1; // Approved / Rejected / Superseded / Withdrawn

    var FIELD_STATUS_REASON = "statuscode";
    var FIELD_PROPOSAL_TOTAL = "tavu_total";
    var FIELD_OPPORTUNITY = "tavu_opportunity";
    var OPP_ESTIMATED_REVENUE = "tavu_estimatedrevenue";

    // Opportunity close dialog (custom page) — must match CLOSE_DIALOG_PAGE in
    // tavu_opportunity_form.js.
    var CLOSE_DIALOG_PAGE = "tavu_opportunityclosedialog_31702";

    var NOTIF = { CLONE: "opentavu_proposal_clone", LOCKED: "opentavu_proposal_locked" };
    var NOTIF_TRANSIENT_MS = 4000;

    // ============================================================
    // Ribbon commands
    // ============================================================

    /**
     * Ribbon command — "Create New Version".
     *
     * Calls the tavu_CloneProposalVersion Custom API for the current proposal,
     * which server-side clones the header + active lines into a new Draft (with the
     * version incremented), marks the current proposal Superseded, and returns the
     * new proposal id. On success the new draft is opened so the consultant can edit
     * and re-send it.
     *
     * Requires a saved (non-dirty) record: the clone reads committed server data.
     *
     * Registered on the Main form command bar via Run JavaScript, passing PrimaryControl.
     *
     * @param {Xrm.FormContext|Xrm.ExecutionContext} primaryControl
     */
    Form.createNewVersion = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        var rawId = formContext.data.entity.getId();
        if (!rawId) {
            notifyTransient(formContext, "Save the proposal before creating a new version.", "WARNING");
            return;
        }
        if (formContext.data.getIsDirty()) {
            notifyTransient(formContext, "Save your pending changes before creating a new version.", "WARNING");
            return;
        }

        var proposalId = rawId.replace(/[{}]/g, "");

        Xrm.Navigation.openConfirmDialog({
            title: "Create New Version",
            text: "This creates a new draft copy of the proposal (with a new version number) " +
                  "and marks the current one as Superseded. Continue?"
        }).then(function (confirm) {
            if (!confirm.confirmed) return;

            var request = {
                ProposalId: proposalId,
                getMetadata: function () {
                    return {
                        boundParameter: null,
                        parameterTypes: {
                            ProposalId: { typeName: "Edm.String", structuralProperty: 1 }
                        },
                        operationType: 0, // 0 = Action
                        operationName: CLONE_API
                    };
                }
            };

            Xrm.WebApi.online.execute(request).then(
                function (response) {
                    if (!response.ok) {
                        throw new Error("Custom API returned status " + response.status);
                    }
                    return response.json();
                }
            ).then(
                function (result) {
                    var newId = result && result.NewProposalId;
                    if (!newId) {
                        notifyTransient(formContext,
                            "The new version was created but no id was returned.", "WARNING");
                        return;
                    }
                    // Open the new draft version so the consultant can edit and re-send it.
                    Xrm.Navigation.openForm({ entityName: "tavu_proposal", entityId: newId });
                },
                function (error) {
                    console.error("[OpenTavu.Proposal.Form] createNewVersion failed:", error);
                    notifyTransient(formContext,
                        "Couldn't create a new version: " + (error && error.message ? error.message : "unknown error"),
                        "ERROR");
                }
            );
        });
    };

    /**
     * Ribbon command — "Mark as Approved".
     *
     * Sets the proposal to Approved by Client (winning proposal), then runs the
     * Approved→Won ceremony: rolls the proposal total into the opportunity's Estimated
     * Revenue and offers to close the opportunity as Won (reusing the close dialog).
     * The single-Approved-per-opportunity rule is enforced server-side, so if another
     * proposal is already Approved the update is rejected with a clear message.
     *
     * Show only while the proposal is Sent to Client / Awaiting Decision.
     * @param {Xrm.FormContext|Xrm.ExecutionContext} primaryControl
     */
    Form.markApproved = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;
        if (!ensureSaved(formContext)) return;

        var status = getOptionValue(formContext, FIELD_STATUS_REASON);
        if (status !== STATUS_SENT_TO_CLIENT && status !== STATUS_AWAITING_DECISION) {
            notifyTransient(formContext,
                "Only a proposal that has been sent to the client can be approved.", "WARNING");
            return;
        }

        var proposalId = formContext.data.entity.getId().replace(/[{}]/g, "");
        var total = getNumber(formContext, FIELD_PROPOSAL_TOTAL);
        var oppRef = getLookup(formContext, FIELD_OPPORTUNITY);

        Xrm.Navigation.openConfirmDialog({
            title: "Mark as Approved",
            text: "Mark this proposal as Approved by Client? It becomes the winning " +
                  "proposal for the opportunity."
        }).then(function (confirm) {
            if (!confirm.confirmed) return;

            Xrm.WebApi.updateRecord("tavu_proposal", proposalId,
                { statecode: STATE_INACTIVE, statuscode: STATUS_APPROVED }).then(
                function () {
                    formContext.data.refresh(false);
                    offerCloseWon(oppRef, total);
                },
                function (error) {
                    console.error("[OpenTavu.Proposal.Form] markApproved failed:", error);
                    notifyTransient(formContext, "Couldn't approve this proposal: " + msg(error), "ERROR");
                }
            );
        });
    };

    /**
     * Ribbon command — "Mark as Lost".
     *
     * Sets the proposal to Rejected by Client. A rejected proposal is a candidate for a
     * new version (Create New Version).
     *
     * Show only while the proposal is Sent to Client / Awaiting Decision.
     * @param {Xrm.FormContext|Xrm.ExecutionContext} primaryControl
     */
    Form.markRejected = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;
        if (!ensureSaved(formContext)) return;

        var status = getOptionValue(formContext, FIELD_STATUS_REASON);
        if (status !== STATUS_SENT_TO_CLIENT && status !== STATUS_AWAITING_DECISION) {
            notifyTransient(formContext,
                "Only a proposal that has been sent to the client can be rejected.", "WARNING");
            return;
        }

        var proposalId = formContext.data.entity.getId().replace(/[{}]/g, "");

        Xrm.Navigation.openConfirmDialog({
            title: "Mark as Lost",
            text: "Mark this proposal as Rejected by Client? You can create a new version " +
                  "to re-propose."
        }).then(function (confirm) {
            if (!confirm.confirmed) return;

            Xrm.WebApi.updateRecord("tavu_proposal", proposalId,
                { statecode: STATE_INACTIVE, statuscode: STATUS_REJECTED }).then(
                function () {
                    formContext.data.refresh(false);
                    notifyTransient(formContext,
                        "Proposal marked as Rejected. Use Create New Version to re-propose.", "INFO");
                },
                function (error) {
                    console.error("[OpenTavu.Proposal.Form] markRejected failed:", error);
                    notifyTransient(formContext, "Couldn't reject this proposal: " + msg(error), "ERROR");
                }
            );
        });
    };

    /**
     * Rolls the winning total into the opportunity's Estimated Revenue (so the close
     * dialog prefills it) and offers to close the opportunity as Won using the same
     * guided dialog as the opportunity form.
     */
    function offerCloseWon(oppRef, total) {
        if (!oppRef) return;
        var oppId = oppRef.id.replace(/[{}]/g, "");

        var proceed = function () {
            Xrm.Navigation.openConfirmDialog({
                title: "Close the opportunity as Won?",
                text: "This proposal is approved. Close the opportunity as Won now?"
            }).then(function (confirm) {
                if (confirm.confirmed) openWonDialog(oppId);
            });
        };

        if (total !== null && total !== undefined) {
            var patch = {};
            patch[OPP_ESTIMATED_REVENUE] = total;
            Xrm.WebApi.updateRecord("tavu_opportunity", oppId, patch).then(proceed, proceed);
        } else {
            proceed();
        }
    }

    /** Opens the opportunity Close-as-Won custom-page dialog for the given opportunity. */
    function openWonDialog(oppId) {
        Xrm.Navigation.navigateTo(
            { pageType: "custom", name: CLOSE_DIALOG_PAGE, recordId: oppId, entityName: "won" },
            {
                target: 2, position: 1,
                width: { value: 480, unit: "px" }, height: { value: 420, unit: "px" },
                title: "Close as Won"
            }
        );
    }

    // ============================================================
    // Lifecycle UI handlers
    // ============================================================

    /**
     * OnLoad — applies the visual lock: when the proposal is Sent to the client or
     * closed, the whole form is made read-only EXCEPT the Status Reason (so the deal
     * can still advance Sent -> Awaiting -> Approved/Rejected). Mirrors the server-side
     * lock (Pl.Proposal.LifecycleTracker), so the user never edits a commercial field
     * only to hit a save error.
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.onLoad = function (executionContext) {
        var formContext = executionContext.getFormContext();
        applyLockdown(formContext);
        wireLineSubgridRefresh(formContext);
    };

    /**
     * OnChange of Status Reason — re-applies the lock so the UI locks immediately when
     * the consultant sends the proposal, without waiting for a reload.
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.onStatusReasonChange = function (executionContext) {
        applyLockdown(executionContext.getFormContext());
    };

    // ============================================================
    // Internal helpers
    // ============================================================

    /**
     * Command-bar handlers receive PrimaryControl (a FormContext); form-event
     * handlers receive an ExecutionContext. Normalize to a FormContext.
     */
    function resolveFormContext(arg) {
        if (!arg) return null;
        return (typeof arg.getFormContext === "function") ? arg.getFormContext() : arg;
    }

    /** Form notification that auto-clears so it does not linger on the form. */
    function notifyTransient(formContext, message, level) {
        formContext.ui.setFormNotification(message, level, NOTIF.CLONE);
        setTimeout(function () {
            formContext.ui.clearFormNotification(NOTIF.CLONE);
        }, NOTIF_TRANSIENT_MS);
    }

    /** True when the record is saved (has an id) and has no pending edits. */
    function ensureSaved(formContext) {
        if (!formContext.data.entity.getId()) {
            notifyTransient(formContext, "Save the proposal first.", "WARNING");
            return false;
        }
        if (formContext.data.getIsDirty()) {
            notifyTransient(formContext, "Save your pending changes first.", "WARNING");
            return false;
        }
        return true;
    }

    /** Numeric attribute value (e.g. Money), or null. */
    function getNumber(formContext, name) {
        var attr = formContext.getAttribute(name);
        if (!attr) return null;
        var v = attr.getValue();
        return (v === null || v === undefined) ? null : v;
    }

    /** First lookup reference {id, entityType, name}, or null. */
    function getLookup(formContext, name) {
        var attr = formContext.getAttribute(name);
        if (!attr) return null;
        var v = attr.getValue();
        return (v && v.length) ? v[0] : null;
    }

    /** Best-effort error message. */
    function msg(error) {
        return (error && error.message) ? error.message : "unknown error";
    }

    /**
     * When the proposal is locked (Sent / Awaiting Decision / closed), disables every
     * control except Status Reason and shows an explanatory banner. Disable-only: when
     * the proposal is still editable it leaves the form's designer settings untouched,
     * so always-read-only fields (Proposal Number, totals, inherited Customer) stay as
     * configured.
     */
    function applyLockdown(formContext) {
        formContext.ui.clearFormNotification(NOTIF.LOCKED);

        if (!isLocked(formContext)) return; // editable — leave designer settings intact

        formContext.ui.controls.forEach(function (ctrl) {
            if (!ctrl || !ctrl.setDisabled) return;
            var name = ctrl.getName ? ctrl.getName() : null;
            // Status Reason stays editable so the deal can still advance.
            if (name === FIELD_STATUS_REASON) { ctrl.setDisabled(false); return; }
            ctrl.setDisabled(true);
        });

        formContext.ui.setFormNotification(
            "This proposal is locked because it has been sent to the client (or closed). " +
            "Its fields are read-only — use Create New Version to make changes.",
            "INFO", NOTIF.LOCKED);
    }

    /** Locked when the proposal is closed (Inactive) or Sent to Client / Awaiting Decision. */
    function isLocked(formContext) {
        if (getOptionValue(formContext, "statecode") === STATE_INACTIVE) return true;
        var status = getOptionValue(formContext, FIELD_STATUS_REASON);
        return status === STATUS_SENT_TO_CLIENT || status === STATUS_AWAITING_DECISION;
    }

    function getOptionValue(formContext, name) {
        var attr = formContext.getAttribute(name);
        if (!attr) return null;
        var v = attr.getValue();
        return (v === null || v === undefined) ? null : v;
    }

    // ============================================================
    // Header totals auto-refresh (subgrid → header rollup)
    // ============================================================

    var HEADER_TOTAL_FIELDS = [
        "tavu_subtotal", "tavu_totaltax", "tavu_total", "tavu_totalcost", "tavu_grossmargin"
    ];

    /**
     * Registers a refresh on every subgrid on the form (there is no designer event for
     * subgrids — it must be wired via addOnLoad in code). A subgrid fires addOnLoad
     * whenever it (re)loads, including after a line is added/edited/deleted, so this is
     * the reliable "lines changed" hook. Fetches only the server-computed header totals
     * and paints them — no full-form refresh (avoids loops and dirtying the form).
     */
    function wireLineSubgridRefresh(formContext) {
        formContext.ui.controls.forEach(function (ctrl) {
            if (ctrl && ctrl.getControlType && ctrl.getControlType() === "subgrid" && ctrl.addOnLoad) {
                ctrl.addOnLoad(function () { refreshHeaderTotals(formContext); });
            }
        });
    }

    function refreshHeaderTotals(formContext) {
        var rawId = formContext.data.entity.getId();
        if (!rawId) return; // unsaved record has no server totals yet
        var id = rawId.replace(/[{}]/g, "");

        Xrm.WebApi.retrieveRecord("tavu_proposal", id,
            "?$select=" + HEADER_TOTAL_FIELDS.join(",")).then(
            function (record) {
                HEADER_TOTAL_FIELDS.forEach(function (name) {
                    setComputed(formContext, name, record[name]);
                });
            },
            function (error) {
                console.warn("[OpenTavu.Proposal.Form] header totals refresh failed:", error);
            }
        );
    }

    /** Paints a server-computed value onto a form field without marking it for save. */
    function setComputed(formContext, name, value) {
        var attr = formContext.getAttribute(name);
        if (!attr) return;
        attr.setValue((value === undefined) ? null : value);
        attr.setSubmitMode("never"); // display only — these are plugin-computed on the server
    }

})(OpenTavu.Proposal.Form);
