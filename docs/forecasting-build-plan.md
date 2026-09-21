# AI-Assisted Forecasting, Phase 1: Deterministic Goal / Quota & Attainment. Build Plan & Handoff Context

> **Purpose of this file.** Self-contained context to build Phase 1 of OpenTavu's
> "AI-Assisted Forecasting" module: the deterministic goal/quota and attainment layer.
> It captures the locked design decisions (do not re-litigate), the exact schema to create
> in the unpacked solution (`core/src/Solution`), the native rollup and calculated-column
> definitions, the pre-operation plugin change, the Power Automate snapshot flow, the
> PCF/ECharts dashboard, seed data and per-firm configuration, and a test checklist.
> Read the "Repo files to read first" section (§2) before creating any object.
> **Do not invent schema names or option values; verify them in the environment** before use,
> exactly as the Module 3 build followed.
>
> **No em dashes** in any object label, description, seed record, plugin string, flow text,
> or dashboard label (project rule). Use commas, periods, colons, or parentheses.

---

## 0. Where this fits

OpenTavu is an AI-first CRM accelerator on **Microsoft Dataverse / Power Platform** for
professional-services SMBs, on a ~US$20/user Power Apps Premium base, provider-agnostic AI
(Azure OpenAI default). Today the sales model tracks a pipeline (`tavu_opportunity` +
configurable `tavu_salesstage`), but it has **no way to forecast against sales goals**: no
target, no attainment, no gap-to-goal, no coverage, no pacing. Phase 1 closes exactly that
gap with **deterministic math only** (native Dataverse rollups + formula columns +
Power Automate). No AI, no historical data required, ships without waiting for the later AI
layer.

Design source of truth, read in this order:

1. `Research/Sales_Model/Forecasting-Design-Proposal-OpenTavu.md` (v0.2), the forecasting design.
2. `Research/Sales_Model/Analytics-Design-Proposal-OpenTavu.md`, the analytics/visualization decision.
3. `core/docs/sales-model.md` (v2.2+), the authoritative technical spec: §6.2/§6.3 opportunity
   fields, §6.3bis stage/forecast-category + goal/quota layer, §6.4 System Settings.
4. `core/VISION.md` (v1.2), Section 8 "AI-Assisted Forecasting" roadmap entry.

**Design mindset (applies to every decision):** pain-point-driven, AI-first (AI replaces the
human judgement step, arithmetic stays deterministic and auditable), simplicity as advantage,
configuration-over-code, mixed-table doctrine (custom `tavu_*`, never Restricted Dynamics
tables). If a proposed field or step does not map to a forecasting need, cut it.

---

## 1. Locked decisions (do not re-litigate)

From `Forecasting-Design-Proposal-OpenTavu.md` §12 and `Analytics-Design-Proposal-OpenTavu.md`,
already written into `sales-model.md` v2.2 and `VISION.md` v1.2:

- **Lean custom tables** `tavu_salesperiod`, `tavu_salestarget`, `tavu_forecastsnapshot`,
  `tavu_reportingcache`. **Never** the native Dynamics `Goal` / `msdyn_forecast*` tables
  (Restricted, require a Dynamics 365 Sales license, break the ~US$20 envelope).
- **All attainment math via native Dataverse rollup fields** for the atomic user-scope target
  (deterministic, no plugin doing arithmetic): Closed Won / Committed / Best Case sums on
  `tavu_salestarget`. Plus a **Manager Adjusted Amount** manual override that does not touch
  rep opportunities.
- **Forecast is category / commit-based** (deterministic sum by category), not a
  probability-weighted expected-value sum.
- **Forecast category on the opportunity** (`tavu_forecastcategory` +
  `tavu_forecastcategoryismanual`), rep-settable, auto-defaulted from the stage's forecast
  category via the same pre-op plugin pattern as `tavu_probability`. Stage to category is
  many-stages to one-of-four-categories, per-firm configurable.
- **Pipeline coverage derived from each firm's win rate**; professional-services seed default
  **~2x** (not the SaaS 3 to 4x).
- **`tavu_forecastsnapshot` is Phase 1**: weekly Power Automate. It is the pacing history AND
  the pre-aggregation that avoids Dataverse's 50k FetchXML aggregate limit and API throttling.
- **Analytics is Power-BI-free by default**: native model-driven dashboards/charts + a
  **PCF control (Apache ECharts / Recharts)** reading pre-aggregated snapshot/cache tables.
  AI risk alert surfaces as a view icon/flag + in-app notification + Power Automate Teams
  Adaptive Card. OSS upgrade path (only when a client outgrows this): Azure Synapse Link to
  ADLS Gen2 to DuckDB/Serverless SQL to managed Metabase. Power BI only if the client already
  owns M365 E5.
- **AI layer (deferred, not in this build):** Empirical-Bayes probability calibration +
  Batch-API deal-risk detection over Module 3 activity. Arithmetic stays deterministic.
- Module name: **"AI-Assisted Forecasting"** (capacity is light PSA-integration visibility only).

### 1.1 The two previously-open decisions, now resolved (Aug 21, 2026)

1. **Quota granularity: user + team + business-line from day one.** `tavu_salestarget` supports
   four scopes (User / Team / Business Line / Company) out of the box. The business-line
   dimension **reuses the existing `tavu_businessline` table** (see §3.2) plus a new
   `tavu_businessline` lookup on the opportunity, so attainment can be sliced per practice.
   Single-practice firms use the seeded default business line and never see the extra dimension
   (config-over-code).
2. **SPI 2026 source-verification pass done for the figures that enter this plan.** The
   coverage default (~2x) is verified against the SPI 2026 Professional Services Maturity
   Benchmark (see §13). Corrected numbers: industry bid win rate **45.1%**, high performers
   **56.5%**; average pipeline coverage **158%**, high performers **224%**. These bracket the
   ~2x seed (1 / 0.451 is about 2.2x). The Empirical-Bayes / arXiv citations were **not**
   verified here because they belong to the deferred Phase 3 AI layer, not this deterministic
   build; leave them flagged "verify before NIW-facing use."

---

## 2. Repo files to read FIRST (mirror these; do not guess)

All paths under `C:\Code\OpenTavu\core\`.

| File | Why |
|---|---|
| `docs/sales-model.md` §6.3bis | The probability-defaulting pre-op pattern this build mirrors for forecast-category defaulting; the stage to forecast-category mapping; the goal/quota paragraph. |
| `src/Plugins/Pl.Opportunity.*` (the assembly hosting probability defaulting, e.g. `Pl.Opportunity.LifecycleTracker`) | **The template to mirror** for forecast-category defaulting and the target-stamp write. Verify the exact assembly/type name in the environment (§6). |
| `src/_Shared/Common/PluginBase.cs`, `LocalPluginContext.cs` | Base contract. Logic in `ExecuteInternal(LocalPluginContext)`; trace via `localContext.Trace(...)`; `MaxDepth`=1 anti-recursion. `SystemService` vs `UserService` rules. |
| `src/WebResources/js/tavu_opportunity_form.js` (or the opportunity form JS) + the probability "Reset to Stage Default" ribbon button | Reference for the forecast-category OnChange handler and the "Reset Forecast Category to Stage Default" button. |
| `src/PCF/` (the case-form AI panel PCF) | The existing PCF the dashboard control mirrors (build tooling, Dataverse WebAPI reads, Entra ID security inheritance). |
| Existing scheduled flows `Fl.Lead.BufferDailyMaintenance`, `Fl.*` | Mirror for `Fl.Forecast.SnapshotWeekly` structure (recurrence, list-rows, upsert). |
| `docs/module3-lead-triage-build-plan.md` / `-test-checklist.md` | The build-plan and test-checklist **style** to match. |

**Naming (non-negotiable, project memory):** cloud flows `Fl.<Area>.<Purpose>`, plugins
`Pl.<Short>.<Action>`, everything else `tavu_<Name>`. Verify against existing siblings before
creating. Group plugin logic by lifecycle concern (one assembly per concern), not one plugin
per field.

---

## 3. Schema, the new and changed objects

Create all objects in the unpacked solution under `core/src/Solution`. Currency columns carry
the native `transactioncurrencyid`. All tables enable Auditing.

### 3.0 Prescribed option-set values (assign these exact values, so plugin constants are known)

The publisher uses the `576600xxx` value range. Assign these explicitly when creating each choice
so code and config agree without a later reconciliation pass.

- **`tavu_forecastcategory`** (existing GLOBAL choice): Pipeline=576600000, Best Case=576600001,
  Committed=576600002, Closed=576600003. **Add Omitted=576600004.** Bind the new opportunity
  `tavu_forecastcategory` field to this SAME global choice (the plugin copies values verbatim).
- **`tavu_scope`** (new global choice for `tavu_salestarget`): User=576600000, Team=576600001,
  Business Line=576600002, Company=576600003.
- **`tavu_periodtype`** (new global choice): Month=576600000, Quarter=576600001 (**no Year**: the
  annual is a derived sum, not a bucket). **Reuse this same global choice for
  `tavu_systemsettings.tavu_forecastperiodtype`** so the stamp compares period values directly.
- **`tavu_metrickind`** (new global choice for `tavu_goaltype`): Currency Sum=576600000,
  Record Count=576600001.
- Opportunity status (existing, do not change): Open=576600001, Won=576600005, Lost=576600006.

### 3.1 `tavu_salesperiod`, the time boundary

| Property | Value |
|---|---|
| Display name | `Sales Period` |
| Schema name | `tavu_salesperiod` |
| Primary column | `Name` (`tavu_name`), e.g. "Q3 2026", "Jul 2026" |
| Ownership | Organization |

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | Required | "Q3 2026", "Jul 2026" |
| Period Type | tavu_periodtype | Choice | Required | **Month / Quarter only** (the target-setting buckets). No Year: the annual figure is the derived sum of a fiscal year's buckets, not a settable period. Firms set targets at Month or Quarter via `tavu_systemsettings.tavu_forecastperiodtype` (default Quarter for PS; Month for high volume). |
| Fiscal Year | tavu_fiscalyear | Whole Number | Required | e.g. 2026. Groups the year's buckets for the derived annual view. |
| Start Date | tavu_startdate | Date Only | Required | |
| End Date | tavu_enddate | Date Only | Required | |

**Active / inactive** uses the native `statecode`/`statuscode` (Active/Inactive), NOT a custom
`tavu_isactive` column. There is no Year period row and no self-parent lookup; the annual is
computed by grouping on `tavu_fiscalyear`.

**Keys:** alternate key on `tavu_name`. No self relationship (annual groups on `tavu_fiscalyear`).

### 3.2 `tavu_businessline`, the practice / business-line dimension (ALREADY EXISTS, reuse)

**Do NOT create this table.** Verified in `core/src/Solution/Entities/tavu_BusinessLine`: it
already exists (display "Business Line" / "Línea de negocio"), used by the service/categorization
side, and is the right firm-wide practice dimension to reuse for forecasting. Its columns:

| Display Name | Schema | Type | Notes |
|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | e.g. "Advisory", "Delivery" |
| Code | tavu_code | Single Line | firm code |
| Description | tavu_description | Multiple Lines | |
| Sort Order | tavu_sortorder | Whole Number | acts as Display Order |
| AI Categorization Hint | tavu_aicategorizationhint | Multiple Lines | used by Module 1 |
| statecode / statuscode | (native) | State | Active / Inactive = the "Is Active" role |

No Color column exists; the dashboard can color business lines from a fixed palette instead.
Forecasting adds only the lookups that point AT this table (opportunity, sales target, system
settings default). If the firm has no explicit business lines, seed a single **"General Practice"**
row and set it as the System Settings default.

### 3.2bis `tavu_goaltype`, the goal-type / forecast-type dimension (schema from day one; Phase 1 seeds Revenue only)

A lean configuration table that makes the goal metric extensible (HubSpot-style forecast types
and goal templates). **Phase 1 builds and seeds only the `Revenue` type**; the table exists now
so Phase-2 activity templates (Meetings Booked, Proposals Sent, Deals Created, Calls) are pure
configuration plus one flow branch, with no schema rework. See the Phase-2 roadmap (§14).

| Property | Value |
|---|---|
| Display name | `Goal Type` |
| Schema name | `tavu_goaltype` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | Organization |

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | Required | "Revenue" (Phase 1). Phase 2: "Meetings Booked", "Proposals Sent", "Deals Created", "Calls Made", "Forecastable Revenue" |
| Metric Kind | tavu_metrickind | Choice | Required | Currency Sum / Record Count. Revenue = Currency Sum. |
| Source Entity | tavu_sourceentity | Text | Required | Logical name of the records measured. Revenue = `tavu_opportunity`. |
| Date Property | tavu_dateproperty | Text | Required | Which date bounds the period ("when the amount is realized"). Revenue = close date (Won: `tavu_actualclosedate`, Open: `tavu_estimatedclosedate`). |
| Amount Property | tavu_amountproperty | Text | Optional | Currency field summed (Currency Sum only). Revenue = `tavu_actualrevenue` / `tavu_estimatedrevenue`. Extensible to ARR/ACV. |
| Uses Forecast Category | tavu_usesforecastcategory | Yes/No | Required | Revenue = Yes (Committed/Best Case/Omitted). Activity counts = No. |

Active/inactive uses the native `statecode`/`statuscode`, not a custom column.

**Keys:** alternate key on `tavu_name`. **Coverage and pacing are revenue-specific**; count-based
goal types show attainment and pacing only (no coverage gauge).

### 3.3 `tavu_salestarget`, the goal / quota (one per subject, per period, per optional business line)

| Property | Value |
|---|---|
| Display name | `Sales Target` |
| Schema name | `tavu_salestarget` |
| Primary column | `Name` (`tavu_name`), e.g. "Ana Perez, Q3 2026" or "Advisory, Q3 2026" |
| Ownership | **User or Team** (so Dataverse row-level + manager-hierarchy security governs who sees which target) |

**Identity / dimension columns:**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | Required | Human label; can be auto-composed |
| Goal Type | tavu_goaltype | Lookup to tavu_goaltype | Required | Phase 1: always "Revenue". Present from day one so Phase-2 activity goals reuse this table. |
| Scope | tavu_scope | Choice | Required | **User / Team / Business Line / Company** |
| Sales Rep | tavu_user | Lookup to SystemUser | Optional | Set for User scope |
| Team | tavu_team | Lookup to Team | Optional | Set for Team scope |
| Business Line | tavu_businessline | Lookup to tavu_businessline | Optional | Set for Business Line scope, or to make a per-rep-per-practice quota on a User-scope row |
| Sales Period | tavu_salesperiod | Lookup to tavu_salesperiod | Required | |
| Parent Target | tavu_parenttarget | Lookup to tavu_salestarget | Optional | Hierarchy: User to Team to Company. Used by the aggregation flow (see §5). |
| Target Key | tavu_targetkey | Single Line | **Optional (auto-stamped, never hand-typed)** | Machine dedup key `goaltype|scope|user|team|businessline|period` (empty slot when a dimension is null), stamped by the pre-op plugin **`Pl.SalesTarget.KeyStamp`** on save; the **alternate key** that prevents duplicate targets and enables upsert. Keep it off the form. |

**Target and manual-override columns:**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Target Amount | tavu_targetamount | Currency | Required | The quota |
| Stretch Target | tavu_stretchtarget | Currency | Optional | |
| Manager Adjusted Amount | tavu_manageradjustedamount | Currency | Optional | Manual hedge (e.g. exclude a whale deal) without touching opportunities |
| Manager Adjustment Note | tavu_manageradjustednote | Multiple Lines | Optional | Why the manager overrode the number |

**Rollup columns (native Dataverse, over the stamped opportunities; see §4). User scope only.**

| Display Name | Schema | Type |
|---|---|---|
| Closed Won Amount | tavu_closedwonamount | Currency (Rollup) |
| Committed Amount | tavu_committedamount | Currency (Rollup) |
| Best Case Amount | tavu_bestcaseamount | Currency (Rollup) |
| Open Pipeline Amount | tavu_openpipelineamount | Currency (Rollup) |
| Open Opportunity Count | tavu_opencount | Whole Number (Rollup) (optional) |

**Formula columns (deterministic; see §5).**

| Display Name | Schema | Type | Formula (deterministic) |
|---|---|---|---|
| Forecast Amount | tavu_forecastamount | Currency (Formula) | ClosedWon + Committed |
| Forecast (Final) | tavu_forecastfinal | Currency (Formula) | If ManagerAdjusted set: ManagerAdjusted, else ClosedWon + Committed |
| Upside Amount | tavu_upsideamount | Currency (Formula) | ClosedWon + Committed + BestCase |
| Gap to Goal | tavu_gaptogoal | Currency (Formula) | Target minus ClosedWon |
| Attainment % | tavu_attainmentpct | Decimal (Formula) | ClosedWon / Target * 100 |
| Forecast Attainment % | tavu_forecastattainmentpct | Decimal (Formula) | (ClosedWon + Committed) / Target * 100 |
| Coverage Ratio | tavu_coverageratio | Decimal (Formula) | OpenPipeline / GapToGoal (see §5 for the goal-met case) |

> For **Team / Business Line / Company** scope rows the native rollups stay 0 (no opportunity is
> stamped directly to them). Their attained/committed/forecast values are produced by the
> aggregation flow into `tavu_reportingcache` (§5, §7), because Dataverse forbids a rollup over
> another rollup. Keep the target amount and manager adjustment on the row; present attainment
> for those scopes from the cache and the dashboard.

**Keys:** alternate key on `tavu_targetkey`. Relationships: N:1 to `tavu_salesperiod` (required),
N:1 to SystemUser (`tavu_user`), N:1 to Team (`tavu_team`), N:1 to `tavu_businessline`, self N:1
(`tavu_parenttarget`), and **1:N to `tavu_opportunity`** (the stamp relationship the rollups use,
schema `tavu_salestarget_opportunity`).

### 3.4 `tavu_forecastsnapshot`, history and pacing

One row per target per weekly snapshot. The pacing time series AND the pre-aggregation that
sidesteps Dataverse's 50k FetchXML aggregate limit.

| Property | Value |
|---|---|
| Display name | `Forecast Snapshot` |
| Schema name | `tavu_forecastsnapshot` |
| Primary column | `Name` (`tavu_name`), auto-composed "Q3 2026, Ana Perez, 2026-08-21" |
| Ownership | **User or Team** (flow sets owner = the target's subject, so security matches the target) |

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | Required | Auto-composed by the flow |
| Sales Target | tavu_salestarget | Lookup to tavu_salestarget | Required | |
| Sales Period | tavu_salesperiod | Lookup to tavu_salesperiod | Required | Denormalized for filtering |
| Scope | tavu_scope | Choice | Required | Denormalized from the target |
| Snapshot Date | tavu_snapshotdate | Date and Time | Required | |
| Period Elapsed % | tavu_periodelapsedpct | Decimal | Optional | % of the period elapsed at snapshot |
| Closed Won Amount | tavu_closedwonamount | Currency | Optional | Captured |
| Committed Amount | tavu_committedamount | Currency | Optional | Captured |
| Best Case Amount | tavu_bestcaseamount | Currency | Optional | Captured |
| Open Pipeline Amount | tavu_openpipelineamount | Currency | Optional | Captured |
| Target Amount | tavu_targetamount | Currency | Optional | Captured (targets can change mid-period) |
| Forecast Amount | tavu_forecastamount | Currency | Optional | Captured Won + Committed (or Final) |
| Attainment % | tavu_attainmentpct | Decimal | Optional | Captured |
| Gap to Goal | tavu_gaptogoal | Currency | Optional | Captured |
| Coverage Ratio | tavu_coverageratio | Decimal | Optional | Captured |
| Expected Pace Amount | tavu_expectedpaceamount | Currency | Optional | Time-adjusted "on-track" target for this snapshot date (drives the pacing line) |
| Snapshot Key | tavu_snapshotkey | Single Line | Required (system) | `targetid|yyyy-MM-dd`, the alternate key (idempotent weekly write) |

**Keys:** alternate key on `tavu_snapshotkey`. Relationships: N:1 to `tavu_salestarget` (required),
N:1 to `tavu_salesperiod`.

### 3.5 `tavu_reportingcache`, current-state pre-aggregation for the dashboard

The single fast-read table the PCF hits (a few dozen rows), so the dashboard never runs a raw
opportunity aggregate. Holds current-state metrics for **all** scopes, including the aggregated
Team / Business Line / Company numbers that have no native rollup.

| Property | Value |
|---|---|
| Display name | `Reporting Cache` |
| Schema name | `tavu_reportingcache` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | **User or Team** (flow sets owner = subject; Company/Business-Line rows owned by a "Sales Leadership" team, visible to managers by security role) |

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | Required | Auto-composed |
| Cache Key | tavu_cachekey | Single Line | Required (system) | `scope|subject|period|businessline`, alternate key (upsert) |
| Scope | tavu_scope | Choice | Required | User / Team / Business Line / Company |
| Sales Period | tavu_salesperiod | Lookup | Required | |
| Sales Rep | tavu_user | Lookup to SystemUser | Optional | Subject |
| Team | tavu_team | Lookup to Team | Optional | Subject |
| Business Line | tavu_businessline | Lookup to tavu_businessline | Optional | Subject / slice |
| Sales Target | tavu_salestarget | Lookup to tavu_salestarget | Optional | Link back where one exists |
| Target Amount | tavu_targetamount | Currency | Optional | |
| Closed Won Amount | tavu_closedwonamount | Currency | Optional | |
| Committed Amount | tavu_committedamount | Currency | Optional | |
| Best Case Amount | tavu_bestcaseamount | Currency | Optional | |
| Open Pipeline Amount | tavu_openpipelineamount | Currency | Optional | |
| Forecast Amount | tavu_forecastamount | Currency | Optional | |
| Forecast (Final) | tavu_forecastfinal | Currency | Optional | |
| Attainment % | tavu_attainmentpct | Decimal | Optional | |
| Forecast Attainment % | tavu_forecastattainmentpct | Decimal | Optional | |
| Gap to Goal | tavu_gaptogoal | Currency | Optional | |
| Coverage Ratio | tavu_coverageratio | Decimal | Optional | |
| Win Rate | tavu_winrate | Decimal | Optional | Trailing Won / (Won + Lost), diagnostic and coverage-target input |
| Computed On | tavu_computedon | Date and Time | Required | Freshness stamp |

**Keys:** alternate key on `tavu_cachekey`.

### 3.6 `tavu_opportunity`, changed and new fields

| Display Name | Schema | Type | Status | Notes |
|---|---|---|---|---|
| Forecast Category | tavu_forecastcategory | Choice (**Omitted** / Pipeline / Best Case / Committed / Closed) | **Changed** | Add the **Omitted** value (new, HubSpot-style "do not forecast"). Also add Omitted to the stage-level `tavu_salesstage.tavu_forecastcategory`. Omitted open deals are excluded from Open Pipeline, coverage, and the forecast until they advance. Defaulted from stage, rep-settable. |
| Forecast Category Is Manual | tavu_forecastcategoryismanual | Yes/No | **Exists** (sales-model v2.2) | Verify present. |
| Business Line | tavu_businessline | Lookup to tavu_businessline | **New** | Optional. Defaults from `tavu_systemsettings.tavu_defaultbusinessline` on create (pre-op). Filterable slice for per-practice attainment. |
| Sales Target | tavu_salestarget | Lookup to tavu_salestarget | **New** | Optional, **system-managed** (the stamp that enables the native rollups). Hide from sellers or place read-only on an admin tab. |

### 3.7 `tavu_systemsettings`, new configuration fields (§6.4)

| Display Name | Schema | Type | Default | Notes |
|---|---|---|---|---|
| Coverage Target | tavu_coveragetarget | Decimal | **2.0** | Per-firm healthy pipeline-coverage multiple (win-rate-derived; ~2x for PS). |
| Forecast Period Type | tavu_forecastperiodtype | Choice (Month / Quarter) | Quarter | The target-setting bucket quota entry and opportunity stamping use. **Not Year** (annual is a derived sum, too long as a tracking bucket). Quarter recommended for PS. |
| Default Business Line | tavu_defaultbusinessline | Lookup to tavu_businessline | (the seeded "General Practice") | Pre-fills `tavu_opportunity.tavu_businessline` when empty. |
| Forecast Snapshot Day | tavu_forecastsnapshotdow | Choice (Mon..Sun) | Friday | Weekly snapshot day (the flow reads this; default Friday 17:00). |

### 3.8 Relationship and key summary (create these)

- `tavu_salestarget` 1:N `tavu_opportunity` via `tavu_opportunity.tavu_salestarget`
  (name `tavu_salestarget_opportunity`). This is the relationship the rollups aggregate over.
- `tavu_businessline` 1:N `tavu_opportunity` via `tavu_opportunity.tavu_businessline`.
- `tavu_businessline` 1:N `tavu_salestarget` via `tavu_salestarget.tavu_businessline`.
- `tavu_goaltype` 1:N `tavu_salestarget` via `tavu_salestarget.tavu_goaltype`.
- `tavu_salesperiod` 1:N `tavu_salestarget`, 1:N `tavu_forecastsnapshot`, 1:N `tavu_reportingcache`.
- `tavu_salestarget` 1:N `tavu_forecastsnapshot`.
- Self relationship: `tavu_salestarget.tavu_parenttarget` (User to Team to Company hierarchy).
- Alternate keys: `tavu_salesperiod(tavu_name)`, `tavu_businessline(tavu_name)`,
  `tavu_salestarget(tavu_targetkey)`, `tavu_forecastsnapshot(tavu_snapshotkey)`,
  `tavu_reportingcache(tavu_cachekey)`.

---

## 4. Native rollup fields on `tavu_salestarget` (exact definitions)

All four aggregate over the **`tavu_salestarget_opportunity`** relationship (the stamped
opportunities). Rollup filters cannot compare a related field to the parent's dynamic period,
so the **period window is enforced by the stamp** (§6): an opportunity is stamped only to the
target whose period contains its effective close date. The rollup therefore filters on state
and category only.

| Rollup | Aggregate | Related (`tavu_opportunity`) filter |
|---|---|---|
| `tavu_closedwonamount` | SUM of `tavu_actualrevenue` | `statecode` = Won |
| `tavu_committedamount` | SUM of `tavu_estimatedrevenue` | `statecode` = Open AND `tavu_forecastcategory` = Committed |
| `tavu_bestcaseamount` | SUM of `tavu_estimatedrevenue` | `statecode` = Open AND `tavu_forecastcategory` = Best Case |
| `tavu_openpipelineamount` | SUM of `tavu_estimatedrevenue` | `statecode` = Open AND `tavu_forecastcategory` != Omitted |
| `tavu_opencount` (opt.) | COUNT | `statecode` = Open AND `tavu_forecastcategory` != Omitted |

Notes:

- Use `tavu_actualrevenue` for Won (the realized number) and `tavu_estimatedrevenue` for open
  deals. Confirm both schema names in the environment (sales-model §6.3 lists
  `tavu_estimatedrevenue` and `tavu_actualrevenue`).
- Rollups recalculate **asynchronously, hourly** by default (a system job) and on-demand via
  the recalculate icon. This is fine for a quota KPI; it is not real-time slicing (a known
  Dataverse trait, called out in the analytics design). The dashboard reads the pre-aggregated
  cache for interactive slicing, not live rollups.
- Do not filter these rollups by business line: the **stamp already routes** an opportunity to
  the single correct (owner, period, business-line) target, so the target's own rollups are
  already scoped. Per-practice rep quotas are separate User-scope rows with `tavu_businessline`
  set, each getting its own stamped opportunities.

---

## 5. Where the deterministic metrics live (formula columns) and the aggregation strategy

### 5.1 User scope: formula columns on `tavu_salestarget`

Attainment %, gap-to-goal, forecast amount, forecast attainment %, upside, and coverage are
**formula columns (Power Fx)** on `tavu_salestarget` (§3.3), referencing the row's own rollups and
`tavu_targetamount`. They are 100% deterministic; no plugin, no AI. **Use formula columns, not
classic calculated columns**: Microsoft's direction is formula columns as the successor to
calculated columns (rollups are not affected and stay, since Power Fx cannot aggregate). Exact
Power Fx is in `docs/forecasting-step-a-guide.md` §8.

- `tavu_gaptogoal` = `tavu_targetamount` - `tavu_closedwonamount`.
- `tavu_attainmentpct` = `tavu_closedwonamount` / `tavu_targetamount` * 100.
  Dataverse returns null when the divisor is 0; render as 0% / "no target set".
- `tavu_forecastamount` = `tavu_closedwonamount` + `tavu_committedamount`.
- `tavu_forecastfinal` = conditional: if `tavu_manageradjustedamount` is set, use it, else
  `tavu_forecastamount` (Dataverse calculated-column IF/ELSE).
- `tavu_forecastattainmentpct` = `tavu_forecastamount` / `tavu_targetamount` * 100.
- `tavu_upsideamount` = `tavu_closedwonamount` + `tavu_committedamount` + `tavu_bestcaseamount`.
- `tavu_coverageratio` = `tavu_openpipelineamount` / `tavu_gaptogoal`. When gap-to-goal <= 0
  (goal already met), Dataverse returns null; the dashboard shows "Goal met." A ratio-vs-target
  variant (`OpenPipeline / Target`) can be computed in the dashboard if the firm prefers it.

> **Caveat (build-safe):** a formula column that references a rollup column reflects the rollup's
> **last hourly recalculation** (not instant). If the maker refuses to reference a rollup column in
> a formula column, compute these same values in the reporting-cache flow (§7) and read them from
> `tavu_reportingcache` instead. That is already the path for the non-user scopes below, so the
> fallback is zero extra design. (Do not fall back to classic calculated columns.)

### 5.2 Team / Business Line / Company scope: the aggregation flow, not rollups

Dataverse **cannot roll up over another rollup**, so a Team target cannot natively sum its
member User targets' Closed Won. Higher scopes are therefore produced by the **aggregation step
of the Power Automate flow** (§7), which sums the child User-scope targets (a handful of rows,
well inside all limits) up the `tavu_parenttarget` hierarchy and writes the result to
`tavu_reportingcache` (current state) and `tavu_forecastsnapshot` (history). This is exactly the
pre-aggregation role the analytics design assigns to the snapshot/cache layer, so it costs
nothing new architecturally.

Freshness tiering (state it in the UI): rep numbers update ~hourly (native rollups); team,
business-line, and company numbers refresh on the flow cadence (nightly cache + weekly snapshot).

---

## 6. Pre-operation plugin change (forecast-category defaulting + target stamp)

Extend the **existing pre-operation opportunity plugin that already defaults `tavu_probability`
from the stage** (per sales-model §6.3bis; likely `Pl.Opportunity.LifecycleTracker`, verify the
exact type in the environment). Add three deterministic behaviours. If the team prefers a
dedicated assembly, scaffold `Pl.Opportunity.ForecastStamp`; extending the existing one is
preferred because behaviour (a) is the identical pattern to probability defaulting.

**(a) Forecast-category defaulting (mirror probability, §6.3bis pseudocode):**

```
on tavu_opportunity Pre-Operation Create:
    if tavu_salesstage is set AND tavu_forecastcategory is null:
        tavu_forecastcategory       = tavu_salesstage.tavu_forecastcategory
        tavu_forecastcategoryismanual = false

on tavu_opportunity Pre-Operation Update:
    if tavu_salesstage changed AND tavu_forecastcategoryismanual == false:
        tavu_forecastcategory = NEW tavu_salesstage.tavu_forecastcategory
    if tavu_forecastcategory changed by the user (not by the plugin):
        tavu_forecastcategoryismanual = true
```

Use the same "user-edited vs plugin-edited" mechanism already used for probability (the
thread-local plugin flag, or the form `OnChange` on `tavu_forecastcategory` sets
`tavu_forecastcategoryismanual = true`). Add a **"Reset Forecast Category to Stage Default"**
ribbon button beside the existing probability reset, which sets the manual flag false and
re-applies the stage default.

**(b) Business-line default on create:**

```
on tavu_opportunity Pre-Operation Create:
    if tavu_businessline is null:
        tavu_businessline = tavu_systemsettings.tavu_defaultbusinessline
```

**(c) Target stamp (enables the native rollups):**

```
on tavu_opportunity Pre-Operation Create/Update
    when ownerid, tavu_estimatedclosedate, tavu_actualclosedate, tavu_businessline,
    or statecode changed (or on create):

    effectiveDate = (statecode == Won) ? tavu_actualclosedate : tavu_estimatedclosedate
    if effectiveDate is null: clear tavu_salestarget; return
    period = tavu_salesperiod where tavu_periodtype == systemsettings.tavu_forecastperiodtype
             AND tavu_startdate <= effectiveDate <= tavu_enddate AND statecode == Active
    target = tavu_salestarget where tavu_scope == User AND tavu_user == ownerid
             AND tavu_salesperiod == period
             AND (tavu_businessline == opp.tavu_businessline
                  OR, if no per-practice row exists for this rep+period, tavu_businessline is null)
    tavu_salestarget = target   // null if none found (rep has no quota row yet)
```

Guardrails: `SystemService` (derived/system write); keep the two retrieves light; never throw
out of the save (a missing period or target is a valid "unstamped" state, not an error). A
**nightly reconciliation** in the flow (§7) re-stamps open and recently-won opportunities as a
backstop for reassignments, period-boundary crossings, and bulk imports. Do not stamp on the
`Assign` message inside the plugin; let the nightly reconciliation catch owner changes, or add a
lightweight real-time flow on ownership change if immediacy is needed.

---

## 7. Power Automate flows

> **Three distinct time axes, do not conflate them.** (1) The **snapshot** ("the photo") is taken
> **weekly**, always, and measures progress *within* a period (the pacing history). (2) The
> **target period / bucket** is **Month or Quarter**, the unit a target is set against and
> attainment is measured on. (3) The **annual figure** is the **sum** of a fiscal year's buckets, a
> derived roll-up, never a bucket you set a target on. The snapshot frequency is independent of the
> period type.

### 7.1 `Fl.Forecast.SnapshotWeekly` (Phase 1 core)

- **Trigger:** Recurrence, weekly, on `tavu_systemsettings.tavu_forecastsnapshotdow` (default
  Friday) 17:00 America/Bogota.
- **Steps:**
  1. **Reconcile stamps (backstop):** list open + last-90-days-Won opportunities; recompute and
     set `tavu_salestarget` per §6(c) where it drifted.
  2. **User-scope snapshot:** list active User-scope `tavu_salestarget` rows in active periods;
     read their native rollups (`tavu_closedwonamount`, `tavu_committedamount`,
     `tavu_bestcaseamount`, `tavu_openpipelineamount`) and formula columns; compute
     `tavu_expectedpaceamount` (time-adjusted: TargetAmount * periodElapsed%, using working-day
     fraction, not naive calendar-linear) and `tavu_periodelapsedpct`; **upsert**
     `tavu_forecastsnapshot` by `tavu_snapshotkey` (`targetid|yyyy-MM-dd`).
  3. **Aggregate higher scopes:** walking `tavu_parenttarget`, sum child User-scope captured
     values into Team, then Business Line, then Company; upsert their `tavu_forecastsnapshot`
     rows too.
  3b. **Derive the annual roll-up:** for each subject, sum the fiscal year's Month/Quarter buckets
     into an annual cache row grouped on `tavu_fiscalyear` (or, simpler, let the dashboard sum the
     fiscal year's period rows client-side). This is the "Total year" view; no target is set on a
     year (annual = sum of the period targets).
  4. **Refresh current-state cache:** upsert `tavu_reportingcache` by `tavu_cachekey` for every
     scope with the same numbers plus `tavu_winrate` (trailing Won/(Won+Lost)) and
     `tavu_computedon`; set each row's owner to its subject (user/team) for security.
- **Idempotent** by the alternate keys, so a manual re-run is safe.

### 7.2 `Fl.Forecast.CacheNightly` (recommended)

Same as steps 1, 3, 4 above (skip writing the weekly history row), on a nightly recurrence, so
Team / Business Line / Company dashboards are at most one day stale between weekly snapshots. Rep
rows already refresh ~hourly via native rollups. Optional but recommended for manager views.

---

## 8. PCF / ECharts dashboard

A single PCF control (mirror the existing case-form AI-panel PCF), **Apache ECharts** (or
Recharts), on a Custom Page / model-driven dashboard. It reads **only** `tavu_reportingcache`
(current state) and `tavu_forecastsnapshot` (pacing history), a few dozen rows, never a raw
opportunity aggregate (this is the deliberate workaround for the 50k FetchXML limit and Web API
service-protection throttling).

**Controls:** a **Scope selector** (User / Team / Business Line / Company) and a **Period
selector** drive which cache rows load. A Business-Line filter is available when the firm has
more than the seeded default line.

**Tiles / charts:**

1. **Attainment gauge/bar:** Closed Won vs Target with `tavu_attainmentpct`. Reads
   `tavu_reportingcache`.
2. **Gap-to-Goal tile:** big-number `tavu_gaptogoal` remaining. Cache.
3. **Coverage gauge:** `tavu_coverageratio` vs the firm's `tavu_coveragetarget` (~2x), with
   color zones: below target (under-covered, red), around target (healthy, green), well above
   target (over-selling beyond delivery capacity, amber warning). Cache. This is the one tile
   that actively rejects the SaaS 3 to 4x norm.
4. **Pacing line:** cumulative Closed Won vs `tavu_expectedpaceamount` over the period, from the
   weekly `tavu_forecastsnapshot` series. Snapshot.
5. **Pipeline-by-category stacked bar:** Pipeline / Best Case / Committed / Closed Won amounts
   (Pipeline = OpenPipeline - Committed - BestCase). Cache.
6. **Forecast scenario (optional):** Base (Won + Committed = `tavu_forecastamount` /
   `tavu_forecastfinal`) vs Upside (`tavu_upsideamount`) vs Target. Cache.

**Basic operational views (free baseline, no PCF):** native model-driven charts for pipeline by
stage and per-rep attainment, for firms that want zero custom surface.

**Security:** the PCF runs client-side under the user's Entra ID; Dataverse row-level security on
`tavu_reportingcache` / `tavu_forecastsnapshot` (owned by the subject user/team, per §3) governs
what each viewer sees. Reps see their own rows; managers see reports' rows via the manager
hierarchy security model; Company / Business-Line rows are owned by a "Sales Leadership" team and
exposed to managers by security role. No BI license anywhere.

**AI risk alert surface (built now, populated in Phase 3):** a Boolean/flag column plus a view
icon/color on the opportunity, an in-app notification (`SendAppNotification`), and a Power
Automate Teams Adaptive Card / email digest. No dashboard dependency, no BI license.

---

## 9. Seed data and per-firm configuration

**Seed (shipped in the managed solution):**

- `tavu_goaltype`: one row **"Revenue"** (Metric Kind = Currency Sum, Source Entity =
  `tavu_opportunity`, Date Property = `tavu_actualclosedate`, Amount Property = `tavu_actualrevenue`,
  Uses Forecast Category = Yes, Active). This is the only goal type Phase 1 builds. The Source/Date/
  Amount fields are metadata for Revenue (attainment comes from the Sales Target rollups); they
  become functional for the Phase-2 generic engine (§14).
- `tavu_businessline`: one row **"General Practice"** (Display Order 1, Active, color neutral).
  Single-practice firms need no setup; the opportunity business-line defaults to it.
- `tavu_salesperiod`: the current fiscal year as **Quarter** rows (Q1..Q4 of the current year)
  each with `tavu_fiscalyear` set (e.g. 2026). **No Year row.** The annual figure is the sum of the
  year's buckets (grouped on `tavu_fiscalyear`; the quota grid shows it as the Total column).
  Document how the admin generates future periods (a maker re-runs a "generate periods" flow or adds
  rows by hand).
- `tavu_systemsettings`: `tavu_coveragetarget` = **2.0**, `tavu_forecastperiodtype` = **Quarter**,
  `tavu_defaultbusinessline` = the seeded General Practice, `tavu_forecastsnapshotdow` = **Friday**.

**Quota entry (simple grid).** To match HubSpot's ease, ship a **quota-entry Custom Page**: rows =
reps (or teams), columns = the periods for the chosen cadence (Q1..Q4, or 12 months, or 1 year),
with a total column and a bulk "apply to all" action. Each cell writes one `tavu_salestarget`
row (upsert by `tavu_targetkey`); the annual total is the sum of the period columns. This is the
low-friction alternative to authoring target records one by one.

**Per-firm configuration points (config-over-code):**

- Cadence: **Month / Quarter / Year** (default Quarter for PS) and the period rows themselves.
- Coverage target (start at 2.0; each firm's realized win rate, surfaced as `tavu_winrate` in
  the cache, informs whether to nudge it: coverage is about 1 / win rate).
- Business lines (add practices, or stay single-line).
- Quota granularity in use: create only User targets, or User + Team, or User + Team +
  Business Line + Company, by creating the corresponding `tavu_salestarget` rows and linking
  `tavu_parenttarget`. The schema supports all; the firm chooses how many scope rows to author.
- Stage to forecast-category mapping (existing `tavu_salesstage.tavu_forecastcategory`), including
  which stages map to **Omitted** (not forecasted).
- Snapshot day of week.

---

## 10. Build order (ordered steps)

- **Step A (maker):** create the tables (`tavu_salesperiod`, `tavu_businessline`, `tavu_goaltype`,
  `tavu_salestarget`, `tavu_forecastsnapshot`, `tavu_reportingcache`), all columns, relationships,
  and alternate keys (§3). Add the new `tavu_opportunity` fields (`tavu_businessline`,
  `tavu_salestarget`) and the `tavu_systemsettings` fields (§3.6, §3.7). Add the **Omitted** value
  to `tavu_forecastcategory` (opportunity and `tavu_salesstage`); confirm
  `tavu_forecastcategory` / `tavu_forecastcategoryismanual` already exist on the opportunity. Seed
  the **Revenue** `tavu_goaltype` row (§9).
- **Step B (maker):** define the four (or five) native rollups on `tavu_salestarget` (§4) and the
  seven formula columns (§5.1). Recalculate once and sanity-check with a couple of test opps.
- **Step C (code):** extend the pre-op opportunity plugin (§6): forecast-category defaulting,
  business-line default, target stamp. Build, sign, register the added step(s) (Pre-Operation,
  Create + Update on `tavu_opportunity`, Server, Synchronous, filtered to the relevant
  attributes for Update). Add the form `OnChange` handler + "Reset Forecast Category to Stage
  Default" button (§6a).
- **Step D (no-code):** build `Fl.Forecast.SnapshotWeekly` (§7.1) and the recommended
  `Fl.Forecast.CacheNightly` (§7.2). Turn them on.
- **Step E (code):** build the PCF/ECharts dashboard (§8) reading cache + snapshot; place it on a
  Custom Page / dashboard with the scope + period selectors; wire security roles.
- **Step E2 (code, optional but recommended):** build the **quota-entry Custom Page** grid (§9)
  for low-friction target capture (rows = reps, columns = periods, bulk apply, upsert by
  `tavu_targetkey`).
- **Step F (config/seed):** load seed rows (§9), including the Revenue `tavu_goaltype`; set System
  Settings; author the firm's target rows for the chosen scopes and link `tavu_parenttarget`.
- **Step G (verify):** run the test checklist (`forecasting-test-checklist.md`).

---

## 11. Gotchas and guardrails

- **The stamp is what makes native rollups possible.** Verify in the plugin trace that a Won
  opportunity is stamped to the target whose period contains its **actual** close date, and an
  open one by its **estimated** close date; re-stamp on close. A wrong stamp silently
  mis-attributes revenue.
- **No rollup over a rollup.** Never try to make a Team target natively sum child targets. Team,
  Business Line, and Company always come from the flow into the cache/snapshot.
- **Calculated-over-rollup is hourly and unsortable.** Acceptable for KPIs; for anything
  interactive or sortable, read the cache. Fall back to flow-computed metrics if the environment
  is unreliable (§5.1).
- **Coverage is a warning, not just a target.** Over-coverage at a ~50% PS win rate means
  selling beyond delivery capacity. The gauge must flag "too high," not only "too low." Do not
  import the SaaS 3 to 4x default.
- **Security:** targets, snapshots, and cache rows are User/Team owned and owner-stamped by the
  flow so reps cannot see each other's numbers; managers see reports' via the hierarchy. Do not
  make these tables Organization-owned with global read.
- **Deterministic only in Phase 1.** No AI, no probability-weighting of the committed number.
  The forecast leadership commits to is Won + Committed (or the manager-adjusted number).
- **Idempotent flows.** Upsert by alternate key; a manual re-run must not duplicate rows.
- **Verify every option-set and status value in the environment** before baking constants into
  the plugin (the Module 3 lesson).

---

## 12. Test checklist

See the companion `forecasting-test-checklist.md` (same style as
`module3-lead-triage-test-checklist.md`). Summary of coverage: schema + rollups, plugin
defaulting + stamp, snapshot/cache flow, dashboard tiles, security isolation, seed/config, and
sign-off invariants (deterministic math, correct period attribution, no rollup-over-rollup, no
BI license, security isolation).

---

## 13. Sources and NIW evidence note

**SPI 2026 figures (verified Aug 21, 2026):** the SPI Research 2026 Professional Services
Maturity Benchmark, as reported by Rocketlane, gives an industry bid win rate of **45.1%** and a
high-performer rate of **56.5%**, with average pipeline coverage of **158%** and high-performer
coverage of **224%** relative to quarterly bookings. These verify and slightly correct the
proposal's "~48%," and they bracket the ~2x coverage seed (1 / 0.451 is about 2.2x). Source:
Rocketlane, "2026 Professional Services Maturity Benchmark"
(https://www.rocketlane.com/blogs/professional-services-maturity-index-2026), summarizing SPI
Research (https://spiresearch.com/). The Empirical-Bayes / arXiv citations remain **unverified**
and belong to the deferred Phase 3 AI layer, not this deterministic build; do not cite them in
any NIW-facing document without independent verification.

**NIW connection (product decision producing evidence).** Phase 1 is a self-contained, deployable
capability delivered under a hard economic constraint (native rollups + Power Automate + PCF,
zero incremental per-user license), with a documented rejection of a generic industry default
(SaaS 3 to 4x coverage) in favor of a segment-correct, evidence-backed value (~2x for
professional services). The legal narrative that uses this belongs to the separate EB-2 NIW
project master, not here.

---

## 14. Phase-2 roadmap (recorded here; deferred, not in the Phase-1 build)

These ideas came from reviewing HubSpot's forecast configuration (Aug 21, 2026). The Phase-1
schema is built to absorb them without rework (the `tavu_goaltype` table and the `tavu_scope` /
`tavu_businessline` dimensions exist from day one). Cross-referenced from `docs/roadmap-phase2.md`.

- **Generic goal engine (config-over-code, any table).** `tavu_goaltype` keeps its
  `tavu_sourceentity`, `tavu_dateproperty`, `tavu_amountproperty`, `tavu_metrickind` fields so a
  goal can target **any** table (not just opportunity) purely by configuration. In Phase 2 the
  snapshot/cache flow reads those fields and builds the aggregation dynamically (group by
  owner/period, filter the date property inside the period, then sum the amount property or count
  rows). Adding a goal on a new table is then a new row, no code change. (For Revenue in Phase 1
  these fields are metadata only; attainment comes from the Sales Target rollups.)
- **`Pl.GoalType.Validate` pre-op plugin** to make the free-text fields safe: on save it checks
  `tavu_sourceentity` is a real table (`RetrieveEntityRequest`, with fuzzy match + friendly
  suggestion, e.g. "opportunity" -> `tavu_opportunity`) and that `tavu_dateproperty` /
  `tavu_amountproperty` are real columns of the right type on that table (`RetrieveAttributeRequest`),
  throwing a guiding error otherwise. This is the safety mechanism that keeps the generic engine
  flexible without typo risk. Build it when the activity goals land (only developers seed goal
  types in Phase 1).
- **Activity goal templates (record-count metric)** as new `tavu_goaltype` rows, PS priority
  order: **Meetings Booked** (Module 3 meetings), **Proposals Sent** (`tavu_proposal`),
  **Deals / Opportunities Created**, **Calls / Activities Made**. Attainment is a count per
  subject per period, computed by the generic engine above, not a native rollup. These are
  leading-indicator goals for coaching reps. Coverage and pacing stay revenue-specific; activity
  goals show attainment and pacing only.
- **Forecastable Revenue** goal type (revenue filtered/grouped by forecast category).
- **Amount-property extensibility:** ARR / ACV / recurring amount for firms that sell retainers
  or subscriptions (the `tavu_goaltype.tavu_amountproperty` field already parameterizes this).
- **Date-property options** beyond close date (created-on, activity date) per goal type, which the
  activity templates need.
- **Optional forecast-submission reminders** (the HubSpot "submission schedule"): a light Power
  Automate reminder for reps to review/submit their forecast on a cadence. Deliberately **not**
  in the MVP: the automatic weekly snapshot already captures the point-in-time forecast without
  rep data entry. Add only if a firm wants the ritual.
- **AI layer (Phase 3, separate):** Empirical-Bayes probability calibration + Batch-API deal-risk
  detection over Module 3 activity (already in the design; unchanged by the above).

---

## Document control

| Version | Date | Author | Notes |
|---|---|---|---|
| 1.0 | Aug 21, 2026 | Gustavo Gonzalez Villani (with Claude) | Initial Phase-1 build plan for the deterministic goal/quota and attainment layer. Full schema for `tavu_salesperiod`, `tavu_serviceline`, `tavu_salestarget`, `tavu_forecastsnapshot`, `tavu_reportingcache`, plus the new `tavu_opportunity` and `tavu_systemsettings` fields. Native rollup definitions and deterministic calculated columns; the rollup-over-rollup limitation and the flow-based aggregation for Team/Service-Line/Company scopes. Pre-op plugin change (forecast-category defaulting, service-line default, target stamp). `Fl.Forecast.SnapshotWeekly` (+ `Fl.Forecast.CacheNightly`). PCF/ECharts dashboard and security. Seed + per-firm config. Resolved: quota granularity = User + Team + Service Line + Company; SPI 2026 figures verified (win rate 45.1% / 56.5%, coverage 158% / 224%). |
| 1.6 | Aug 21, 2026 | Gustavo Gonzalez Villani (with Claude) | **Keep `tavu_goaltype`'s Source Entity / Date Property / Amount Property fields** (Gustavo: a goal can target any table, not only opportunity). §14 now describes a **generic goal engine** (the Phase-2 flow reads those fields and builds the aggregation dynamically for any table) plus a **`Pl.GoalType.Validate`** pre-op plugin that validates the free-text fields against metadata (fuzzy-match + friendly suggestion) so flexibility carries no typo risk. Finalized the Revenue seed row: Source Entity `tavu_opportunity`, Date Property `tavu_actualclosedate`, Amount Property `tavu_actualrevenue` (metadata only for Revenue; attainment comes from the rollups). |
| 1.5 | Aug 21, 2026 | Gustavo Gonzalez Villani (with Claude) | **Use formula columns (Power Fx), not classic calculated columns**, for the derived math (gap, forecast, attainment %, coverage), per Microsoft's direction (formula columns are the successor to calculated columns). **Rollups are unchanged and NOT deprecated** (Power Fx cannot aggregate across related records, so the four sums stay as native rollups). Exact Power Fx added to `docs/forecasting-step-a-guide.md` §8; §5.1 updated. Verified against Microsoft Learn (formula columns) plus community sources. |
| 1.4 | Aug 21, 2026 | Gustavo Gonzalez Villani (with Claude) | Schema simplifications from build review. (1) **`tavu_periodtype` = Month / Quarter only** (dropped Year; the annual is a derived sum). (2) **Dropped `tavu_isactive`** from `tavu_salesperiod` and `tavu_goaltype`, and `tavu_parentperiod` from `tavu_salesperiod`: use the native statecode Active/Inactive; added **`tavu_fiscalyear`** (Whole Number) to group the annual view. (3) **`tavu_targetkey` is Optional and auto-stamped** (never hand-typed) by a new pre-op plugin **`Pl.SalesTarget.KeyStamp`**; documented its format and a full example record. Reflected in `docs/forecasting-step-a-guide.md`. |
| 1.3 | Aug 21, 2026 | Gustavo Gonzalez Villani (with Claude) | Reconciled against the real solution (`core/src/Solution`) and started the build. (1) **`tavu_businessline` already exists, reuse it** (do not create); §3.2 rewritten to its real columns. (2) **Corrected the estimated-value field to `tavu_estimatedrevenue`** (was `tavu_estimatedvalue`) in the rollups and stamp. (3) Added **§3.0 prescribed option-set values** (forecast category incl. Omitted=576600004; new tavu_scope, tavu_periodtype, tavu_metrickind; opportunity status). (4) **Started Step C**: extended `Pl.Opportunity.LifecycleTracker` with forecast-category defaulting (mirror probability), force-to-Closed on close, reset on reopen, and business-line default on Create (committed). The target stamp will be a separate `Pl.Opportunity.ForecastStamp` assembly (needs the new tables). |
| 1.2 | Aug 21, 2026 | Gustavo Gonzalez Villani (with Claude) | Clarified the three independent time axes and dropped Year as a target period. **Target period (`tavu_forecastperiodtype`) = Month / Quarter only** (default Quarter); Year is only a reporting roll-up parent (no targets set on it), and the **annual figure is the derived sum** of a fiscal year's buckets (Total column in the quota grid; an annual roll-up row written by the flow). The **weekly snapshot** ("the photo") is a separate axis from the period bucket. Updated §3.1, §3.7, §7, §9. |
| 1.1 | Aug 21, 2026 | Gustavo Gonzalez Villani (with Claude) | Revisions after reviewing HubSpot forecast configuration. (1) **Renamed Service Line to Business Line** (`tavu_businessline`) throughout, per OpenTavu terminology. (2) Added the **Omitted** forecast category (HubSpot-style "do not forecast"): new value on the opportunity and stage; Open Pipeline / coverage / forecast exclude it. (3) Added the **`tavu_goaltype`** config table from day one (goal/forecast type: metric kind, source, date property, amount property, forecast-category-aware); **Phase 1 seeds and builds Revenue only**. (4) **Cadence = Month / Quarter / Year**, default Quarter for PS; added a **quota-entry grid Custom Page** for low-friction target capture (rows = reps, columns = periods). (5) Added **§14 Phase-2 roadmap**: activity goal templates (Meetings Booked, Proposals Sent, Deals Created, Calls) as count-based goal types over Module 3 / proposal / opportunity data, Forecastable Revenue, ARR/ACV amount extensibility, and the optional forecast-submission reminder (deliberately not in MVP). |
