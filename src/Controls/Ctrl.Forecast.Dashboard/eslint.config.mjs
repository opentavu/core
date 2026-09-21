import eslintjs from "@eslint/js";
import microsoftPowerApps from "@microsoft/eslint-plugin-power-apps";
import pluginPromise from "eslint-plugin-promise";
import globals from "globals";
import typescriptEslint from "typescript-eslint";

// This control is an ECharts imperative dataset control (no React). It deliberately
// uses `any` for ECharts option objects, so type-checked linting and the React plugin
// used by the other controls are intentionally omitted here.
/** @type {import('eslint').Linter.Config[]} */
export default [
  {
    ignores: ["**/generated", "out/**", "node_modules/**", "obj/**", "bin/**"],
  },
  eslintjs.configs.recommended,
  ...typescriptEslint.configs.recommended,
  pluginPromise.configs["flat/recommended"],
  {
    plugins: {
      "@microsoft/power-apps": microsoftPowerApps,
    },
    languageOptions: {
      globals: {
        ...globals.browser,
        ComponentFramework: true,
      },
      parserOptions: {
        ecmaVersion: 2020,
        sourceType: "module",
      },
    },
    rules: {
      "@typescript-eslint/no-explicit-any": "off",
      "@typescript-eslint/no-unused-vars": "off",
    },
  },
];
