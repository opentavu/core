"use strict";

/**
 * OpenTavu: System Settings form (tavu_systemsettings)
 *
 * Command bar button:
 *   "Verify and complete configuration" -> OpenTavu.SystemSettings.Form.initializeConfiguration
 *   Parameter: PrimaryControl. Visible to administrators only (the Custom API checks its own
 *   execute privilege anyway).
 *
 * Calls the Custom API tavu_InitializeConfiguration (Mode = "full"): seeds the missing reference
 * rows, keeps sales periods ahead and checks the install. The API stops before the platform time
 * limit and answers Complete = false when there is more to do; this script calls it again
 * automatically (up to MAX_ROUNDS) and then shows the final summary.
 *
 * @author OpenTavu, Gustavo González Villani
 * SPDX-License-Identifier: MIT
 */

var OpenTavu = OpenTavu || {};
OpenTavu.SystemSettings = OpenTavu.SystemSettings || {};
OpenTavu.SystemSettings.Form = OpenTavu.SystemSettings.Form || {};

(function (Form) {

    var INITIALIZE_API = "tavu_InitializeConfiguration";
    var MAX_ROUNDS = 6;

    var TAVU_I18N = (function () {
        var S = {
            1033: {
                "confirmTitle": "Verify and complete configuration",
                "confirmText": "OpenTavu will create the reference data that is missing (case statuses, SLAs, calendar, sales stages, AI task prompts, units and geography), create upcoming sales periods if that option is on, and check the install. Values you already changed are never overwritten. Continue?",
                "working": "Checking and completing the configuration ({0})...",
                "doneTitle": "Configuration checked",
                "doneTitleIssues": "Configuration checked: action needed",
                "notFinished": "The setup did not finish after several rounds. Run it again to continue.",
                "error": "The configuration could not be completed: ",
                "unknownError": "unknown error",
                "savePending": "Save your pending changes first, then run Verify Setup."
            },
            3082: {
                "confirmTitle": "Verificar y completar la configuración",
                "confirmText": "OpenTavu creará los datos de referencia que falten (estados de caso, SLA, calendario, etapas de venta, prompts de las tareas de IA, unidades y geografía), creará los próximos periodos de venta si esa opción está activa y revisará la instalación. Nunca sobrescribe valores que usted ya cambió. ¿Continuar?",
                "working": "Revisando y completando la configuración ({0})...",
                "doneTitle": "Configuración revisada",
                "doneTitleIssues": "Configuración revisada: hay acciones pendientes",
                "notFinished": "La configuración no terminó después de varias rondas. Ejecútela de nuevo para continuar.",
                "error": "No se pudo completar la configuración: ",
                "unknownError": "error desconocido",
                "savePending": "Guarde primero los cambios pendientes y luego ejecute Verify Setup."
            }
        };
        function lc() { try { return Xrm.Utility.getGlobalContext().userSettings.languageId; } catch (e) { return 1033; } }
        return function (k, a0) {
            var tb = S[lc()] || S[1033];
            var v = (tb && tb[k] != null) ? tb[k] : (S[1033][k] != null ? S[1033][k] : k);
            if (a0 !== undefined) v = String(v).replace("{0}", a0);
            return v;
        };
    })();

    function callApi() {
        var request = {
            Mode: "full",
            getMetadata: function () {
                return {
                    boundParameter: null,
                    parameterTypes: { Mode: { typeName: "Edm.String", structuralProperty: 1 } },
                    operationType: 0, // 0 = Action
                    operationName: INITIALIZE_API
                };
            }
        };
        return Xrm.WebApi.online.execute(request).then(function (response) {
            if (!response.ok) throw new Error("Custom API returned status " + response.status);
            return response.json();
        });
    }

    function run(round, formContext) {
        Xrm.Utility.showProgressIndicator(TAVU_I18N("working", round + "/" + MAX_ROUNDS));
        return callApi().then(function (result) {
            if (result && result.Complete === false && round < MAX_ROUNDS) {
                return run(round + 1, formContext);
            }
            Xrm.Utility.closeProgressIndicator();

            var summary = (result && result.Summary) || "";
            var report = {};
            try { report = JSON.parse((result && result.Report) || "{}"); } catch (e) { report = {}; }
            if (result && result.Complete === false) summary = TAVU_I18N("notFinished") + "\n\n" + summary;

            var findings = (report.errors || 0) + (report.warnings || 0);
            var issues = findings > 0 || (result && result.Complete === false);
            // Size the dialog to its content: two lines when all is well, taller per finding.
            var height = issues ? Math.min(560, 260 + findings * 40) : 200;
            return Xrm.Navigation.openAlertDialog(
                { title: TAVU_I18N(issues ? "doneTitleIssues" : "doneTitle"), text: summary },
                { height: height, width: 600 }
            ).then(function () {
                if (formContext && formContext.data) formContext.data.refresh(false);
            });
        });
    }

    /**
     * Command handler. Pass PrimaryControl as the first parameter.
     * @param {object} primaryControl form context of the System Settings record
     */
    Form.initializeConfiguration = function (primaryControl) {
        var formContext = primaryControl;

        // The setup fills empty fields of this record on the server; a later save of unsaved
        // edits would overwrite them. Ask for a save first, as the proposal form does.
        try {
            if (formContext && formContext.data && formContext.data.entity.getIsDirty()) {
                Xrm.Navigation.openAlertDialog({ text: TAVU_I18N("savePending") });
                return;
            }
        } catch (e) { /* no form context: continue */ }

        Xrm.Navigation.openConfirmDialog({
            title: TAVU_I18N("confirmTitle"),
            text: TAVU_I18N("confirmText")
        }).then(function (confirm) {
            if (!confirm.confirmed) return;
            run(1, formContext).catch(function (error) {
                Xrm.Utility.closeProgressIndicator();
                console.error("[OpenTavu.SystemSettings.Form] initializeConfiguration failed:", error);
                Xrm.Navigation.openErrorDialog({
                    message: TAVU_I18N("error") + ((error && error.message) || TAVU_I18N("unknownError"))
                });
            });
        });
    };

})(OpenTavu.SystemSettings.Form);
