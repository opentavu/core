"use strict";

/**
 * OpenTavu — Sales Target Form (tavu_salestarget)
 *
 * Makes the "subject" dimension required to match the chosen Scope, so a quota row is
 * never saved without the entity it belongs to (which would leave a hole in the machine
 * key tavu_targetkey and orphan the record from the attainment rollups):
 *
 *   Scope = User          → Sales Rep (tavu_salesrep)        REQUIRED;  Business Line OPTIONAL
 *   Scope = Team          → Team (tavu_team)                 REQUIRED;  Business Line OPTIONAL
 *   Scope = Business Line → Business Line (tavu_businessline) REQUIRED
 *   Scope = Company       → none required
 *
 * Business Line is an OPTIONAL slice on User- and Team-scope targets: a rep (or team) can
 * carry several quota rows in the same period, one per business line (e.g. Gustavo $40k in
 * Consulting and $50k in Architecture = two User targets, same rep + period, different line).
 * Left empty, the row is that rep's / team's non-line-specific ("general") quota. These
 * per-line rows do NOT auto-sum into a rep total; that consolidation is the higher-scope
 * aggregation done by the snapshot flow. Matches Pl.Opportunity.ForecastStamp, which prefers
 * the exact-line target and falls back to the no-line one. Business Line is hidden only on
 * Company scope.
 *
 * When the Scope changes, the dimensions that no longer apply are cleared so the
 * plugin-stamped key (Pl.SalesTarget.KeyStamp) never carries a stale rep/team.
 *
 * IMPORTANT: the Sales Rep lookup is tavu_salesrep (NOT tavu_user). The C# plugins
 * (KeyStamp AttrUser, ForecastStamp AttrTargetUser) must use the SAME logical name.
 *
 * This is form UX only. The server-side plugin (KeyStamp) is the source of truth for the
 * key; nothing here writes it. The required levels set here are also NOT a substitute for
 * a server-side uniqueness rule — the alternate key on tavu_targetkey enforces that.
 *
 * Form event registration (designer → handler; pass execution context):
 *   OnLoad              → OpenTavu.SalesTarget.Form.onLoad
 *   OnChange tavu_scope → OpenTavu.SalesTarget.Form.onScopeChange
 *
 * @author OpenTavu — Gustavo González Villani
 * SPDX-License-Identifier: MIT
 */

var OpenTavu = OpenTavu || {};
OpenTavu.SalesTarget = OpenTavu.SalesTarget || {};
OpenTavu.SalesTarget.Form = OpenTavu.SalesTarget.Form || {};

(function (Form) {

    // ---- Fields ------------------------------------------------------------
    var FIELD_SCOPE = "tavu_scope";
    var FIELD_REP = "tavu_salesrep";
    var FIELD_TEAM = "tavu_team";
    var FIELD_LINE = "tavu_businessline";

    // ---- Scope option values (tavu_scope, publisher 576600xxx range) -------
    var SCOPE_USER = 576600000;
    var SCOPE_TEAM = 576600001;
    var SCOPE_BUSINESS_LINE = 576600002;
    var SCOPE_COMPANY = 576600003;

    /**
     * OnLoad — applies the scope rules against the saved value without clearing
     * anything (respects existing data; does not dirty a clean record).
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.onLoad = function (executionContext) {
        applyScopeRules(executionContext.getFormContext(), false);
    };

    /**
     * OnChange of Scope — re-applies the rules and clears the dimensions that no longer
     * apply, so the key the plugin builds is clean.
     * @param {Xrm.ExecutionContext} executionContext
     */
    Form.onScopeChange = function (executionContext) {
        applyScopeRules(executionContext.getFormContext(), true);
    };

    /**
     * Core rule engine.
     * @param {Xrm.FormContext} formContext
     * @param {boolean} clearIrrelevant  when true, clear the now-inapplicable dimensions
     */
    function applyScopeRules(formContext, clearIrrelevant) {
        var scope = getOptionValue(formContext, FIELD_SCOPE);

        // Defaults: nothing required, subject fields visible, business line visible.
        var requireUser = false, requireTeam = false, requireLine = false;
        var showUser = true, showTeam = true, showLine = true;

        switch (scope) {
            case SCOPE_USER:
                requireUser = true;
                showTeam = false;          // team never applies to a User target
                // showLine stays true: Business Line is an optional per-line slice for the rep.
                break;
            case SCOPE_TEAM:
                requireTeam = true;
                showUser = false;          // rep never applies to a Team target
                // showLine stays true: Business Line is an optional per-line slice for the team.
                break;
            case SCOPE_BUSINESS_LINE:
                requireLine = true;        // only the business line applies
                showUser = false;
                showTeam = false;
                break;
            case SCOPE_COMPANY:
                showUser = false;          // company-wide: no subject dimension
                showTeam = false;
                showLine = false;
                break;
            default:
                // No scope selected yet: leave everything optional and visible.
                break;
        }

        setField(formContext, FIELD_REP, requireUser, showUser, clearIrrelevant);
        setField(formContext, FIELD_TEAM, requireTeam, showTeam, clearIrrelevant);
        setField(formContext, FIELD_LINE, requireLine, showLine, clearIrrelevant);
    }

    /**
     * Applies required level + visibility to one lookup, and (optionally) clears its value
     * when it is being hidden. A required field is always left visible so the save is never
     * blocked by a hidden-yet-required control.
     */
    function setField(formContext, name, required, visible, clearWhenHidden) {
        var attr = formContext.getAttribute(name);
        if (!attr) return; // field not on this form — nothing to do

        attr.setRequiredLevel(required ? "required" : "none");

        // Never hide a required field.
        var show = visible || required;
        var controls = attr.controls;
        if (controls && controls.forEach) {
            controls.forEach(function (ctrl) {
                if (ctrl && ctrl.setVisible) ctrl.setVisible(show);
            });
        }

        // Clear a value only when the field is being hidden AND we were asked to (scope change).
        if (clearWhenHidden && !show) {
            var cur = attr.getValue();
            if (cur !== null && cur !== undefined) {
                attr.setValue(null);
                attr.setSubmitMode("always"); // ensure the cleared value is persisted
            }
        }
    }

    function getOptionValue(formContext, name) {
        var attr = formContext.getAttribute(name);
        if (!attr) return null;
        var v = attr.getValue();
        return (v === null || v === undefined) ? null : v;
    }

})(OpenTavu.SalesTarget.Form);
