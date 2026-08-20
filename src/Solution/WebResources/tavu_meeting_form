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

    var TAVU_I18N = (function () {
        var S = {
            1033: {
                "assocOppTitle": "Associate to opportunity",
                "createOppTitle": "Create opportunity",
                "assocOppText_1": "Associate this meeting to the suggested opportunity \"",
                "assocOppText_2": "\"? The meeting is completed and the opportunity's discovery notes are updated.",
                "createOppFor": "Create a new opportunity for ",
                "createOppAssoc": " and associate the meeting to it?",
                "theContact": "the contact ",
                "theAccount": "the account ",
                "andJoin": " and ",
                "noCustomer_1": "No existing customer is linked. This will create ",
                "noCustomer_2": " from the call (matching existing records first), then a new opportunity. Continue?",
                "createNoData": "Create a new opportunity from this meeting and associate it? Note: no customer is linked and the call has no prospect data, so this may not succeed.",
                "saveFirst": "Save the meeting first.",
                "savePending": "Save your pending changes first.",
                "drafting": "Drafting follow-up email…",
                "draftFailed": "The draft could not be created.",
                "draftError": "Couldn't draft the follow-up email: ",
                "discardTitle": "Discard Meeting",
                "discardText": "Discard this meeting? It will be closed and left out of the opportunity.",
                "discardError": "Couldn't discard this meeting: ",
                "closedLock": "This meeting is closed. Its fields are read-only.",
                "associating": "Associating meeting…",
                "associateError": "Couldn't associate this meeting: ",
                "notAwaiting": "This meeting is not awaiting review (it may already be resolved).",
                "aiPrefix": "AI: ",
                "confidenceOpen": " (confidence ",
                "confidenceClose": "%)",
                "unknownError": "unknown error"
            },
            3082: {
                "assocOppTitle": "Asociar a oportunidad",
                "createOppTitle": "Crear oportunidad",
                "assocOppText_1": "¿Asociar esta reunión a la oportunidad sugerida \"",
                "assocOppText_2": "\"? La reunión se marca como completada y se actualizan las notas de descubrimiento de la oportunidad.",
                "createOppFor": "Crear una nueva oportunidad para ",
                "createOppAssoc": " y asociar la reunión a ella?",
                "theContact": "el contacto ",
                "theAccount": "la cuenta ",
                "andJoin": " y ",
                "noCustomer_1": "No hay ningún cliente vinculado. Esto creará ",
                "noCustomer_2": " a partir de la llamada (buscando primero registros existentes) y luego una nueva oportunidad. ¿Continuar?",
                "createNoData": "¿Crear una nueva oportunidad a partir de esta reunión y asociarla? Nota: no hay cliente vinculado y la llamada no tiene datos del prospecto, por lo que podría no completarse.",
                "saveFirst": "Guarde primero la reunión.",
                "savePending": "Guarde primero los cambios pendientes.",
                "drafting": "Redactando el correo de seguimiento…",
                "draftFailed": "No se pudo crear el borrador.",
                "draftError": "No se pudo redactar el correo de seguimiento: ",
                "discardTitle": "Descartar reunión",
                "discardText": "¿Descartar esta reunión? Se cerrará y quedará fuera de la oportunidad.",
                "discardError": "No se pudo descartar esta reunión: ",
                "closedLock": "Esta reunión está cerrada. Sus campos son de solo lectura.",
                "associating": "Asociando la reunión…",
                "associateError": "No se pudo asociar esta reunión: ",
                "notAwaiting": "Esta reunión no está en espera de revisión (puede que ya esté resuelta).",
                "aiPrefix": "IA: ",
                "confidenceOpen": " (confianza ",
                "confidenceClose": "%)",
                "unknownError": "error desconocido"
            }
        };
        function lc() { try { return Xrm.Utility.getGlobalContext().userSettings.languageId; } catch (e) { return 1033; } }
        return function (k, a0, a1) {
            var tb = S[lc()] || S[1033];
            var v = (tb && tb[k] != null) ? tb[k] : (S[1033][k] != null ? S[1033][k] : k);
            if (a0 !== undefined) v = String(v).replace("{0}", a0);
            if (a1 !== undefined) v = String(v).replace("{1}", a1);
            return v;
        };
    })();

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
    var FIELD_ACCOUNT = "tavu_account";
    var FIELD_CONTACT = "tavu_contact";

    // Prospect fields (AI-extracted; shown only when nothing matched, so the rep can review/edit
    // before Create Opportunity auto-provisions the contact + account from the call).
    var FIELD_PROSPECT_COMPANY = "tavu_prospectcompanyname";
    var FIELD_PROSPECT_FIRST = "tavu_prospectfirstname";
    var FIELD_PROSPECT_LAST = "tavu_prospectlastname";
    var FIELD_PROSPECT_EMAIL = "tavu_prospectemail";
    var FIELD_PROSPECT_PHONE = "tavu_prospectphone";
    var PROSPECT_FIELDS = [
        FIELD_PROSPECT_COMPANY, FIELD_PROSPECT_FIRST, FIELD_PROSPECT_LAST,
        FIELD_PROSPECT_EMAIL, FIELD_PROSPECT_PHONE
    ];

    // Form section holding the prospect fields. Shown only when prospect data was captured AND no
    // customer is linked; hidden otherwise so the form stays clean.
    var SECTION_PROSPECT = "section_prospectnewcustomer";

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
                title: TAVU_I18N("assocOppTitle"),
                text: TAVU_I18N("assocOppText_1") + suggested.name +
                      TAVU_I18N("assocOppText_2")
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
            title: TAVU_I18N("createOppTitle"),
            text: buildCreateConfirmText(formContext)
        }).then(function (confirm) {
            if (!confirm.confirmed) return;
            runAssociate(formContext, meetingId, { OpportunityId: null, CreateNewOpportunity: true }, true);
        });
    };

    /**
     * Builds the Create Opportunity confirm text. When an account/contact is already matched, it is
     * a plain "create opportunity". When nothing matched but the AI captured prospect data, it warns
     * that the contact and account will be created from the call (the 2nd-line human write).
     */
    function buildCreateConfirmText(formContext) {
        var account = getLookup(formContext, FIELD_ACCOUNT);
        var contact = getLookup(formContext, FIELD_CONTACT);
        if (account || contact) {
            var who = account ? account.name : contact.name;
            return TAVU_I18N("createOppFor") + who + TAVU_I18N("createOppAssoc");
        }
        var company = getText(formContext, FIELD_PROSPECT_COMPANY);
        var first = getText(formContext, FIELD_PROSPECT_FIRST) || "";
        var last = getText(formContext, FIELD_PROSPECT_LAST) || "";
        var person = (first + " " + last).trim();
        if (company || person) {
            var parts = [];
            if (person) parts.push(TAVU_I18N("theContact") + person);
            if (company) parts.push(TAVU_I18N("theAccount") + company);
            return TAVU_I18N("noCustomer_1") + parts.join(TAVU_I18N("andJoin")) +
                TAVU_I18N("noCustomer_2");
        }
        return TAVU_I18N("createNoData");
    }

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
            notifyTransient(formContext, TAVU_I18N("saveFirst"), "WARNING");
            return;
        }
        var meetingId = formContext.data.entity.getId().replace(/[{}]/g, "");

        Xrm.Utility.showProgressIndicator(TAVU_I18N("drafting"));
        callBuildMeetingEmailDraft(meetingId).then(
            function (result) {
                Xrm.Utility.closeProgressIndicator();
                var emailId = result && result.EmailId;
                if (!emailId) {
                    notifyTransient(formContext, TAVU_I18N("draftFailed"), "ERROR");
                    return;
                }
                openRecordModal(formContext, EMAIL_ENTITY, emailId.replace(/[{}]/g, ""));
            },
            function (error) {
                Xrm.Utility.closeProgressIndicator();
                console.error("[OpenTavu.Meeting.Form] reviewDraft failed:", error);
                Xrm.Navigation.openErrorDialog({ message: TAVU_I18N("draftError") + msg(error) });
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
            title: TAVU_I18N("discardTitle"),
            text: TAVU_I18N("discardText")
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
                    notifyTransient(formContext, TAVU_I18N("discardError") + msg(error), "ERROR");
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
        applyProspectVisibility(formContext);
        applyLockdown(formContext);
    };

    /**
     * Shows the AI-extracted prospect fields ONLY when no account/contact matched, so the rep can
     * review or correct them before Create Opportunity provisions the customer from the call. When
     * a customer is already linked, these fields are hidden to keep the form clean. No-ops silently
     * if the fields are not on the form.
     */
    function applyProspectVisibility(formContext) {
        var hasCustomer = !!(getLookup(formContext, FIELD_ACCOUNT) || getLookup(formContext, FIELD_CONTACT));
        var hasProspect = PROSPECT_FIELDS.some(function (name) {
            return !!getText(formContext, name);
        });
        // Visible only when the AI captured prospect data and no customer is linked yet.
        setSectionVisible(formContext, SECTION_PROSPECT, !hasCustomer && hasProspect);
    }

    /** Shows/hides a form section by name, searching every tab. No-op if not found. */
    function setSectionVisible(formContext, sectionName, visible) {
        var tabs = formContext.ui.tabs.get();
        for (var i = 0; i < tabs.length; i++) {
            var section = tabs[i].sections.get(sectionName);
            if (section) { section.setVisible(visible); return; }
        }
    }

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
            if (conf !== null && conf !== undefined) confText = TAVU_I18N("confidenceOpen") + Math.round(conf) + TAVU_I18N("confidenceClose");
        } else {
            level = "WARNING";
        }
        formContext.ui.setFormNotification(TAVU_I18N("aiPrefix") + summary + confText, level, NOTIF.AI);
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
            TAVU_I18N("closedLock"), "INFO", NOTIF.LOCKED);
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
        Xrm.Utility.showProgressIndicator(TAVU_I18N("associating"));
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
                Xrm.Navigation.openErrorDialog({ message: TAVU_I18N("associateError") + msg(error) });
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
            notifyTransient(formContext, TAVU_I18N("saveFirst"), "WARNING");
            return false;
        }
        if (formContext.data.getIsDirty()) {
            notifyTransient(formContext, TAVU_I18N("savePending"), "WARNING");
            return false;
        }
        var status = getOptionValue(formContext, FIELD_STATUS_REASON);
        if (status !== STATUS_PROCESSED && status !== STATUS_MANUAL_REVIEW) {
            notifyTransient(formContext,
                TAVU_I18N("notAwaiting"), "WARNING");
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
        return (error && error.message) ? error.message : TAVU_I18N("unknownError");
    }

})(OpenTavu.Meeting.Form);
