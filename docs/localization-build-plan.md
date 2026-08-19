# OpenTavu Localization (i18n) — Build Plan

Status: Draft
Owner: Gustavo González Villani
Scope of this document: how OpenTavu supports Spanish alongside English across every user-facing surface, and the phased plan to implement it.

---

## 1. Decisions (fixed)

1. **English is the base language; Spanish is added on top — not a fork.** English stays the base language of the environment, the public repository, and the NIW evidence trail. Spanish (generic **LCID 3082**, `es`) is provisioned as an *additional* language so it becomes a per-user selection. There is one solution, one codebase, two language layers.
2. **Per-user, not a global "Spanish mode."** Each user sees the UI in their personal Dataverse language. An English-speaking reviewer and a Spanish-speaking consultant work in the same environment at the same time.
3. **Only user-facing strings are localized. Code stays English, always.** Identifiers, schema names (`tavu_*`), comments, commit messages, and developer documentation remain in English with no exception. Localization touches display labels and user-visible text only.
4. **Target locale: Spanish generic (3082).** One Spanish language pack covering LATAM including Colombia. If a country-specific variant (e.g. es-MX 2058) is ever needed, the architecture below already supports adding it as one more locale.

### In scope

Three surfaces, in priority order:

- **A. Dataverse metadata** — entity, field, option-set, view, form, and sitemap labels.
- **B. PCF controls** — the four React/Fluent controls.
- **C. Web resources** — the form JavaScript and HTML shown to the user.

### Out of scope (deferred, but recommended next)

- **D. AI output language.** Today the AI output language is governed by prompt string literals in C# (gateway `Functions/*.cs`, `Ai/`, and `core/src/_Shared/AI`), and at least one prompt relies on *"write in the same language as the context provided"* — which is fragile and is the likely cause of the summary/discovery language issue already observed. This effort does **not** cover it, by decision. It is documented here as the recommended follow-up because it fixes a real bug and is a clean NIW methodology point (deterministic control of model behavior rather than prompt luck). See §7.

---

## 2. Why this is the right architecture

Dataverse is multilingual by design. Provisioning a language does not duplicate anything: every localizable label already carries a per-language slot, and the platform picks the right one from the user's language setting automatically. PCF and web resources plug into the same user-language signal. So the "best way" is to lean on the platform's native localization on every layer instead of inventing a parallel translation mechanism or maintaining a second Spanish build.

The only real work is: (1) provide the Spanish values, and (2) stop hardcoding English in the two code layers (PCF and web resources) so they can read the right value at runtime.

---

## 3. Prerequisite — put the solution under source control (unpack)

Today `core/src/Solution` is empty on disk: the solution lives only in the environment. That means metadata translations cannot be versioned, diffed, or reviewed. Before starting, adopt a solution export/unpack workflow so the localized labels become reviewable text.

Steps:

- `pac solution export --name <SolutionName> --managed false --path ./Solution.zip`
- `pac solution unpack --zipfile ./Solution.zip --folder core/src/Solution --packagetype Unmanaged`
- Commit the unpacked tree. After this, `LocalizedLabels` for each entity/attribute/option set appear inside `customizations.xml` and are diffable in a pull request.

NIW note: this also gives reproducibility — anyone can reconstruct the exact localized configuration from source, which is a methodological-rigor point for the evidence file.

---

## 4. Surface A — Dataverse metadata (largest coverage, do first)

Mechanism: provision the language, then use the platform's translation export/import round-trip.

1. **Provision Spanish** in the target environment: *Settings → Languages →* enable Spanish (3082). This installs the base UI translations for the platform itself (ribbon, system messages) for free.
2. **Export Translations** from the solution. This produces `CrmTranslations.xml` (an Excel workbook) with one column per enabled language and a blank Spanish column for every custom label — entities, fields, option-set values, views, forms, charts, dashboards, and the sitemap of the model-driven app.
3. **Translate the Spanish column only.** Use the glossary in §8 for consistency. Leave English untouched.
4. **Import Translations** back into the solution, then publish.
5. **Re-export the solution and commit** so the new `LocalizedLabels` land in `core/src/Solution` (per §3).

What this covers without touching a single line of code: all field labels, all option-set choices (e.g. sentiment values, case status), all view and form names, the navigation/sitemap, and the app display name.

Acceptance: a user whose Dataverse language is Spanish sees every form field, view, choice, and menu item in Spanish; a user in English sees English. No records or data are affected — only labels.

---

## 5. Surface B — PCF controls (resx + getString)

The four controls under `core/src/Controls/` currently hardcode English in their `.tsx` (e.g. in `Ctrl.Case.AiAssessment`: "AI assessment", "Confidence", "Manual review required", "Problem", "Business impact", "Missing info", "AI reasoning (audit trail)", "Awaiting AI processing…"). None declare a `<resx>` resource.

Controls to localize:

- `Ctrl.Case.AiAssessment`
- `Ctrl.Case.CaseConversation`
- `Ctrl.Case.SlaCountdown`
- `Ctrl.Meeting.AiSummary`

Per control:

1. Create `strings/<Control>.1033.resx` (English) and `strings/<Control>.3082.resx` (Spanish), with one key per user-facing string (`aiAssessment_title`, `badge_confidence`, `review_required_title`, `field_problem`, …).
2. Declare both in `ControlManifest.Input.xml` inside `<resources>`:
   ```xml
   <resx path="strings/AiAssessment.1033.resx" version="1.0.0" />
   <resx path="strings/AiAssessment.3082.resx" version="1.0.0" />
   ```
3. Replace every literal in the `.tsx` with `context.resources.getString("key")`. Pass the resolved strings down as props (or expose the `resources` object to the component). Values that interpolate (e.g. `Confidence ${pct}%`) become a template key like `badge_confidence` = `"Confidence {0}%"` filled at render time.
4. Also localize the manifest `display-name-key` / `description-key` values via the same resx keys so the control's own labels follow the user language.

PCF resolves the correct resx from the user's UI language automatically — no manual language detection in the component.

Note on code discipline (decision 3): only the *string values* move to resx. Keys, component names, and comments stay English.

Acceptance: with the user set to Spanish, each control renders its labels, badges, and messages in Spanish; with English, unchanged from today.

---

## 6. Surface C — Web resources (JS + HTML)

These are what the user actually reads on the classic forms, so they must be localized (code stays English per decision 3 — only the visible strings change).

Files: the 10 `tavu_*_form.js` scripts and 3 HTML web resources under `core/src/WebResources/` (`tavu_companyprofile_open.html`, `tavu_systemsettings_open.html`, `tavu_teamssyncwizard.html`).

Recommended mechanism — a shared strings module keyed by user language:

1. Add one web resource `tavu_strings.js` exporting a table:
   ```js
   // tavu_strings.js — user-facing text only; keys and code stay English.
   var Tavu = Tavu || {};
   Tavu.i18n = (function () {
     var S = {
       1033: { proposal_sent_ok: "Proposal sent to client.", /* … */ },
       3082: { proposal_sent_ok: "Propuesta enviada al cliente.", /* … */ }
     };
     function lcid() {
       return Xrm.Utility.getGlobalContext().userSettings.languageId;
     }
     return {
       t: function (key) {
         var l = lcid();
         return (S[l] && S[l][key]) || S[1033][key] || key; // fall back to English
       }
     };
   })();
   ```
2. In each form script, replace hardcoded strings with `Tavu.i18n.t("key")`. Add `tavu_strings.js` as a form library dependency before the form scripts.
3. For the 3 HTML web resources, either localize with the same table via a small inline script that swaps text on load by `languageId`, or (cleaner) use RESX web resources with `context.getResourceString`. The strings-table approach keeps all three surfaces on one mechanism and one glossary.

Acceptance: notifications, dialog text, and button labels raised by form scripts and the HTML pages appear in the user's language; English is the guaranteed fallback for any missing key.

---

## 7. Deferred — AI output language (recommended follow-up)

Not in this effort's scope, documented so it is not forgotten.

Problem: output language is set by English prompt literals and by *"same language as the context"* heuristics, which drift. Recommended fix: make **output language an explicit parameter** derived from the user/record culture and threaded into every prompt centrally (gateway + `core/src/_Shared/AI`), e.g. append `"Respond in Spanish (es)."` built from a single resolver rather than per-call ad-hoc text. Doing it once at the AI layer makes every module (categorization, lead triage, meeting summary, proposal email) inherit correct behavior, and removes the fragile heuristic. This is a small, high-value change and a clean NIW methodology point; schedule it right after Surface A.

---

## 8. Translation governance — glossary / termbase

Consistency across the three surfaces requires one canonical EN→ES term list. Maintain it in-repo (e.g. `core/docs/glossary-es.md`) and use it for metadata, resx, and the strings table alike. Seed entries:

| English | Español (es) |
|---|---|
| Lead | Prospecto |
| Opportunity | Oportunidad |
| Account | Cuenta |
| Contact | Contacto |
| Proposal | Propuesta |
| Case | Caso |
| Meeting | Reunión |
| Sentiment | Sentimiento |
| Confidence | Confianza |
| Manual review required | Requiere revisión manual |
| Business impact | Impacto en el negocio |
| Missing info | Información faltante |

One reviewer signs off on new terms before they are merged, to keep the vocabulary stable across releases (and quotable as consistent in the evidence file).

---

## 9. Phased plan (summary)

| Phase | Work | Files / surface | Verification |
|---|---|---|---|
| 0 | Unpack solution; provision Spanish (3082) | `core/src/Solution`, environment | Solution builds and imports; Spanish selectable per user |
| 1 | Metadata translations (export → translate → import) | `CrmTranslations.xml` → `customizations.xml` | Spanish user sees all labels/choices/views/sitemap in Spanish |
| 2 | PCF resx + getString | 4 controls under `core/src/Controls` | Controls render Spanish per user language |
| 3 | Web resources strings module | `core/src/WebResources` (10 JS + 3 HTML) | Form messages/dialogs in Spanish, English fallback |
| 4 (deferred) | AI output language parameter | gateway + `core/src/_Shared/AI` | AI output matches user/record language deterministically |

Recommended order rationale: Phase 1 gives the most coverage for the least effort and no code risk; Phases 2–3 are the bounded code work; Phase 4 is deferred by decision but is the natural next step because it fixes a live bug.

---

## 10. Verification checklist (all phases)

- Create two test users, one English (1033) and one Spanish (3082). Every check runs as both.
- Metadata: open each main form, each view, each option-set field, and the app navigation — confirm labels switch with the user.
- PCF: load each control on its form as both users; confirm titles, badges, messages, and empty states are translated and interpolation is correct.
- Web resources: trigger each notification/dialog path in the form scripts and open each HTML web resource as both users.
- Fallback: temporarily remove one Spanish key and confirm it falls back to English rather than showing the raw key or breaking.
- No data or schema names changed: diff `customizations.xml` to confirm only `LocalizedLabels` were added, never renamed schema/logical names.
