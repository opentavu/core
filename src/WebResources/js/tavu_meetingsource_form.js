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
            title: "Enable Teams sync"
        };

        var x = getXrm();
        if (!x || !x.Navigation || !x.Navigation.navigateTo) {
            if (x && x.Navigation && x.Navigation.openErrorDialog) {
                x.Navigation.openErrorDialog({ message: "Client API unavailable; cannot open the wizard." });
            }
            return;
        }

        x.Navigation.navigateTo(pageInput, navOptions).then(
            function () { /* closed; nothing to refresh */ },
            function (err) {
                x.Navigation.openErrorDialog({
                    message: "Could not open the Teams sync wizard.",
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
