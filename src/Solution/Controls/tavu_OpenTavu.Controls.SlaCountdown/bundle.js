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

/***/ "./SlaCountdown/components/SlaCountdownBar.tsx"
/*!*****************************************************!*\
  !*** ./SlaCountdown/components/SlaCountdownBar.tsx ***!
  \*****************************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   SlaCountdownBar: () => (/* binding */ SlaCountdownBar)\n/* harmony export */ });\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! react */ \"react\");\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0___default = /*#__PURE__*/__webpack_require__.n(react__WEBPACK_IMPORTED_MODULE_0__);\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__ = __webpack_require__(/*! @fluentui/react-components */ \"@fluentui/react-components\");\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1___default = /*#__PURE__*/__webpack_require__.n(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__);\nfunction _slicedToArray(r, e) { return _arrayWithHoles(r) || _iterableToArrayLimit(r, e) || _unsupportedIterableToArray(r, e) || _nonIterableRest(); }\nfunction _nonIterableRest() { throw new TypeError(\"Invalid attempt to destructure non-iterable instance.\\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.\"); }\nfunction _unsupportedIterableToArray(r, a) { if (r) { if (\"string\" == typeof r) return _arrayLikeToArray(r, a); var t = {}.toString.call(r).slice(8, -1); return \"Object\" === t && r.constructor && (t = r.constructor.name), \"Map\" === t || \"Set\" === t ? Array.from(r) : \"Arguments\" === t || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(t) ? _arrayLikeToArray(r, a) : void 0; } }\nfunction _arrayLikeToArray(r, a) { (null == a || a > r.length) && (a = r.length); for (var e = 0, n = Array(a); e < a; e++) n[e] = r[e]; return n; }\nfunction _iterableToArrayLimit(r, l) { var t = null == r ? null : \"undefined\" != typeof Symbol && r[Symbol.iterator] || r[\"@@iterator\"]; if (null != t) { var e, n, i, u, a = [], f = !0, o = !1; try { if (i = (t = t.call(r)).next, 0 === l) { if (Object(t) !== t) return; f = !1; } else for (; !(f = (e = i.call(t)).done) && (a.push(e.value), a.length !== l); f = !0); } catch (r) { o = !0, n = r; } finally { try { if (!f && null != t.return && (u = t.return(), Object(u) !== u)) return; } finally { if (o) throw n; } } return a; } }\nfunction _arrayWithHoles(r) { if (Array.isArray(r)) return r; }\n\n\nvar useStyles = (0,_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.makeStyles)({\n  root: {\n    display: \"flex\",\n    flexDirection: \"column\",\n    rowGap: \"6px\",\n    paddingTop: \"4px\",\n    paddingBottom: \"4px\",\n    fontFamily: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.fontFamilyBase\n  },\n  row: {\n    display: \"flex\",\n    justifyContent: \"space-between\",\n    alignItems: \"center\"\n  },\n  label: {\n    fontSize: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.fontSizeBase200,\n    color: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralForeground3\n  },\n  remaining: {\n    fontSize: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.fontSizeBase300,\n    fontWeight: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.fontWeightSemibold\n  },\n  track: {\n    height: \"6px\",\n    borderRadius: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.borderRadiusCircular,\n    backgroundColor: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralBackground4,\n    overflow: \"hidden\"\n  },\n  fill: {\n    height: \"6px\",\n    borderRadius: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.borderRadiusCircular\n  },\n  caption: {\n    fontSize: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.fontSizeBase200,\n    color: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralForeground4\n  },\n  badge: {\n    alignSelf: \"flex-start\"\n  }\n});\nfunction formatDuration(ms) {\n  var totalMin = Math.floor(Math.abs(ms) / 60000);\n  var days = Math.floor(totalMin / 1440);\n  var hours = Math.floor(totalMin % 1440 / 60);\n  var mins = totalMin % 60;\n  if (days > 0) return days + \"d \" + hours + \"h\";\n  if (hours > 0) return hours + \"h \" + mins + \"m\";\n  return mins + \"m\";\n}\nvar SlaCountdownBar = props => {\n  var _a, _b, _c;\n  var styles = useStyles();\n  var strings = props.strings;\n  var _React$useState = react__WEBPACK_IMPORTED_MODULE_0__.useState(Date.now()),\n    _React$useState2 = _slicedToArray(_React$useState, 2),\n    now = _React$useState2[0],\n    setNow = _React$useState2[1];\n  // Re-render every 30s so the countdown stays live without any server call.\n  react__WEBPACK_IMPORTED_MODULE_0__.useEffect(() => {\n    var id = setInterval(() => setNow(Date.now()), 30000);\n    return () => clearInterval(id);\n  }, []);\n  if (!props.targetDate) {\n    return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"span\", {\n      className: styles.caption\n    }, strings.noTarget);\n  }\n  var target = props.targetDate.getTime();\n  var created = props.createdOn ? props.createdOn.getTime() : undefined;\n  var remainingMs = target - now;\n  var overdue = remainingMs <= 0;\n  // Elapsed fraction for the bar; needs createdOn, otherwise show it full.\n  var fraction = 1;\n  if (created !== undefined && target > created) {\n    fraction = (now - created) / (target - created);\n  }\n  fraction = Math.max(0, Math.min(1, fraction));\n  // Terminal/paused states take precedence over the live countdown. Met = the case was resolved within\n  // (or the SLA closed as) target, so the clock is done — never show \"Overdue\" red. Paused = frozen.\n  var isMet = ((_a = props.statusLabel) !== null && _a !== void 0 ? _a : \"\").trim().toLowerCase() === \"met\";\n  var isPaused = ((_b = props.statusLabel) !== null && _b !== void 0 ? _b : \"\").trim().toLowerCase() === \"paused\";\n  // Color: met -> green; paused -> neutral; overdue -> red; >=80% elapsed -> amber; else green.\n  var color;\n  if (isMet) {\n    color = _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorPaletteGreenForeground1;\n  } else if (isPaused) {\n    color = _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralForeground3;\n  } else if (overdue) {\n    color = _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorPaletteRedForeground1;\n  } else if (fraction >= 0.8) {\n    color = _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorPaletteDarkOrangeForeground1;\n  } else {\n    color = _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorPaletteGreenForeground1;\n  }\n  // Derive the pill from the LIVE countdown so it never lags behind the bar (fixes the\n  // \"still Warning while overdue until refresh\" issue). Terminal state (Met) comes from\n  // the stored status; the gateway remains the authoritative writer of tavu_slastatus.\n  var badgeColor = isPaused ? \"informative\" : isMet ? \"success\" : overdue ? \"danger\" : fraction >= 0.8 ? \"warning\" : \"success\";\n  var badgeLabel = isPaused ? strings.badgePaused : isMet ? props.statusLabel : overdue ? strings.badgeBreached : fraction >= 0.8 ? strings.badgeWarning : strings.badgeOnTrack;\n  var remainingText = isMet ? strings.remainingMet : isPaused ? strings.remainingPaused : overdue ? strings.overdue.replace(\"{0}\", formatDuration(remainingMs)) : strings.remaining.replace(\"{0}\", formatDuration(remainingMs));\n  return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.root\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.row\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"span\", {\n    className: styles.label\n  }, (_c = props.label) !== null && _c !== void 0 ? _c : strings.resolution), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"span\", {\n    className: styles.remaining,\n    style: {\n      color\n    }\n  }, remainingText)), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.track\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.fill,\n    style: {\n      width: Math.round(fraction * 100) + \"%\",\n      backgroundColor: color\n    }\n  })), props.statusLabel ? (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Badge, {\n    className: styles.badge,\n    appearance: \"tint\",\n    color: badgeColor\n  }, badgeLabel)) : null);\n};\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./SlaCountdown/components/SlaCountdownBar.tsx?\n}");

/***/ },

/***/ "./SlaCountdown/index.ts"
/*!*******************************!*\
  !*** ./SlaCountdown/index.ts ***!
  \*******************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   SlaCountdown: () => (/* binding */ SlaCountdown)\n/* harmony export */ });\n/* harmony import */ var _components_SlaCountdownBar__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! ./components/SlaCountdownBar */ \"./SlaCountdown/components/SlaCountdownBar.tsx\");\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__ = __webpack_require__(/*! @fluentui/react-components */ \"@fluentui/react-components\");\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1___default = /*#__PURE__*/__webpack_require__.n(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__);\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_2__ = __webpack_require__(/*! react */ \"react\");\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_2___default = /*#__PURE__*/__webpack_require__.n(react__WEBPACK_IMPORTED_MODULE_2__);\n\n\n\nclass SlaCountdown {\n  constructor() {\n    // Empty\n  }\n  init(context, notifyOutputChanged) {\n    this.notifyOutputChanged = notifyOutputChanged;\n  }\n  updateView(context) {\n    var _a, _b, _c, _d, _e;\n    var p = context.parameters;\n    // Localized UI strings — resolved from the resx bundle for the user's language.\n    var strings = {\n      noTarget: context.resources.getString(\"noTarget\"),\n      resolution: context.resources.getString(\"resolution\"),\n      badgePaused: context.resources.getString(\"badgePaused\"),\n      badgeBreached: context.resources.getString(\"badgeBreached\"),\n      badgeWarning: context.resources.getString(\"badgeWarning\"),\n      badgeOnTrack: context.resources.getString(\"badgeOnTrack\"),\n      remainingMet: context.resources.getString(\"remainingMet\"),\n      remainingPaused: context.resources.getString(\"remainingPaused\"),\n      overdue: context.resources.getString(\"overdue\"),\n      remaining: context.resources.getString(\"remaining\")\n    };\n    var props = {\n      targetDate: (_a = p.targetDate.raw) !== null && _a !== void 0 ? _a : undefined,\n      createdOn: (_b = p.createdOn.raw) !== null && _b !== void 0 ? _b : undefined,\n      statusLabel: this.getOptionLabel(p.slaStatus),\n      label: (_c = p.label.raw) !== null && _c !== void 0 ? _c : undefined,\n      strings: strings\n    };\n    // Use the host's Fluent theme (model-driven app provides it, incl. dark mode);\n    // fall back to the light theme in the test harness.\n    var host = context;\n    var theme = (_e = (_d = host.fluentDesignLanguage) === null || _d === void 0 ? void 0 : _d.tokenTheme) !== null && _e !== void 0 ? _e : _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.webLightTheme;\n    return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_2__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.FluentProvider, {\n      theme\n    }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_2__.createElement(_components_SlaCountdownBar__WEBPACK_IMPORTED_MODULE_0__.SlaCountdownBar, props));\n  }\n  getOptionLabel(param) {\n    var _a, _b;\n    var raw = param === null || param === void 0 ? void 0 : param.raw;\n    if (raw === null || raw === undefined) {\n      return undefined;\n    }\n    var meta = param.attributes;\n    var options = (_a = meta === null || meta === void 0 ? void 0 : meta.Options) !== null && _a !== void 0 ? _a : [];\n    return (_b = options.find(o => o.Value === raw)) === null || _b === void 0 ? void 0 : _b.Label;\n  }\n  getOutputs() {\n    return {};\n  }\n  destroy() {\n    // No cleanup required.\n  }\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./SlaCountdown/index.ts?\n}");

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
/******/ 	let __webpack_exports__ = __webpack_require__("./SlaCountdown/index.ts");
/******/ 	pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad = __webpack_exports__;
/******/ 	
/******/ })()
;
if (window.ComponentFramework && window.ComponentFramework.registerControl) {
	ComponentFramework.registerControl('OpenTavu.Controls.SlaCountdown', pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad.SlaCountdown);
} else {
	var OpenTavu = OpenTavu || {};
	OpenTavu.Controls = OpenTavu.Controls || {};
	OpenTavu.Controls.SlaCountdown = pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad.SlaCountdown;
	pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad = undefined;
}