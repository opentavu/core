# CLAUDE.md

Guide for AI coding agents (Claude Code, GitHub Copilot, others) and for humans working in this repository. Read it before changing anything. If something here disagrees with the code, the code wins; fix this file in the same change.

## What this repository is

OpenTavu is an open-source (MIT) AI-first CRM **framework** for small and mid-sized professional-services firms, built on Microsoft Power Platform (Dataverse, model-driven app, C# plugins and Custom APIs, PCF controls, Power Automate). Call it a "framework", never a "platform".

The AI gateway (Azure Functions, .NET 8) lives in a separate public repo: `github.com/opentavu/gateway`. Nothing from the gateway goes into this repo.

## Repository map

```
src/
  Solution/               Unpacked Dataverse solution "OpenTavu" (OpenTavu Core). Schema source of truth.
  Solution.Integrations/  Unpacked solution "OpenTavuIntegrations" (optional flows: email, Forms, OneDrive).
  Plugins/                One project per plugin assembly: Pl.<Short>.<Action>
  _Shared/Common/         PluginBase.cs, LocalPluginContext.cs (plugin base contract), OpenTavu.snk
  _Shared/AI/             Provider-agnostic AI layer (IAIProvider, Azure OpenAI, OpenAI, Gateway)
  Controls/               PCF controls: Ctrl.<Area>.<Name>
  WebResources/js/        Form scripts: tavu_<table>_form.js
templates/                dotnet new templates (opentavu-plugin)
tools/                    sync-solution.ps1, release-smoke.ps1
docs/                     Living product documentation (see "Docs map")
```

## Schema: where the truth is

- Tables, columns, choices, forms, views, Custom APIs, plugin steps and flows are authored in the **development environment** and mirrored into `src/Solution` and `src/Solution.Integrations` with `tools/sync-solution.ps1` (Solution Packager format, package type Both).
- **Before using a table or column name, look it up in `src/Solution/Entities/<Table>/Entity.xml` or `src/Solution/OptionSets/`.** Do not guess logical names and do not trust names in older docs. When docs and `src/Solution` disagree, `src/Solution` wins and the doc gets fixed.
- Do not hand-edit unpacked XML expecting it to reach Dataverse. Make the change in the dev environment, then run the sync and review the diff.
- If `src/Solution` looks older than the feature you are working on, say so and ask for a sync before writing code against it.

## Naming conventions (never invent a pattern)

| Artifact | Pattern | Examples |
|---|---|---|
| Cloud flows | `Fl.<Area>.<Purpose>` (PascalCase) | `Fl.Lead.WebToLead`, `Fl.Forecast.SnapshotDaily` |
| Plugins | `Pl.<Short>.<Action>` | `Pl.Lead.Triage`, `Pl.Case.Categorize`, `Pl.Setup.Initialize` |
| Custom APIs, tables, columns, choices | `tavu_<name>` | `tavu_PromoteLead`, `tavu_lead`, `tavu_aitaskconfiguration` |
| PCF controls | `Ctrl.<Area>.<Name>` | `Ctrl.Case.SlaCountdown`, `Ctrl.Forecast.Dashboard` |
| Form scripts | `tavu_<table>_form.js` | `tavu_opportunity_form.js` |

Before naming anything, list the existing siblings in `src/Solution` (or `src/Plugins`) and mirror the closest one.

## Plugins

Create new plugin projects with the template, from `src/Plugins`, always with `-n`:

```powershell
cd src\Plugins
dotnet new opentavu-plugin -n Pl.<Short>.<Action> --entityName <tavu_table> --actionName <Action> --entityShortName <Short>
```

Without `-n` the template dumps files into the current folder with broken `_Shared` paths. Full details: `templates/README.md`.

Contract (read `src/_Shared/Common/PluginBase.cs` and `LocalPluginContext.cs` before writing plugin code; do not guess signatures):

- Inherit `OpenTavu.Dataverse.Common.PluginBase`; business logic only in `ExecuteInternal(LocalPluginContext localContext)`. `PluginBase` handles service extraction, errors and `MaxDepth` (anti-recursion).
- `UserService` for anything that must respect the caller's privileges. `SystemService` only when a bypass is justified (reading configuration tables, writing derived or audit fields). A wrong choice causes silent failures for low-privilege users and imports.
- Trace with `localContext.Trace(...)`, never `TracingService.Trace` directly.
- Schema constants at the top of the class. Router pattern: private handlers that decide for themselves whether the current change concerns them.
- Pre-Operation by default (modify Target in place: no extra Update, no recursion).
- Group by functional category: if new logic shares a trigger with an existing plugin, add a handler there instead of a new assembly.
- Plugins target **net462** (Dataverse sandbox). Libraries that need modern .NET (PDF rendering, long-running orchestration, agent loops) belong in the gateway, not in a plugin.

## Design rules (apply to every change)

1. **Pain-point driven.** Every field, table or feature must map to a documented pain point (VISION.md, section 3). Classificatory metadata and "every CRM has it" are reasons to cut, not to add.
2. **AI-first, not AI-bolted-on.** AI proposes and decides with confidence thresholds; humans review exceptions. A manual MVP feature must have an explicit path to its AI version in the roadmap.
3. **Simplicity.** Fewer fields than Dynamics out of the box. The burden of proof is on adding a field.
4. **Configuration over code.** Anything that varies per firm (stages, case types, tiers, SLAs, business lines, prompts, models) lives in a `tavu_*` configuration table, never in `statuscode` values or code. Company Profile (`tavu_companyprofile`) says who the firm is; System Settings (`tavu_systemsettings`) says how OpenTavu behaves.

## AI rules

- Every AI task is a row in `tavu_aitaskconfiguration` (model, temperature, confidence threshold, max tokens, prompt). Never hardcode a prompt or a model name in code.
- Call AI only through the provider-agnostic layer in `src/_Shared/AI` (direct mode or gateway mode). No keys in code, in solution files or in environment variable default values.
- AI-generated text is written in the language of its source input (transcript, email, case).
- Every AI call records which grounding sources were available and which were used.
- Autonomy vocabulary L0 to L3: everything live today is an L1 "AI task". Do not call anything an "agent" unless it meets L2.

## Setup routine and seed data

- A managed solution carries no rows. Reference data is created after import by the Custom API `tavu_InitializeConfiguration` (`Pl.Setup.Initialize`; modes `full`, `periods`, `diagnose`; outputs `Summary`, `Report` (JSON), `Complete`), exposed as the "Verify and complete configuration" button in System Settings. Design: `docs/setup-initialize-design.md`.
- Seed file: `src/Plugins/Pl.Setup.Initialize/Seed/opentavu-seed.json` (embedded in the assembly). Bump its version on every change.
- Seed rules: match existing rows by code or name, never by GUID; only fill empty fields; never overwrite what the firm changed; never recreate a row the firm deactivated; tables that firms rename get a `tavu_code`.
- Demo data is never part of the solution or the seed.

## Writing style

- Never use em dashes (the long dash) anywhere: code strings, comments, prompts, docs, commit messages. Use commas, periods, colons or parentheses.
- Public docs are in English. Spanish UI strings follow the localization plan (LCID 3082).
- Present capabilities in commercial lifecycle order (sales first, then service), not by module number.

## Definition of done

A feature (plugin, Custom API, AI task, flow, table) is done only when, in the same change:

1. The schema change is synced into `src/Solution` (or `src/Solution.Integrations`) and committed with the code.
2. The living docs in `docs/` describe the implemented behavior.
3. The public capability surfaces are updated where the capability changed: `README.md` (AI modules and project status), `VISION.md` (functional coverage) and the site (`../site/index.html`, separate folder).

## What agents may and may not do

- May: read and edit code and docs, run `dotnet build`, run read-only `pac` commands, run `tools/sync-solution.ps1` against the dev environment, propose diffs.
- Must not, without explicit human approval in the conversation: `git push`, create or delete repos or releases, register plugins or change Dataverse metadata, run `pac solution import` against any environment other than a disposable test environment, or commit anything containing a secret, a tenant URL default, or a key.

## Release checklist

1. Commit all code; run `tools/sync-solution.ps1` and review the diff.
2. Check custom tables for an orphaned system `transactioncurrencyid` still referenced by a view (breaks managed import on a clean org).
3. Export managed Core and Integrations; run `tools/release-smoke.ps1` against a disposable environment. It must end with 0 errors.
4. Update release notes, `docs/installation.md` asset names, README and VISION.

## Docs map

- `docs/architecture.md`: data model, plugins, integration points.
- `docs/sales-model.md`, `docs/opportunity-close-dialog.md`, `docs/proposal-lifecycle.md`: sales cycle.
- `docs/service-model.md`: case lifecycle and SLA.
- `docs/forecasting-*.md`: forecasting phase 1 (deterministic, no AI).
- `docs/module3-*.md`, `docs/teams-sync-wizard-design.md`, `docs/web-to-lead.md`: lead triage and activity capture.
- `docs/setup-initialize-design.md`: setup routine and diagnostics.
- `docs/installation.md`, `docs/configuration.md`: what a new installer follows.
- `docs/view-definitions.md`, `docs/localization-build-plan.md`, `docs/roadmap-phase2.md`.

Private, maintainer-only notes go in `CLAUDE.local.md` (gitignored).
