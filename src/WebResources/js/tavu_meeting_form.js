"use strict";

/**
 * OpenTavu Meeting Form (tavu_meeting). Module 3, Part B (Activity Capture), the human step.
 *
 * The AI (Pl.Meeting.Capture) already captured the meeting: summary, discovery extract, a matched
 * contact/account, and a suggested opportunity, leaving the meeting in "Processed" (AI ran) or
 * "Manual Review Required" (AI could not run). These ribbon commands are the human decision that
 * commits the meeting to a deal and enriches it. Association and opportunity creation call the
 * tavu_AssociateMeeting Custom API; Discard is a plain status flip handled here.
 *
 * Ribbon commands (Run JavaScript, pass PrimaryControl):
 *   "Associate to opportunity" → OpenTavu.Meeting.Form.associate
 *   "Create opportunity"       → OpenTavu.Meeting.Form.createOpportunity
 *   "Review draft email"       → OpenTavu.Meeting.Form.reviewDraft
 *   "Discard"                  → OpenTavu.Meeting.Form.discard
 *      Recommended enable rule: show when the meeting needs a human, i.e. Status Reason is
 *      'Processed' OR 'Manual Review Required'. The handlers also self-guard.
 *
 * Form event registration (designer → handler; pass execution context):
 *   OnLoad → OpenTavu.Meeting.Form.onLoad   (surface the AI summary; lock resolved meetings)
 *
 * @author OpenTavu, Gustavo González Villani
 * SPDX-License-Identifier: MIT
 */

var OpenTavu = OpenTavu || {};
OpenTavu.Meeting = OpenTavu.Meeting || {};
OpenTavu.Meeting.Form = OpenTavu.Meeting.Form || {};

(function (Form) {

    var ASSOCIATE_API = "tavu_AssociateMeeting";
    var EMAIL_DRAFT_API = "tavu_BuildMeetingEmailDraft";
    var MEETING_ENTITY = "tavu_meeting";
    var OPP_ENTITY = "tavu_opportunity";
    var EMAIL_ENTITY = "email";

    // tavu_meeting statuscode values (mirror Pl.Meeting.Capture / AssociateMeeting).
    var STATUS_PROCESSED = 576600003;      // Open, awaiting human association
    var STATUS_MANUAL_REVIEW = 576600004;  // Open, AI could not run
    var STATUS_DISCARDED = 576600006;      // Canceled outcome
    var STATE_CANCELED = 2;

    var FIELD_STATUS_REASON = "statuscode";
    var FIELD_SUGGESTED_OPP = "tavu_suggestedopportunity";
    var FIELD_SUMMARY = "tavu_summary";
    var FIELD_CONFIDENCE = "tavu_aiconfidence";     // stored 0-100
    var FIELD_DRAFT_EMAIL = "tavu_draftemail";      // lookup -> email (BuildMeetingEmailDraft, later)

    var NOTIF = { ACTION: "opentavu_meeting_action", AI: "opentavu_meeting_ai", LOCKED: "opentavu_meeting_locked" };
    var NOTIF_TRANSIENT_MS = 4000;

    // ============================================================
    // Ribbon commands
    // ============================================================

    /**
     * "Associate to opportunity": AI-first path. If the AI suggested an opportunity, one click
     * accepts it (the server uses the meeting's Suggested Opportunity). If there is no suggestion,
     * open an opportunity picker and associate to the chosen one. On success the meeting is
     * completed as Reviewed and the deal's discovery notes are consolidated server-side.
     */
    Form.associate = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;
        if (!ensureReviewable(formContext)) return;

        var meetingId = formContext.data.entity.getId().replace(/[{}]/g, "");
        var suggested = getLookup(formContext, FIELD_SUGGESTED_OPP);

        if (suggested) {
            Xrm.Navigation.openConfirmDialog({
                title: "Associate to opportunity",
                text: "Associate this meeting to the suggested opportunity \"" + suggested.name +
                      "\"? The meeting is completed and the opportunity's discovery notes are updated."
            }).then(function (confirm) {
                if (!confirm.confirmed) return;
                runAssociate(formContext, meetingId, { OpportunityId: null, CreateNewOpportunity: false });
            });
            return;
        }

        // No suggestion: let the human pick the opportunity.
        Xrm.Utility.lookupObjects({ entityTypes: [OPP_ENTITY], allowMultiSelect: false }).then(
            function (selected) {
                if (!selected || !selected.length) return; // cancelled
                var oppId = selected[0].id.replace(/[{}]/g, "");
                runAssociate(formContext, meetingId, { OpportunityId: oppId, CreateNewOpportunity: false });
            },
            function (error) {
                console.warn("[OpenTavu.Meeting.Form] opportunity picker cancelled/failed:", msg(error));
            }
        );
    };

    /**
     * "Create opportunity": no fitting opportunity exists. Create a new one from the meeting's
     * matched account/contact (server-side), associate the meeting to it, and open it so the
     * user lands on the new deal.
     */
    Form.createOpportunity = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;
        if (!ensureReviewable(formContext)) return;

        var meetingId = formContext.data.entity.getId().replace(/[{}]/g, "");

        Xrm.Navigation.openConfirmDialog({
            title: "Create opportunity",
            text: "Create a new opportunity from this meeting's customer and associate the meeting to it?"
        }).then(function (confirm) {
            if (!confirm.confirmed) return;
            runAssociate(formContext, meetingId, { OpportunityId: null, CreateNewOpportunity: true }, true);
        });
    };

    /**
     * "Review draft email": open the follow-up draft for the rep to corroborate and send, in a
     * modal dialog so closing it returns to this meeting. If no draft exists yet, generate it
     * first via tavu_BuildMeetingEmailDraft, then open it. On close the meeting is refreshed.
     */
    Form.reviewDraft = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        var existing = getLookup(formContext, FIELD_DRAFT_EMAIL);
        if (existing) {
            openRecordModal(formContext, EMAIL_ENTITY, existing.id.replace(/[{}]/g, ""));
            return;
        }

        if (!formContext.data.entity.getId()) {
            notifyTransient(formContext, "Save the meeting first.", "WARNING");
            return;
        }
        var meetingId = formContext.data.entity.getId().replace(/[{}]/g, "");

        Xrm.Utility.showProgressIndicator("Drafting follow-up email…");
        callBuildMeetingEmailDraft(meetingId).then(
            function (result) {
                Xrm.Utility.closeProgressIndicator();
                var emailId = result && result.EmailId;
                if (!emailId) {
                    notifyTransient(formContext, "The draft could not be created.", "ERROR");
                    return;
                }
                openRecordModal(formContext, EMAIL_ENTITY, emailId.replace(/[{}]/g, ""));
            },
            function (error) {
                Xrm.Utility.closeProgressIndicator();
                console.error("[OpenTavu.Meeting.Form] reviewDraft failed:", error);
                Xrm.Navigation.openErrorDialog({ message: "Couldn't draft the follow-up email: " + msg(error) });
            }
        );
    };

    /**
     * "Discard": the meeting has no discovery value. Cancel it as Discarded. No opportunity is
     * touched. (Empty/failed captures are already routed to Manual Review by the plugin; this is
     * the human "reviewed, not useful" outcome.)
     */
    Form.discard = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;
        if (!ensureReviewable(formContext)) return;

        var meetingId = formContext.data.entity.getId().replace(/[{}]/g, "");

        Xrm.Navigation.openConfirmDialog({
            title: "Discard Meeting",
            text: "Discard this meeting? It will be closed and left out of the opportunity."
        }).then(function (confirm) {
            if (!confirm.confirmed) return;

            Xrm.WebApi.updateRecord(MEETING_ENTITY, meetingId,
                { statecode: STATE_CANCELED, statuscode: STATUS_DISCARDED }).then(
                function () {
                    formContext.data.refresh(false).then(
                        function () { applyLockdown(formContext); },
                        function () { applyLockdown(formContext); });
                },
                function (error) {
                    console.error("[OpenTavu.Meeting.Form] discard failed:", error);
                    notifyTransient(formContext, "Couldn't discard this meeting: " + msg(error), "ERROR");
                }
            );
        });
    };

    /**
     * "New Meeting": grid command entry point. Opens a blank meeting form so reps create the
     * right activity in one click, instead of digging into "Other Activities". Grid context,
     * so no PrimaryControl is needed.
     */
    Form.newRecord = function () {
        Xrm.Navigation.openForm({ entityName: MEETING_ENTITY, useQuickCreateForm: false });
    };

    // ============================================================
    // Lifecycle UI handlers
    // ============================================================

    /**
     * OnLoad: surfaces the AI summary + confidence so the reviewer sees the AI's read before
     * acting, and locks the form when the meeting is already resolved (Completed/Canceled).
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.onLoad = function (executionContext) {
        var formContext = executionContext.getFormContext();
        showAiSummary(formContext);
        applyLockdown(formContext);
    };

    /** Shows the AI summary + confidence as a banner while the meeting awaits the human. */
    function showAiSummary(formContext) {
        formContext.ui.clearFormNotification(NOTIF.AI);
        var status = getOptionValue(formContext, FIELD_STATUS_REASON);
        if (status !== STATUS_PROCESSED && status !== STATUS_MANUAL_REVIEW) return;

        var summary = getText(formContext, FIELD_SUMMARY);
        if (!summary) return;

        // Confidence only makes sense when the AI ran (Processed). Manual Review Required means
        // the AI could not run, so the summary carries the reason; show it as a warning.
        var confText = "";
        var level = "INFO";
        if (status === STATUS_PROCESSED) {
            var conf = getNumber(formContext, FIELD_CONFIDENCE);
            if (conf !== null && conf !== undefined) confText = " (confidence " + Math.round(conf) + "%)";
        } else {
            level = "WARNING";
        }
        formContext.ui.setFormNotification("AI: " + summary + confText, level, NOTIF.AI);
    }

    /**
     * When the meeting is resolved (Completed = Reviewed, or Canceled = Discarded), disable every
     * control and show a banner so a closed activity is not edited by accident.
     */
    function applyLockdown(formContext) {
        formContext.ui.clearFormNotification(NOTIF.LOCKED);
        if (getOptionValue(formContext, "statecode") === 0) return; // Open: leave editable

        formContext.ui.controls.forEach(function (ctrl) {
            if (ctrl && ctrl.setDisabled) ctrl.setDisabled(true);
        });
        formContext.ui.setFormNotification(
            "This meeting is closed. Its fields are read-only.", "INFO", NOTIF.LOCKED);
    }

    // ============================================================
    // Custom API call
    // ============================================================

    /**
     * Runs tavu_AssociateMeeting. When openOpp is true, opens the resulting opportunity in a modal
     * dialog (so closing it returns to this meeting); otherwise just refreshes and locks. Either
     * way the meeting is refreshed at the end. Surfaces whether discovery was consolidated.
     */
    function runAssociate(formContext, meetingId, opts, openOpp) {
        Xrm.Utility.showProgressIndicator("Associating meeting…");
        callAssociateMeeting(meetingId, opts).then(
            function (result) {
                Xrm.Utility.closeProgressIndicator();
                var oppId = result && result.OpportunityId;
                if (openOpp && oppId) {
                    // Modal opp form; on close we refresh the meeting (openRecordModal handles it).
                    openRecordModal(formContext, OPP_ENTITY, oppId.replace(/[{}]/g, ""));
                    return;
                }
                formContext.data.refresh(false).then(
                    function () { applyLockdown(formContext); },
                    function () { applyLockdown(formContext); });
            },
            function (error) {
                Xrm.Utility.closeProgressIndicator();
                console.error("[OpenTavu.Meeting.Form] associate failed:", error);
                Xrm.Navigation.openErrorDialog({ message: "Couldn't associate this meeting: " + msg(error) });
            }
        );
    }

    /**
     * Opens a record in a centered modal dialog. Its promise resolves when the dialog closes, so
     * we refresh (and re-lock) the meeting then, keeping the user inside the meeting flow.
     */
    function openRecordModal(formContext, entityName, recordId) {
        var pageInput = { pageType: "entityrecord", entityName: entityName, entityId: recordId };
        var navOptions = { target: 2, position: 1, width: { value: 70, unit: "%" } };
        Xrm.Navigation.navigateTo(pageInput, navOptions).then(
            function () {
                formContext.data.refresh(false).then(
                    function () { applyLockdown(formContext); },
                    function () { applyLockdown(formContext); });
            },
            function (error) {
                console.warn("[OpenTavu.Meeting.Form] modal dialog closed with error:", msg(error));
                formContext.data.refresh(false);
            }
        );
    }

    /**
     * Executes tavu_AssociateMeeting. Only provided parameters are included so the request
     * metadata matches (MeetingId always; OpportunityId / CreateNewOpportunity when given).
     * @returns {Promise<Object>} resolves with { OpportunityId, DiscoveryConsolidated }.
     */
    function callAssociateMeeting(meetingId, opts) {
        opts = opts || {};
        var request = { MeetingId: meetingId };
        var paramTypes = { MeetingId: { typeName: "Edm.String", structuralProperty: 1 } };

        if (opts.OpportunityId) {
            request.OpportunityId = opts.OpportunityId;
            paramTypes.OpportunityId = { typeName: "Edm.String", structuralProperty: 1 };
        }
        if (opts.CreateNewOpportunity) {
            request.CreateNewOpportunity = true;
            paramTypes.CreateNewOpportunity = { typeName: "Edm.Boolean", structuralProperty: 1 };
        }
        if (opts.OpportunityTopic) {
            request.OpportunityTopic = opts.OpportunityTopic;
            paramTypes.OpportunityTopic = { typeName: "Edm.String", structuralProperty: 1 };
        }

        request.getMetadata = function () {
            return {
                boundParameter: null,
                parameterTypes: paramTypes,
                operationType: 0, // 0 = Action
                operationName: ASSOCIATE_API
            };
        };

        return Xrm.WebApi.online.execute(request).then(function (response) {
            if (!response.ok) throw new Error("Custom API returned status " + response.status);
            return response.json();
        });
    }

    /**
     * Executes tavu_BuildMeetingEmailDraft (MeetingId -> EmailId). Generates the AI follow-up
     * draft server-side and returns the new email's id.
     * @returns {Promise<Object>} resolves with { EmailId }.
     */
    function callBuildMeetingEmailDraft(meetingId) {
        var request = { MeetingId: meetingId };
        request.getMetadata = function () {
            return {
                boundParameter: null,
                parameterTypes: { MeetingId: { typeName: "Edm.String", structuralProperty: 1 } },
                operationType: 0, // 0 = Action
                operationName: EMAIL_DRAFT_API
            };
        };
        return Xrm.WebApi.online.execute(request).then(function (response) {
            if (!response.ok) throw new Error("Custom API returned status " + response.status);
            return response.json();
        });
    }

    // ============================================================
    // Internal helpers
    // ============================================================

    function resolveFormContext(arg) {
        if (!arg) return null;
        return (typeof arg.getFormContext === "function") ? arg.getFormContext() : arg;
    }

    /** True when the meeting is saved, clean, and still awaiting the human (Processed / Manual Review). */
    function ensureReviewable(formContext) {
        if (!formContext.data.entity.getId()) {
            notifyTransient(formContext, "Save the meeting first.", "WARNING");
            return false;
        }
        if (formContext.data.getIsDirty()) {
            notifyTransient(formContext, "Save your pending changes first.", "WARNING");
            return false;
        }
        var status = getOptionValue(formContext, FIELD_STATUS_REASON);
        if (status !== STATUS_PROCESSED && status !== STATUS_MANUAL_REVIEW) {
            notifyTransient(formContext,
                "This meeting is not awaiting review (it may already be resolved).", "WARNING");
            return false;
        }
        return true;
    }

    function notifyTransient(formContext, message, level) {
        formContext.ui.setFormNotification(message, level, NOTIF.ACTION);
        setTimeout(function () { formContext.ui.clearFormNotification(NOTIF.ACTION); }, NOTIF_TRANSIENT_MS);
    }

    function getOptionValue(formContext, name) {
        var attr = formContext.getAttribute(name);
        if (!attr) return null;
        var v = attr.getValue();
        return (v === null || v === undefined) ? null : v;
    }

    function getText(formContext, name) {
        var attr = formContext.getAttribute(name);
        if (!attr) return null;
        var v = attr.getValue();
        return (v === null || v === undefined || String(v).trim() === "") ? null : v;
    }

    function getNumber(formContext, name) {
        var attr = formContext.getAttribute(name);
        if (!attr) return null;
        var v = attr.getValue();
        return (v === null || v === undefined) ? null : v;
    }

    function getLookup(formContext, name) {
        var attr = formContext.getAttribute(name);
        if (!attr) return null;
        var v = attr.getValue();
        return (v && v.length) ? v[0] : null;
    }

    function msg(error) {
        return (error && error.message) ? error.message : "unknown error";
    }

})(OpenTavu.Meeting.Form);
