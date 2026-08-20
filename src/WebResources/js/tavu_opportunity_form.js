"use strict";

/**
 * OpenTavu — Opportunity Form (tavu_opportunity)
 *
 * Customer field: filter by tenant Customer Mode + mirror to typed Account/Contact.
 * Lifecycle (Option A): Sales Stage drives while Open; Won/Lost takes over when
 * Closed. Rationale & schema: sales-model.md §6, §6.3bis, §7.3–§7.4.
 *
 * Form event registration (designer → handler; pass execution context):
 *   OnLoad                   → OpenTavu.Opportunity.Form.onLoad
 *   OnSave                   → OpenTavu.Opportunity.Form.onSave
 *   OnChange statecode       → OpenTavu.Opportunity.Form.onStateCodeChange
 *   OnChange statuscode      → OpenTavu.Opportunity.Form.onStatusReasonChange
 *   OnChange tavu_customer → OpenTavu.Opportunity.Form.onCustomerChange
 *
 * Command bar registration (Main form → Run JavaScript, param PrimaryControl):
 *   "Reset Probability"      → OpenTavu.Opportunity.Form.resetProbability
 *   "Close as Won"           → OpenTavu.Opportunity.Form.closeAsWon
 *   "Close as Lost"          → OpenTavu.Opportunity.Form.closeAsLost
 *   "Reopen"                 → OpenTavu.Opportunity.Form.reopen
 *
 * @author OpenTavu — Gustavo González Villani
 * SPDX-License-Identifier: MIT
 */

var OpenTavu = OpenTavu || {};
OpenTavu.Opportunity = OpenTavu.Opportunity || {};
OpenTavu.Opportunity.Form = OpenTavu.Opportunity.Form || {};

(function (Form) {

    // ============================================================
    // i18n (English 1033 / Spanish 3082)
    // ============================================================
    var TAVU_I18N = (function () {
        var S = {
            1033: {
                "closedBanner_1": "This opportunity is closed (",
                "closedBanner_2": "). Fields are read-only to preserve the historical record. To capture new work, create a new opportunity for this customer.",
                "won": "Won",
                "lost": "Lost",
                "closed": "Closed",
                "onDate": " on ",
                "customerModeWarn": "Customer Mode could not be loaded. Customer filtering is disabled. Refresh to retry.",
                "closedNoReset": "This opportunity is closed. Probability cannot be reset.",
                "resetMissingFlag": "Reset is unavailable: the override flag field is missing from this form. Add it (it can be hidden) and publish, then try again.",
                "selectStageFirst": "Select a Sales Stage first — the default probability comes from the stage.",
                "stageNoDefault": "This Sales Stage has no default probability configured.",
                "resetDone_1": "Probability reset to the stage default (",
                "resetDone_2": "%).",
                "resetSaveFailed": "Probability was reset on the form but the save failed. Save manually to apply.",
                "readStageFail": "Could not read the stage default probability. Try again.",
                "alreadyClosed": "This opportunity is already closed.",
                "savePendingClose": "Save your pending changes before closing the opportunity.",
                "saveBeforeClose": "Save the opportunity before closing it.",
                "closeAsWon": "Close as Won",
                "closeAsLost": "Close as Lost",
                "closeDialogError": "The close dialog could not be opened. Try again.",
                "alreadyOpen": "This opportunity is already open.",
                "reopenTitle": "Reopen Opportunity",
                "reopenText": "This opportunity will return to Open and re-enter the pipeline. The close details (revenue / lost reason / close date) are kept as history. Probability will reset to the current stage default. Continue?",
                "reopenDone": "Opportunity reopened. Probability reset to the stage default.",
                "reopenFail": "The opportunity could not be reopened. Try again.",
                "rejectMode_1": "This system is configured in ",
                "rejectMode_2": " mode and only allows ",
                "rejectMode_3": " as Customers. The ",
                "rejectMode_4": " you selected was not saved.",
                "anAccount": "an Account",
                "aContact": "a Contact",
                "b2bOnly": "B2B Only",
                "b2cOnly": "B2C Only",
                "accounts": "Accounts",
                "contacts": "Contacts"
            },
            3082: {
                "closedBanner_1": "Esta oportunidad está cerrada (",
                "closedBanner_2": "). Los campos son de solo lectura para preservar el registro histórico. Para capturar nuevo trabajo, cree una nueva oportunidad para este cliente.",
                "won": "Ganada",
                "lost": "Perdida",
                "closed": "Cerrada",
                "onDate": " el ",
                "customerModeWarn": "No se pudo cargar el Modo de cliente. El filtrado de clientes está deshabilitado. Actualice para reintentar.",
                "closedNoReset": "Esta oportunidad está cerrada. No se puede restablecer la probabilidad.",
                "resetMissingFlag": "El restablecimiento no está disponible: falta en este formulario el campo del indicador de override. Agréguelo (puede estar oculto) y publique; luego intente de nuevo.",
                "selectStageFirst": "Seleccione primero una Etapa de venta — la probabilidad predeterminada proviene de la etapa.",
                "stageNoDefault": "Esta Etapa de venta no tiene una probabilidad predeterminada configurada.",
                "resetDone_1": "Probabilidad restablecida al valor predeterminado de la etapa (",
                "resetDone_2": "%).",
                "resetSaveFailed": "La probabilidad se restableció en el formulario pero falló el guardado. Guarde manualmente para aplicar.",
                "readStageFail": "No se pudo leer la probabilidad predeterminada de la etapa. Intente de nuevo.",
                "alreadyClosed": "Esta oportunidad ya está cerrada.",
                "savePendingClose": "Guarde los cambios pendientes antes de cerrar la oportunidad.",
                "saveBeforeClose": "Guarde la oportunidad antes de cerrarla.",
                "closeAsWon": "Cerrar como ganada",
                "closeAsLost": "Cerrar como perdida",
                "closeDialogError": "No se pudo abrir el diálogo de cierre. Intente de nuevo.",
                "alreadyOpen": "Esta oportunidad ya está abierta.",
                "reopenTitle": "Reabrir oportunidad",
                "reopenText": "Esta oportunidad volverá a Abierta y regresará al pipeline. Los detalles del cierre (ingresos / motivo de pérdida / fecha de cierre) se conservan como historial. La probabilidad se restablecerá al valor predeterminado de la etapa actual. ¿Continuar?",
                "reopenDone": "Oportunidad reabierta. Probabilidad restablecida al valor predeterminado de la etapa.",
                "reopenFail": "No se pudo reabrir la oportunidad. Intente de nuevo.",
                "rejectMode_1": "Este sistema está configurado en ",
                "rejectMode_2": " y solo permite ",
                "rejectMode_3": " como clientes. La opción ",
                "rejectMode_4": " que seleccionó no se guardó.",
                "anAccount": "Cuenta",
                "aContact": "Contacto",
                "b2bOnly": "Solo B2B",
                "b2cOnly": "Solo B2C",
                "accounts": "Cuentas",
                "contacts": "Contactos"
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

    // Custom page (canvas) used as the guided close dialog. MUST match the unique
    // name of the custom page added to the OpenTavu solution.
    var CLOSE_DIALOG_PAGE = "tavu_opportunityclosedialog_31702";

    var CLOSE_OUTCOME = { WON: "won", LOST: "lost" };

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
    Form.onLoad = function (executionContext) {
        refreshLifecycleUi(executionContext);
        Form.enforceReadOnlyFields(executionContext);
        Form.applyCustomerModeFilter(executionContext);
    };

    /** Reserved for future save-time validations. */
    Form.onSave = function (executionContext) { };

    /** @param {Xrm.ExecutionContext} executionContext */
    Form.onStateCodeChange = function (executionContext) {
        refreshLifecycleUi(executionContext);
    };

    /** @param {Xrm.ExecutionContext} executionContext */
    Form.onStatusReasonChange = function (executionContext) {
        refreshLifecycleUi(executionContext);
    };

    /** @param {Xrm.ExecutionContext} executionContext */
    Form.onCustomerChange = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var customerAttr = formContext.getAttribute("tavu_customer");
        if (!customerAttr) return; // field not on the form — nothing to validate
        var customerValue = customerAttr.getValue();

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
    Form.applyOpenClosedEmphasis = function (executionContext) {
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
    Form.applyCloseSectionVisibility = function (executionContext) {
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

    Form.enforceReadOnlyFields = function (executionContext) {
        var formContext = executionContext.getFormContext();
        CLOSE_MANAGED_FIELDS.forEach(function (fieldName) {
            setControlDisabled(formContext, fieldName, true);
        });
    };

    /**
     * Locks all controls when closed, except subgrid/timeline (audit trail)
     * and CLOSE_MANAGED_FIELDS (which stay locked even when reopened).
     */
    Form.applyClosedLockdown = function (executionContext) {
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

    Form.applyClosedStateNotification = function (executionContext) {
        var formContext = executionContext.getFormContext();

        formContext.ui.clearFormNotification(NOTIF.CLOSED_STATE);
        if (!isClosed(formContext)) return;

        var status = getStatusReason(formContext);
        var label = (status === STATUS_WON) ? TAVU_I18N("won")
                  : (status === STATUS_LOST) ? TAVU_I18N("lost")
                  : TAVU_I18N("closed");
        var closeDate = getFormattedAttributeValue(formContext, "tavu_actualclosedate");
        var dateClause = closeDate ? (TAVU_I18N("onDate") + closeDate) : "";
        var message = TAVU_I18N("closedBanner_1") + label + dateClause +
            TAVU_I18N("closedBanner_2");

        formContext.ui.setFormNotification(message, "INFO", NOTIF.CLOSED_STATE);
    };

    /**
     * Resolves customer mode and applies PreSearch filter to the Customer
     * lookup. Non-blocking: form remains usable while mode resolves.
     */
    Form.applyCustomerModeFilter = function (executionContext) {
        var formContext = executionContext.getFormContext();

        getCustomerMode().then(
            function (mode) { applyPreSearchFilter(formContext, mode); },
            function (error) {
                formContext.ui.setFormNotification(
                    TAVU_I18N("customerModeWarn"),
                    "WARNING",
                    NOTIF.MODE_FETCH_FAILED
                );
                console.warn("[OpenTavu.Opportunity.Form] Customer Mode fetch failed:", error);
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
    Form.resetProbability = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        // A closed opportunity is historical/read-only — never touch it.
        if (isClosed(formContext)) {
            notifyTransient(formContext,
                TAVU_I18N("closedNoReset"),
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
                "[OpenTavu.Opportunity.Form] resetProbability: '" +
                FIELD_PROBABILITY_IS_MANUAL + "' is not on the form. Add it (hidden is fine) and publish.");
            notifyTransient(formContext,
                TAVU_I18N("resetMissingFlag"),
                "ERROR");
            return;
        }

        var stageAttr = formContext.getAttribute(FIELD_SALES_STAGE);
        var stageRef = stageAttr ? stageAttr.getValue() : null;
        if (!stageRef || stageRef.length === 0) {
            notifyTransient(formContext,
                TAVU_I18N("selectStageFirst"),
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
                        TAVU_I18N("stageNoDefault"),
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
                            TAVU_I18N("resetDone_1") + def + TAVU_I18N("resetDone_2"),
                            "INFO");
                    },
                    function (saveError) {
                        console.error(
                            "[OpenTavu.Opportunity.Form] resetProbability save failed:", saveError);
                        notifyTransient(formContext,
                            TAVU_I18N("resetSaveFailed"),
                            "ERROR");
                    }
                );
            },
            function (error) {
                console.error(
                    "[OpenTavu.Opportunity.Form] resetProbability failed:", error);
                notifyTransient(formContext,
                    TAVU_I18N("readStageFail"),
                    "ERROR");
            }
        );
    };

    /**
     * Ribbon command — "Close as Won".
     * Show only when the opportunity is Open (enable rule: statecode = Active).
     * @param {Xrm.FormContext|Xrm.ExecutionContext} primaryControl
     */
    Form.closeAsWon = function (primaryControl) {
        openCloseDialog(resolveFormContext(primaryControl), CLOSE_OUTCOME.WON);
    };

    /**
     * Ribbon command — "Close as Lost".
     * Show only when the opportunity is Open (enable rule: statecode = Active).
     * @param {Xrm.FormContext|Xrm.ExecutionContext} primaryControl
     */
    Form.closeAsLost = function (primaryControl) {
        openCloseDialog(resolveFormContext(primaryControl), CLOSE_OUTCOME.LOST);
    };

    /**
     * Opens the guided close custom page as a centered dialog, passing the
     * opportunity id and the outcome (won/lost). The custom page captures the
     * required inputs (Actual Revenue for Won, Lost Reason for Lost, plus close
     * date and notes), writes them to the opportunity together with the terminal
     * statecode/statuscode, and saves — which fires the server engine:
     *   - Pl.Opportunity.LifecycleTracker (Pre-Op): validates inputs, forces
     *     probability to 100/0, defaults the close date.
     *   - Pl.Opportunity.CloseOrchestrator (Post-Op): logs the
     *     tavu_opportunityclose activity and, on Won, marks the customer.
     *
     * The dialog owns the write so the optionset (Lost Reason) and numeric input
     * render natively. On dialog close we refresh the form to reflect the new
     * closed state and the plugin-derived effects.
     *
     * We require a clean (saved) form first: the dialog writes server-side
     * directly, so any unsaved edits on the open form would be missed or conflict.
     */
    function openCloseDialog(formContext, outcome) {
        if (!formContext) return;

        if (isClosed(formContext)) {
            notifyTransient(formContext, TAVU_I18N("alreadyClosed"), "INFO");
            return;
        }
        if (formContext.data.getIsDirty()) {
            notifyTransient(formContext,
                TAVU_I18N("savePendingClose"), "WARNING");
            return;
        }

        var oppId = formContext.data.entity.getId().replace(/[{}]/g, "");
        if (!oppId) {
            notifyTransient(formContext,
                TAVU_I18N("saveBeforeClose"), "WARNING");
            return;
        }

        var isWon = outcome === CLOSE_OUTCOME.WON;

        // Custom pages opened via navigateTo only forward two parameters:
        // recordId and entityName. Arbitrary keys (e.g. "outcome") are dropped by
        // the platform (confirmed limitation). We keep recordId as the real GUID and
        // repurpose ("hijack") entityName to carry the close outcome, since this page
        // is NOT record-bound and never needs the real table name. The page reads it
        // via Param("entityName").
        var pageInput = {
            pageType: "custom",
            name: CLOSE_DIALOG_PAGE,
            recordId: oppId,
            entityName: outcome // carries "won" | "lost"; read via Param("entityName")
        };
        var navOptions = {
            target: 2,      // dialog
            position: 1,    // center
            width: { value: 480, unit: "px" },
            height: { value: 420, unit: "px" },
            title: isWon ? TAVU_I18N("closeAsWon") : TAVU_I18N("closeAsLost")
        };

        Xrm.Navigation.navigateTo(pageInput, navOptions).then(
            function () {
                // Dialog closed (confirmed or cancelled). Refresh either way; the
                // custom page performed any write server-side.
                formContext.data.refresh(false).then(
                    function () { refreshLifecycleUi(wrapFormContext(formContext)); },
                    function () { refreshLifecycleUi(wrapFormContext(formContext)); }
                );
            },
            function (error) {
                console.error("[OpenTavu.Opportunity.Form] close dialog failed:", error);
                notifyTransient(formContext,
                    TAVU_I18N("closeDialogError"), "ERROR");
            }
        );
    }

    /**
     * Ribbon command — "Reopen".
     *
     * Returns a closed (Won/Lost) opportunity to Open so it can re-enter the
     * pipeline. Needs no user input, so it is fully self-contained: confirm,
     * flip statecode/statuscode, save.
     *
     * The close details (tavu_actualrevenue / tavu_lostreason /
     * tavu_actualclosedate / tavu_closenotes) are deliberately preserved as
     * history — the form keeps them locked even when reopened. On save the
     * Pl.Opportunity.LifecycleTracker plugin detects the reopen (previous
     * statecode = Inactive) and re-applies the current Sales Stage default
     * probability, clearing the manual flag.
     *
     * Registered on the Main form command bar via Run JavaScript, passing
     * PrimaryControl. Show the button only when the record is closed
     * (enable rule: statecode = Inactive).
     *
     * @param {Xrm.FormContext|Xrm.ExecutionContext} primaryControl
     */
    Form.reopen = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);
        if (!formContext) return;

        if (!isClosed(formContext)) {
            notifyTransient(formContext, TAVU_I18N("alreadyOpen"), "INFO");
            return;
        }

        Xrm.Navigation.openConfirmDialog({
            title: TAVU_I18N("reopenTitle"),
            text: TAVU_I18N("reopenText")
        }).then(function (result) {
            if (!result.confirmed) return;

            setAttributeValue(formContext, "statecode", STATE_ACTIVE);
            setAttributeValue(formContext, FIELD_STATUS_REASON, STATUS_OPEN);

            formContext.data.save().then(
                function () {
                    notifyTransient(formContext,
                        TAVU_I18N("reopenDone"), "INFO");
                    refreshLifecycleUi(wrapFormContext(formContext));
                },
                function (saveError) {
                    console.error("[OpenTavu.Opportunity.Form] reopen save failed:", saveError);
                    notifyTransient(formContext,
                        TAVU_I18N("reopenFail"), "ERROR");
                }
            );
        });
    };

    // ============================================================
    // Reserved hooks for future modules
    // ============================================================

    /** Reserved for Module 4 — AI Proposal Generator. */
    Form.refreshProposalSignals = function (executionContext) { };

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
        Form.applyOpenClosedEmphasis(executionContext);
        Form.applyCloseSectionVisibility(executionContext);
        Form.applyClosedLockdown(executionContext);
        Form.applyClosedStateNotification(executionContext);
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

    /** Adapts a FormContext into the { getFormContext() } shape the UI passes expect. */
    function wrapFormContext(formContext) {
        return { getFormContext: function () { return formContext; } };
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
                console.warn("[OpenTavu.Opportunity.Form] No tavu_systemsettings row. Defaulting to Mixed.");
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
        var control = formContext.getControl("tavu_customer");
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
        formContext.getAttribute("tavu_customer").setValue(null);

        var typeLabel = attemptedEntityType === "account" ? TAVU_I18N("anAccount") : TAVU_I18N("aContact");
        var modeLabel = mode === CUSTOMER_MODE.B2B_ONLY ? TAVU_I18N("b2bOnly") : TAVU_I18N("b2cOnly");
        var allowedLabel = mode === CUSTOMER_MODE.B2B_ONLY ? TAVU_I18N("accounts") : TAVU_I18N("contacts");
        var message = TAVU_I18N("rejectMode_1") + modeLabel + TAVU_I18N("rejectMode_2") +
            allowedLabel + TAVU_I18N("rejectMode_3") + typeLabel + TAVU_I18N("rejectMode_4");

        formContext.ui.setFormNotification(message, "WARNING", NOTIF.INVALID_CUSTOMER);
    }

})(OpenTavu.Opportunity.Form);