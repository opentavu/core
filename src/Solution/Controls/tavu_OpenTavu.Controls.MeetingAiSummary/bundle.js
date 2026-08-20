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

/***/ "./MeetingAiSummary/components/MeetingAiSummaryCard.tsx"
/*!**************************************************************!*\
  !*** ./MeetingAiSummary/components/MeetingAiSummaryCard.tsx ***!
  \**************************************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   MeetingAiSummaryCard: () => (/* binding */ MeetingAiSummaryCard)\n/* harmony export */ });\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! react */ \"react\");\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0___default = /*#__PURE__*/__webpack_require__.n(react__WEBPACK_IMPORTED_MODULE_0__);\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__ = __webpack_require__(/*! @fluentui/react-components */ \"@fluentui/react-components\");\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1___default = /*#__PURE__*/__webpack_require__.n(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__);\n\n\nvar useStyles = (0,_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.makeStyles)({\n  root: {\n    display: \"flex\",\n    flexDirection: \"column\",\n    rowGap: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.spacingVerticalM,\n    padding: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.spacingVerticalL,\n    borderRadius: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.borderRadiusLarge,\n    boxShadow: \"0 0 0 1px \".concat(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralStroke2),\n    backgroundColor: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralBackground1,\n    width: \"100%\",\n    boxSizing: \"border-box\"\n  },\n  header: {\n    display: \"flex\",\n    alignItems: \"center\",\n    justifyContent: \"space-between\",\n    columnGap: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.spacingHorizontalS,\n    rowGap: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.spacingVerticalXS,\n    flexWrap: \"wrap\"\n  },\n  titleGroup: {\n    display: \"flex\",\n    alignItems: \"center\",\n    columnGap: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.spacingHorizontalSNudge\n  },\n  badges: {\n    display: \"flex\",\n    alignItems: \"center\",\n    columnGap: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.spacingHorizontalXS,\n    flexWrap: \"wrap\"\n  },\n  brandDot: {\n    width: \"8px\",\n    height: \"8px\",\n    borderRadius: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.borderRadiusCircular,\n    backgroundColor: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorBrandBackground\n  },\n  block: {\n    display: \"flex\",\n    flexDirection: \"column\",\n    rowGap: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.spacingVerticalXXS\n  },\n  label: {\n    color: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralForeground3,\n    textTransform: \"uppercase\",\n    letterSpacing: \"0.3px\"\n  },\n  placeholder: {\n    color: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralForeground3,\n    fontStyle: \"italic\"\n  }\n});\nvar MeetingAiSummaryCard = props => {\n  var styles = useStyles();\n  var summary = props.summary,\n    discoveryExtract = props.discoveryExtract,\n    confidence = props.confidence,\n    confidenceThreshold = props.confidenceThreshold,\n    strings = props.strings;\n  var hasContent = [summary, discoveryExtract].some(v => typeof v === \"string\" && v.length > 0);\n  var confidencePct = confidence === null || confidence === undefined ? undefined : Math.round(confidence);\n  var lowConfidence = confidence !== null && confidence !== undefined && confidence < confidenceThreshold;\n  var renderField = (label, value) => value ? (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.block,\n    key: label\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    size: 200,\n    weight: \"semibold\",\n    className: styles.label\n  }, label), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    size: 300\n  }, value))) : null;\n  return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.root\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.header\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.titleGroup\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"span\", {\n    className: styles.brandDot,\n    \"aria-hidden\": \"true\"\n  }), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    size: 400,\n    weight: \"semibold\"\n  }, strings.title)), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.badges\n  }, confidencePct !== undefined && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Badge, {\n    appearance: \"tint\",\n    color: lowConfidence ? \"warning\" : \"success\"\n  }, strings.confidence.replace(\"{0}\", String(confidencePct)))))), lowConfidence && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.MessageBar, {\n    intent: \"warning\"\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.MessageBarBody, null, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.MessageBarTitle, null, strings.reviewBeforeAssociating), \" \" + strings.lowConfidence.replace(\"{0}\", String(confidencePct !== null && confidencePct !== void 0 ? confidencePct : 0))))), !hasContent && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    size: 300,\n    className: styles.placeholder\n  }, strings.awaiting)), summary && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    size: 400,\n    weight: \"semibold\"\n  }, summary)), renderField(strings.discoveryExtract, discoveryExtract));\n};\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./MeetingAiSummary/components/MeetingAiSummaryCard.tsx?\n}");

/***/ },

/***/ "./MeetingAiSummary/index.ts"
/*!***********************************!*\
  !*** ./MeetingAiSummary/index.ts ***!
  \***********************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   MeetingAiSummary: () => (/* binding */ MeetingAiSummary)\n/* harmony export */ });\n/* harmony import */ var _components_MeetingAiSummaryCard__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! ./components/MeetingAiSummaryCard */ \"./MeetingAiSummary/components/MeetingAiSummaryCard.tsx\");\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__ = __webpack_require__(/*! @fluentui/react-components */ \"@fluentui/react-components\");\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1___default = /*#__PURE__*/__webpack_require__.n(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__);\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_2__ = __webpack_require__(/*! react */ \"react\");\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_2___default = /*#__PURE__*/__webpack_require__.n(react__WEBPACK_IMPORTED_MODULE_2__);\n\n\n\nclass MeetingAiSummary {\n  constructor() {\n    // Empty\n  }\n  init(context, notifyOutputChanged) {\n    this.notifyOutputChanged = notifyOutputChanged;\n  }\n  updateView(context) {\n    var _a, _b, _c, _d, _e;\n    var p = context.parameters;\n    // Localized UI strings — resolved from the resx bundle for the user's language.\n    var strings = {\n      title: context.resources.getString(\"title\"),\n      confidence: context.resources.getString(\"confidence\"),\n      reviewBeforeAssociating: context.resources.getString(\"reviewBeforeAssociating\"),\n      lowConfidence: context.resources.getString(\"lowConfidence\"),\n      awaiting: context.resources.getString(\"awaiting\"),\n      discoveryExtract: context.resources.getString(\"discoveryExtract\")\n    };\n    var props = {\n      summary: (_a = p.aiSummary.raw) !== null && _a !== void 0 ? _a : undefined,\n      discoveryExtract: (_b = p.discoveryExtract.raw) !== null && _b !== void 0 ? _b : undefined,\n      // Stored as a whole percentage (0-100), so no scaling here.\n      confidence: p.aiConfidence.raw,\n      confidenceThreshold: (_c = p.confidenceThreshold.raw) !== null && _c !== void 0 ? _c : 70,\n      strings: strings\n    };\n    // Use the host's Fluent theme when available (model-driven app provides it,\n    // including dark mode); fall back to the light theme in the test harness.\n    var host = context;\n    var theme = (_e = (_d = host.fluentDesignLanguage) === null || _d === void 0 ? void 0 : _d.tokenTheme) !== null && _e !== void 0 ? _e : _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.webLightTheme;\n    return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_2__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.FluentProvider, {\n      theme,\n      style: {\n        width: \"100%\"\n      }\n    }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_2__.createElement(_components_MeetingAiSummaryCard__WEBPACK_IMPORTED_MODULE_0__.MeetingAiSummaryCard, props));\n  }\n  getOutputs() {\n    return {};\n  }\n  destroy() {\n    // No cleanup required.\n  }\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./MeetingAiSummary/index.ts?\n}");

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
/******/ 	const __webpack_module_cache__ = {};
/******/ 	
/******/ 	// The require function
/******/ 	function __webpack_require__(moduleId) {
/******/ 		// Check if module is in cache
/******/ 		const cachedModule = __webpack_module_cache__[moduleId];
/******/ 		if (cachedModule !== undefined) {
/******/ 			return cachedModule.exports;
/******/ 		}
/******/ 		// Create a new module (and put it into the cache)
/******/ 		const module = __webpack_module_cache__[moduleId] = {
/******/ 			// no module.id needed
/******/ 			// no module.loaded needed
/******/ 			exports: {}
/******/ 		};
/******/ 	
/******/ 		// Execute the module function
/******/ 		if (!(moduleId in __webpack_modules__)) {
/******/ 			delete __webpack_module_cache__[moduleId];
/******/ 			const e = new Error("Cannot find module '" + moduleId + "'");
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
/******/ 			const getter = module && module.__esModule ?
/******/ 				() => (module['default']) :
/******/ 				() => (module);
/******/ 			__webpack_require__.d(getter, { a: getter });
/******/ 			return getter;
/******/ 		};
/******/ 	})();
/******/ 	
/******/ 	/* webpack/runtime/define property getters */
/******/ 	(() => {
/******/ 		// define getter/value functions for harmony exports
/******/ 		__webpack_require__.d = (exports, definition) => {
/******/ 			if(Array.isArray(definition)) {
/******/ 				var i = 0;
/******/ 				while(i < definition.length) {
/******/ 					var key = definition[i++];
/******/ 					var binding = definition[i++];
/******/ 					if(!__webpack_require__.o(exports, key)) {
/******/ 						if(binding === 0) {
/******/ 							Object.defineProperty(exports, key, { enumerable: true, value: definition[i++] });
/******/ 						} else {
/******/ 							Object.defineProperty(exports, key, { enumerable: true, get: binding });
/******/ 						}
/******/ 					} else if(binding === 0) { i++; }
/******/ 				}
/******/ 			} else {
/******/ 				for(var key in definition) {
/******/ 					if(__webpack_require__.o(definition, key) && !__webpack_require__.o(exports, key)) {
/******/ 						Object.defineProperty(exports, key, { enumerable: true, get: definition[key] });
/******/ 					}
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
/******/ 			if(Symbol.toStringTag) {
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
/******/ 	let __webpack_exports__ = __webpack_require__("./MeetingAiSummary/index.ts");
/******/ 	pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad = __webpack_exports__;
/******/ 	
/******/ })()
;
if (window.ComponentFramework && window.ComponentFramework.registerControl) {
	ComponentFramework.registerControl('OpenTavu.Controls.MeetingAiSummary', pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad.MeetingAiSummary);
} else {
	var OpenTavu = OpenTavu || {};
	OpenTavu.Controls = OpenTavu.Controls || {};
	OpenTavu.Controls.MeetingAiSummary = pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad.MeetingAiSummary;
	pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad = undefined;
}