/*
 * ATTENTION: The "eval" devtool has been used (maybe by default in mode: "development").
 * This devtool is neither made for production nor for readable output files.
 * It uses "eval()" calls to create a separate source file in the browser devtools.
 * If you are trying to read the output file, select a different devtool (https://webpack.js.org/configuration/devtool/)
 * or disable the default devtool with "devtool: false".
 * If you are looking for production-ready output files, see mode: "production" (https://webpack.js.org/configuration/mode/).
 */
var pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad;
/******/ (() => { // webpackBootstrap
/******/ 	"use strict";
/******/ 	var __webpack_modules__ = ({

/***/ "./AiAssessment/components/AiAssessmentCard.tsx"
/*!******************************************************!*\
  !*** ./AiAssessment/components/AiAssessmentCard.tsx ***!
  \******************************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   AiAssessmentCard: () => (/* binding */ AiAssessmentCard)\n/* harmony export */ });\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! react */ \"react\");\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0___default = /*#__PURE__*/__webpack_require__.n(react__WEBPACK_IMPORTED_MODULE_0__);\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__ = __webpack_require__(/*! @fluentui/react-components */ \"@fluentui/react-components\");\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1___default = /*#__PURE__*/__webpack_require__.n(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__);\n\n\nvar useStyles = (0,_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.makeStyles)({\n  root: {\n    display: \"flex\",\n    flexDirection: \"column\",\n    rowGap: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.spacingVerticalM,\n    padding: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.spacingVerticalL,\n    borderRadius: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.borderRadiusLarge,\n    boxShadow: \"0 0 0 1px \".concat(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralStroke2),\n    backgroundColor: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralBackground1,\n    width: \"100%\",\n    boxSizing: \"border-box\"\n  },\n  header: {\n    display: \"flex\",\n    alignItems: \"center\",\n    justifyContent: \"space-between\",\n    columnGap: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.spacingHorizontalS,\n    rowGap: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.spacingVerticalXS,\n    flexWrap: \"wrap\"\n  },\n  titleGroup: {\n    display: \"flex\",\n    alignItems: \"center\",\n    columnGap: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.spacingHorizontalSNudge\n  },\n  badges: {\n    display: \"flex\",\n    alignItems: \"center\",\n    columnGap: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.spacingHorizontalXS,\n    flexWrap: \"wrap\"\n  },\n  brandDot: {\n    width: \"8px\",\n    height: \"8px\",\n    borderRadius: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.borderRadiusCircular,\n    backgroundColor: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorBrandBackground\n  },\n  block: {\n    display: \"flex\",\n    flexDirection: \"column\",\n    rowGap: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.spacingVerticalXXS\n  },\n  label: {\n    color: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralForeground3,\n    textTransform: \"uppercase\",\n    letterSpacing: \"0.3px\"\n  },\n  placeholder: {\n    color: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralForeground3,\n    fontStyle: \"italic\"\n  }\n});\nvar sentimentColor = label => {\n  switch ((label !== null && label !== void 0 ? label : \"\").toLowerCase()) {\n    case \"calm\":\n      return \"success\";\n    case \"concerned\":\n      return \"warning\";\n    case \"frustrated\":\n      return \"severe\";\n    case \"critical\":\n      return \"danger\";\n    default:\n      return \"informative\";\n  }\n};\nvar AiAssessmentCard = props => {\n  var styles = useStyles();\n  var summary = props.summary,\n    problem = props.problem,\n    businessImpact = props.businessImpact,\n    missingInfo = props.missingInfo,\n    reasoning = props.reasoning,\n    sentimentLabel = props.sentimentLabel,\n    confidence = props.confidence,\n    multiIntent = props.multiIntent,\n    confidenceThreshold = props.confidenceThreshold;\n  var hasContent = [summary, problem, businessImpact, missingInfo].some(v => typeof v === \"string\" && v.length > 0);\n  var confidencePct = confidence === null || confidence === undefined ? undefined : Math.round(confidence * 100);\n  var lowConfidence = confidence !== null && confidence !== undefined && confidence < confidenceThreshold;\n  var needsReview = lowConfidence || multiIntent === true;\n  var renderField = (label, value) => value ? (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.block,\n    key: label\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    size: 200,\n    weight: \"semibold\",\n    className: styles.label\n  }, label), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    size: 300\n  }, value))) : null;\n  return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.root\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.header\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.titleGroup\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"span\", {\n    className: styles.brandDot,\n    \"aria-hidden\": \"true\"\n  }), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    size: 400,\n    weight: \"semibold\"\n  }, \"AI assessment\")), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.badges\n  }, confidencePct !== undefined && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Badge, {\n    appearance: \"tint\",\n    color: lowConfidence ? \"warning\" : \"success\"\n  }, \"Confidence \".concat(confidencePct, \"%\"))), sentimentLabel && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Badge, {\n    appearance: \"tint\",\n    color: sentimentColor(sentimentLabel)\n  }, sentimentLabel)))), needsReview && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.MessageBar, {\n    intent: \"warning\"\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.MessageBarBody, null, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.MessageBarTitle, null, \"Manual review required\"), multiIntent ? \" Multiple intents detected — review and split this case.\" : \" Low confidence (\".concat(confidencePct !== null && confidencePct !== void 0 ? confidencePct : 0, \"%) \\u2014 verify the categorization before assigning.\")))), !hasContent && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    size: 300,\n    className: styles.placeholder\n  }, \"Awaiting AI processing\\u2026\")), summary && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    size: 400,\n    weight: \"semibold\"\n  }, summary)), renderField(\"Problem\", problem), renderField(\"Business impact\", businessImpact), renderField(\"Missing info\", missingInfo), reasoning && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(react__WEBPACK_IMPORTED_MODULE_0__.Fragment, null, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Divider, null), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Accordion, {\n    collapsible: true\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.AccordionItem, {\n    value: \"reasoning\"\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.AccordionHeader, null, \"AI reasoning (audit trail)\"), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.AccordionPanel, null, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    size: 200\n  }, reasoning)))))));\n};\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./AiAssessment/components/AiAssessmentCard.tsx?\n}");

/***/ },

/***/ "./AiAssessment/index.ts"
/*!*******************************!*\
  !*** ./AiAssessment/index.ts ***!
  \*******************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   AiAssessment: () => (/* binding */ AiAssessment)\n/* harmony export */ });\n/* harmony import */ var _components_AiAssessmentCard__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! ./components/AiAssessmentCard */ \"./AiAssessment/components/AiAssessmentCard.tsx\");\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__ = __webpack_require__(/*! @fluentui/react-components */ \"@fluentui/react-components\");\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1___default = /*#__PURE__*/__webpack_require__.n(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__);\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_2__ = __webpack_require__(/*! react */ \"react\");\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_2___default = /*#__PURE__*/__webpack_require__.n(react__WEBPACK_IMPORTED_MODULE_2__);\n\n\n\nclass AiAssessment {\n  constructor() {\n    // Empty\n  }\n  init(context, notifyOutputChanged) {\n    this.notifyOutputChanged = notifyOutputChanged;\n  }\n  updateView(context) {\n    var _a, _b, _c, _d, _e, _f, _g, _h;\n    var p = context.parameters;\n    var props = {\n      summary: (_a = p.aiSummary.raw) !== null && _a !== void 0 ? _a : undefined,\n      problem: (_b = p.aiProblem.raw) !== null && _b !== void 0 ? _b : undefined,\n      businessImpact: (_c = p.aiBusinessImpact.raw) !== null && _c !== void 0 ? _c : undefined,\n      missingInfo: (_d = p.aiMissingInfo.raw) !== null && _d !== void 0 ? _d : undefined,\n      reasoning: (_e = p.aiReasoning.raw) !== null && _e !== void 0 ? _e : undefined,\n      sentimentLabel: this.getOptionLabel(p.aiSentiment),\n      // tavu_AIConfidenceScore is stored 0-100 (whole percentage, consistent\n      // with the lead/meeting plugins). The card works on a 0-1 scale (it\n      // multiplies by 100 for display and compares against a 0-1 threshold),\n      // so normalize here at the boundary — otherwise 90 renders as \"9000%\"\n      // and the low-confidence review gate never fires.\n      confidence: p.aiConfidenceScore.raw === null || p.aiConfidenceScore.raw === undefined ? null : p.aiConfidenceScore.raw / 100,\n      multiIntent: p.multiIntentDetected.raw === true,\n      confidenceThreshold: (_f = p.confidenceThreshold.raw) !== null && _f !== void 0 ? _f : 0.85\n    };\n    // Use the host's Fluent theme when available (model-driven app provides it,\n    // including dark mode); fall back to the light theme in the test harness.\n    var host = context;\n    var theme = (_h = (_g = host.fluentDesignLanguage) === null || _g === void 0 ? void 0 : _g.tokenTheme) !== null && _h !== void 0 ? _h : _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.webLightTheme;\n    return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_2__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.FluentProvider, {\n      theme,\n      style: {\n        width: \"100%\"\n      }\n    }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_2__.createElement(_components_AiAssessmentCard__WEBPACK_IMPORTED_MODULE_0__.AiAssessmentCard, props));\n  }\n  /**\n   * Resolve the display label for a bound OptionSet (choice) property.\n   */\n  getOptionLabel(param) {\n    var _a, _b;\n    var raw = param === null || param === void 0 ? void 0 : param.raw;\n    if (raw === null || raw === undefined) {\n      return undefined;\n    }\n    var meta = param.attributes;\n    var options = (_a = meta === null || meta === void 0 ? void 0 : meta.Options) !== null && _a !== void 0 ? _a : [];\n    return (_b = options.find(o => o.Value === raw)) === null || _b === void 0 ? void 0 : _b.Label;\n  }\n  getOutputs() {\n    return {};\n  }\n  destroy() {\n    // No cleanup required.\n  }\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./AiAssessment/index.ts?\n}");

/***/ },

/***/ "@fluentui/react-components"
/*!************************************!*\
  !*** external "FluentUIReactv940" ***!
  \************************************/
(module) {

module.exports = FluentUIReactv940;

/***/ },

/***/ "react"
/*!***************************!*\
  !*** external "Reactv16" ***!
  \***************************/
(module) {

module.exports = Reactv16;

/***/ }

/******/ 	});
/************************************************************************/
/******/ 	// The module cache
/******/ 	var __webpack_module_cache__ = {};
/******/ 	
/******/ 	// The require function
/******/ 	function __webpack_require__(moduleId) {
/******/ 		// Check if module is in cache
/******/ 		var cachedModule = __webpack_module_cache__[moduleId];
/******/ 		if (cachedModule !== undefined) {
/******/ 			return cachedModule.exports;
/******/ 		}
/******/ 		// Create a new module (and put it into the cache)
/******/ 		var module = __webpack_module_cache__[moduleId] = {
/******/ 			// no module.id needed
/******/ 			// no module.loaded needed
/******/ 			exports: {}
/******/ 		};
/******/ 	
/******/ 		// Execute the module function
/******/ 		if (!(moduleId in __webpack_modules__)) {
/******/ 			delete __webpack_module_cache__[moduleId];
/******/ 			var e = new Error("Cannot find module '" + moduleId + "'");
/******/ 			e.code = 'MODULE_NOT_FOUND';
/******/ 			throw e;
/******/ 		}
/******/ 		__webpack_modules__[moduleId](module, module.exports, __webpack_require__);
/******/ 	
/******/ 		// Return the exports of the module
/******/ 		return module.exports;
/******/ 	}
/******/ 	
/************************************************************************/
/******/ 	/* webpack/runtime/compat get default export */
/******/ 	(() => {
/******/ 		// getDefaultExport function for compatibility with non-harmony modules
/******/ 		__webpack_require__.n = (module) => {
/******/ 			var getter = module && module.__esModule ?
/******/ 				() => (module['default']) :
/******/ 				() => (module);
/******/ 			__webpack_require__.d(getter, { a: getter });
/******/ 			return getter;
/******/ 		};
/******/ 	})();
/******/ 	
/******/ 	/* webpack/runtime/define property getters */
/******/ 	(() => {
/******/ 		// define getter functions for harmony exports
/******/ 		__webpack_require__.d = (exports, definition) => {
/******/ 			for(var key in definition) {
/******/ 				if(__webpack_require__.o(definition, key) && !__webpack_require__.o(exports, key)) {
/******/ 					Object.defineProperty(exports, key, { enumerable: true, get: definition[key] });
/******/ 				}
/******/ 			}
/******/ 		};
/******/ 	})();
/******/ 	
/******/ 	/* webpack/runtime/hasOwnProperty shorthand */
/******/ 	(() => {
/******/ 		__webpack_require__.o = (obj, prop) => (Object.prototype.hasOwnProperty.call(obj, prop))
/******/ 	})();
/******/ 	
/******/ 	/* webpack/runtime/make namespace object */
/******/ 	(() => {
/******/ 		// define __esModule on exports
/******/ 		__webpack_require__.r = (exports) => {
/******/ 			if(typeof Symbol !== 'undefined' && Symbol.toStringTag) {
/******/ 				Object.defineProperty(exports, Symbol.toStringTag, { value: 'Module' });
/******/ 			}
/******/ 			Object.defineProperty(exports, '__esModule', { value: true });
/******/ 		};
/******/ 	})();
/******/ 	
/************************************************************************/
/******/ 	
/******/ 	// startup
/******/ 	// Load entry module and return exports
/******/ 	// This entry module can't be inlined because the eval devtool is used.
/******/ 	var __webpack_exports__ = __webpack_require__("./AiAssessment/index.ts");
/******/ 	pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad = __webpack_exports__;
/******/ 	
/******/ })()
;
if (window.ComponentFramework && window.ComponentFramework.registerControl) {
	ComponentFramework.registerControl('OpenTavu.Controls.AiAssessment', pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad.AiAssessment);
} else {
	var OpenTavu = OpenTavu || {};
	OpenTavu.Controls = OpenTavu.Controls || {};
	OpenTavu.Controls.AiAssessment = pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad.AiAssessment;
	pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad = undefined;
}