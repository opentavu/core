"use strict";

/**
 * OpenTavu Meeting Source Form (tavu_meetingsource). Module 3, Part B (Activity Capture), setup side.
 *
 * A meeting source is one connector row (Teams today; Fathom and others later). This script exposes
 * the "Enable Teams sync" wizard: a command that opens the tavu_teamssyncwizard.html web resource as
 * a centered modal. The wizard diagnoses the three tenant setup gates (via the tavu_ProbeMeetingSource
 * Custom API, which proxies GET /api/teams/health so the tenant key never reaches the browser) and
 * guides remediation. Design: docs/teams-sync-wizard-design.md.
 *
 * Ribbon command (Run JavaScript, pass PrimaryControl):
 *   "Test & configure sync" → OpenTavu.MeetingSource.Form.openTeamsWizard
 *      Recommended enable rule: visible on the Teams source row (MVP is Teams-only; the handler also
 *      self-guards and simply probes the Teams route regardless).
 *
 * @author OpenTavu, Gustavo González Villani
 * SPDX-License-Identifier: MIT
 */

var OpenTavu = OpenTavu || {};
OpenTavu.MeetingSource = OpenTavu.MeetingSource || {};
OpenTavu.MeetingSource.Form = OpenTavu.MeetingSource.Form || {};

(function (Form) {

    var TAVU_I18N = (function () {
        var S = {
            1033: {
                "enableTitle": "Enable Teams sync",
                "clientApiUnavailable": "Client API unavailable; cannot open the wizard.",
                "wizardOpenError": "Could not open the Teams sync wizard."
            },
            3082: {
                "enableTitle": "Habilitar sincronización de Teams",
                "clientApiUnavailable": "API de cliente no disponible; no se puede abrir el asistente.",
                "wizardOpenError": "No se pudo abrir el asistente de sincronización de Teams."
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

    var WIZARD_WEBRESOURCE = "tavu_teamssyncwizard";
    var NAME_FIELD = "tavu_name"; // connector name, e.g. "Teams"

    /**
     * Opens the Teams sync wizard as a modal dialog.
     * @param {*} primaryControl the form context (pass PrimaryControl from the command bar).
     */
    Form.openTeamsWizard = function (primaryControl) {
        var formContext = resolveFormContext(primaryControl);

        // Pass the connector name so the wizard/probe knows which source route to hit. Optional: with
        // no name it defaults to "Teams" (the only supported route today). No userId is passed, so the
        // gateway probes the first user flagged for meeting sync.
        var sourceName = "Teams";
        try {
            var v = formContext && formContext.getAttribute(NAME_FIELD) && formContext.getAttribute(NAME_FIELD).getValue();
            if (v) { sourceName = v; }
        } catch (e) { /* keep default */ }

        var pageInput = {
            pageType: "webresource",
            webresourceName: WIZARD_WEBRESOURCE,
            data: JSON.stringify({ source: sourceName })
        };
        var navOptions = {
            target: 2,          // dialog
            position: 1,        // center
            width: { value: 560, unit: "px" },
            height: { value: 640, unit: "px" },
            title: TAVU_I18N("enableTitle")
        };

        var x = getXrm();
        if (!x || !x.Navigation || !x.Navigation.navigateTo) {
            if (x && x.Navigation && x.Navigation.openErrorDialog) {
                x.Navigation.openErrorDialog({ message: TAVU_I18N("clientApiUnavailable") });
            }
            return;
        }

        x.Navigation.navigateTo(pageInput, navOptions).then(
            function () { /* closed; nothing to refresh */ },
            function (err) {
                x.Navigation.openErrorDialog({
                    message: TAVU_I18N("wizardOpenError"),
                    details: err && err.message ? err.message : String(err)
                });
            }
        );
    };

    // ---- helpers ----

    function resolveFormContext(primaryControl) {
        if (primaryControl && typeof primaryControl.getAttribute === "function") { return primaryControl; }
        try {
            if (typeof Xrm !== "undefined" && Xrm.Page && typeof Xrm.Page.getAttribute === "function") { return Xrm.Page; }
        } catch (e) {}
        return null;
    }

    function getXrm() {
        try { if (typeof Xrm !== "undefined" && Xrm.Navigation) { return Xrm; } } catch (e) {}
        try { if (window.parent && window.parent.Xrm && window.parent.Xrm.Navigation) { return window.parent.Xrm; } } catch (e) {}
        try { if (window.top && window.top.Xrm && window.top.Xrm.Navigation) { return window.top.Xrm; } } catch (e) {}
        return null;
    }

})(OpenTavu.MeetingSource.Form);
