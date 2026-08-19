"use strict";

/**
 * OpenTavu Lead Form (tavu_lead). Module 3, Step 3 (the human gate).
 *
 * The AI (Pl.Lead.Triage) already triaged the lead; when it needs a human it leaves the lead
 * in "Awaiting Human Review" (AI ran) or "Manual Review Required" (AI could not run), with
 * context in AI Recommendation. These ribbon commands are that human decision, the single
 * point where a master Contact/Account may be created, so they are shown on those two states.
 *
 * Ribbon commands (Run JavaScript, pass PrimaryControl):
 *   "Approve & Promote" → OpenTavu.Lead.Form.approveAndPromote
 *   "Link to Existing"  → OpenTavu.Lead.Form.linkToExisting
 *   "Discard"           → OpenTavu.Lead.Form.discard
 *      Recommended command visibility (enable rule): show when the lead needs a human, i.e.
 *      Status Reason is 'Awaiting Human Review' OR 'Manual Review Required'.
 *      The handlers also self-guard, and the server still owns the writes.
 *
 * Form event registration (designer → handler; pass execution context):
 *   OnLoad → OpenTavu.Lead.Form.onLoad   (surface the AI recommendation; lock closed leads)
 *
 * Approve & Promote / Link to Existing call the tavu_PromoteLead Custom API; Discard is a
 * plain status flip (no master record touched), handled here directly.
 *
 * @author OpenTavu, Gustavo González Villani
 * SPDX-License-Identifier: MIT
 */

var OpenTavu = OpenTavu || {};
OpenTavu.Lead = OpenTavu.Lead || {};
OpenTavu.Lead.Form = OpenTavu.Lead.Form || {};

(function (Form) {

    var PROMOTE_API = "tavu_PromoteLead";
    var LEAD_ENTITY = "tavu_lead";

    // tavu_lead statuscode values (real environment values).
    var STATUS_AWAITING_HUMAN_REVIEW = 576600003;
    var STATUS_MANUAL_REVIEW_REQUIRED = 576600004;
    var STATUS_PROMOTED_TO_CONTACT = 576600005;
    var STATUS_NOT_QUALIFIED = 576600008; // human "Discard" outcome (reviewed, not pursued)
    var STATE_INACTIVE = 1;

    var FIELD_STATUS_REASON = "statuscode";
    var FIELD_AI_RECOMMENDATION = "tavu_airecommendation";
    var FIELD_AI_CONFIDENCE = "tavu_aiconfidencescore";
    var FIELD_MATCHED_CONTACT = "tavu_matchedcontact";

    var NOTIF = { ACTION: "opentavu_lead_action", AI: "opentavu_lead_ai", LOCKED: "opentavu_lead_locked" };
    var NOTIF_TRANSIENT_MS = 4000;

    // ============================================================
    // Ribbon commands
    // ============================================================

    /**
     * "Approve & Promote": the one irreversible action, create the master Contact
     * (and its Account) from this anonymous lead. Delegates to tavu_PromoteLead with only
     * the LeadId; the server resolves the account (matched account, or a new one from the
     * company name), creates the contact, and closes the lead as Promoted to Contact.
     * On success, opens the new contact so the user lands on the real record.
     */
    Form.approveAndPromote = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;
        if (!ensureReviewable(formContext)) return;

        var leadId = formContext.data.entity.getId().replace(/[{}]/g, "");

        Xrm.Navigation.openConfirmDialog({
            title: "Approve & Promote",
            text: "This creates a new Contact (and Account if needed) from this lead and closes " +
                  "the lead as Promoted to Contact. Continue?"
        }).then(function (confirm) {
            if (!confirm.confirmed) return;

            Xrm.Utility.showProgressIndicator("Promoting lead…");
            callPromoteLead(leadId, null, null).then(
                function (result) {
                    Xrm.Utility.closeProgressIndicator();
                    var contactId = result && result.ContactId;
                    if (contactId) {
                        Xrm.Navigation.openForm({ entityName: "contact", entityId: contactId.replace(/[{}]/g, "") });
                    } else {
                        formContext.data.refresh(false).then(
                            function () { applyLockdown(formContext); },
                            function () { applyLockdown(formContext); });
                    }
                },
                function (error) {
                    Xrm.Utility.closeProgressIndicator();
                    console.error("[OpenTavu.Lead.Form] approveAndPromote failed:", error);
                    Xrm.Navigation.openErrorDialog({
                        message: "Couldn't promote this lead: " + msg(error)
                    });
                }
            );
        });
    };

    /**
     * "Link to Existing": the lead is really an existing person. Pick the contact and
     * link it (no new record created). Delegates to tavu_PromoteLead with LinkToContactId;
     * the server sets Promoted Contact and closes the lead.
     */
    Form.linkToExisting = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;
        if (!ensureReviewable(formContext)) return;

        var leadId = formContext.data.entity.getId().replace(/[{}]/g, "");

        // Default the picker to the AI's suggested contact, if any.
        var suggested = getLookup(formContext, FIELD_MATCHED_CONTACT);
        var lookupOptions = { entityTypes: ["contact"], allowMultiSelect: false };
        if (suggested) {
            lookupOptions.defaultEntityType = "contact";
            lookupOptions.defaultViewId = null;
        }

        Xrm.Utility.lookupObjects(lookupOptions).then(
            function (selected) {
                if (!selected || !selected.length) return; // user cancelled
                var contactId = selected[0].id.replace(/[{}]/g, "");

                Xrm.Utility.showProgressIndicator("Linking lead…");
                callPromoteLead(leadId, contactId, null).then(
                    function () {
                        Xrm.Utility.closeProgressIndicator();
                        formContext.data.refresh(false).then(
                            function () { applyLockdown(formContext); },
                            function () { applyLockdown(formContext); });
                    },
                    function (error) {
                        Xrm.Utility.closeProgressIndicator();
                        console.error("[OpenTavu.Lead.Form] linkToExisting failed:", error);
                        Xrm.Navigation.openErrorDialog({ message: "Couldn't link this lead: " + msg(error) });
                    }
                );
            },
            function (error) {
                console.warn("[OpenTavu.Lead.Form] contact picker cancelled/failed:", msg(error));
            }
        );
    };

    /**
     * "Discard": the reviewer decides this lead is not worth pursuing. No master record is
     * touched: just close the lead as Not Qualified. (Automated junk is caught earlier by the
     * AI as Discarded as Noise; this is the human's "reviewed, pass" outcome.)
     */
    Form.discard = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;
        if (!ensureReviewable(formContext)) return;

        var leadId = formContext.data.entity.getId().replace(/[{}]/g, "");

        Xrm.Navigation.openConfirmDialog({
            title: "Discard Lead",
            text: "Close this lead as Not Qualified? No contact or account will be created."
        }).then(function (confirm) {
            if (!confirm.confirmed) return;

            Xrm.WebApi.updateRecord(LEAD_ENTITY, leadId,
                { statecode: STATE_INACTIVE, statuscode: STATUS_NOT_QUALIFIED }).then(
                function () {
                    formContext.data.refresh(false).then(
                        function () { applyLockdown(formContext); },
                        function () { applyLockdown(formContext); });
                },
                function (error) {
                    console.error("[OpenTavu.Lead.Form] discard failed:", error);
                    notifyTransient(formContext, "Couldn't discard this lead: " + msg(error), "ERROR");
                }
            );
        });
    };

    // ============================================================
    // Lifecycle UI handlers
    // ============================================================

    /**
     * OnLoad: surfaces the AI recommendation (so the reviewer sees the AI's call and
     * reasoning before acting) and locks the form when the lead is already closed.
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.onLoad = function (executionContext) {
        var formContext = executionContext.getFormContext();
        showAiRecommendation(formContext);
        applyLockdown(formContext);
    };

    /** Shows the AI recommendation + confidence as an info banner while awaiting review. */
    function showAiRecommendation(formContext) {
        formContext.ui.clearFormNotification(NOTIF.AI);
        var status = getOptionValue(formContext, FIELD_STATUS_REASON);
        if (status !== STATUS_AWAITING_HUMAN_REVIEW && status !== STATUS_MANUAL_REVIEW_REQUIRED) return;

        var rec = getText(formContext, FIELD_AI_RECOMMENDATION);
        if (!rec) return;

        // Confidence only makes sense when the AI actually ran (Awaiting Human Review).
        // Manual Review Required means the AI could not run, so show the reason as a warning
        // banner without a (misleading) confidence figure.
        var confText = "";
        var level = "INFO";
        if (status === STATUS_AWAITING_HUMAN_REVIEW) {
            var conf = getNumber(formContext, FIELD_AI_CONFIDENCE);
            if (conf !== null && conf !== undefined) confText = " (confidence " + Math.round(conf) + "%)";
        } else {
            level = "WARNING";
        }
        formContext.ui.setFormNotification("AI: " + rec + confText, level, NOTIF.AI);
    }

    /**
     * When the lead is closed (Inactive: promoted, discarded, etc.), disable every
     * control and show a banner, so a resolved lead is not edited by accident. Disable-only:
     * an open lead keeps its designer settings.
     */
    function applyLockdown(formContext) {
        formContext.ui.clearFormNotification(NOTIF.LOCKED);
        if (getOptionValue(formContext, "statecode") !== STATE_INACTIVE) return;

        formContext.ui.controls.forEach(function (ctrl) {
            if (ctrl && ctrl.setDisabled) ctrl.setDisabled(true);
        });
        formContext.ui.setFormNotification(
            "This lead is closed. Its fields are read-only.", "INFO", NOTIF.LOCKED);
    }

    // ============================================================
    // Custom API call
    // ============================================================

    /**
     * Executes tavu_PromoteLead. Only the parameters that are provided are included, so the
     * request metadata matches (LeadId always; LinkToContactId / LinkToAccountId when given).
     * @returns {Promise<Object>} resolves with the parsed response { ContactId, AccountId }.
     */
    function callPromoteLead(leadId, linkContactId, linkAccountId) {
        var request = { LeadId: leadId };
        var paramTypes = { LeadId: { typeName: "Edm.String", structuralProperty: 1 } };

        if (linkContactId) {
            request.LinkToContactId = linkContactId;
            paramTypes.LinkToContactId = { typeName: "Edm.String", structuralProperty: 1 };
        }
        if (linkAccountId) {
            request.LinkToAccountId = linkAccountId;
            paramTypes.LinkToAccountId = { typeName: "Edm.String", structuralProperty: 1 };
        }

        request.getMetadata = function () {
            return {
                boundParameter: null,
                parameterTypes: paramTypes,
                operationType: 0, // 0 = Action
                operationName: PROMOTE_API
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

    /** True when the lead is saved, clean, and still Awaiting Human Review. */
    function ensureReviewable(formContext) {
        if (!formContext.data.entity.getId()) {
            notifyTransient(formContext, "Save the lead first.", "WARNING");
            return false;
        }
        if (formContext.data.getIsDirty()) {
            notifyTransient(formContext, "Save your pending changes first.", "WARNING");
            return false;
        }
        var status = getOptionValue(formContext, FIELD_STATUS_REASON);
        if (status !== STATUS_AWAITING_HUMAN_REVIEW && status !== STATUS_MANUAL_REVIEW_REQUIRED) {
            notifyTransient(formContext,
                "This lead is not awaiting review (it may already be resolved).", "WARNING");
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

})(OpenTavu.Lead.Form);
