"use strict";

/**
 * OpenTavu — Product Form Script
 *
 * Form: Product > Main
 * Purpose: Show the Kit Components section only when the product is a kit
 *          (tavu_iskit = Yes). A normal product shows just the details; marking
 *          it as a kit reveals the components grid below. Business Rules cannot
 *          toggle section visibility, so this is done in script.
 *
 * Form registration (designer → handler; pass execution context: yes):
 *   OnLoad                → OpenTavu.Product.Form.onLoad
 *   tavu_iskit OnChange   → OpenTavu.Product.Form.onIsKitChange
 *
 * @author  OpenTavu — Gustavo González Villani
 * @license MIT
 * @version 0.1.0
 */

var OpenTavu = OpenTavu || {};
OpenTavu.Product = OpenTavu.Product || {};
OpenTavu.Product.Form = OpenTavu.Product.Form || {};

(function (Form) {

    var FIELD_ISKIT = "tavu_iskit";              // Yes/No  (VERIFY)
    var SECTION_KIT = "Section_KitComponents";   // section holding the kit components subgrid

    /** @param {Xrm.ExecutionContext} executionContext */
    Form.onLoad = function (executionContext) {
        toggleKitSection(executionContext.getFormContext());
    };

    /** @param {Xrm.ExecutionContext} executionContext */
    Form.onIsKitChange = function (executionContext) {
        toggleKitSection(executionContext.getFormContext());
    };

    // ----- internal -----

    function toggleKitSection(formContext) {
        setSectionVisible(formContext, SECTION_KIT, isKit(formContext));
    }

    function isKit(formContext) {
        var attr = formContext.getAttribute(FIELD_ISKIT);
        return attr ? attr.getValue() === true : false;
    }

    /** Tab-agnostic: searches every tab for the section by name. */
    function setSectionVisible(formContext, sectionName, visible) {
        formContext.ui.tabs.forEach(function (tab) {
            var section = tab.sections.get(sectionName);
            if (section) section.setVisible(visible);
        });
    }

})(OpenTavu.Product.Form);
