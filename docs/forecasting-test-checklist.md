# AI-Assisted Forecasting, Phase 1: End-to-End Test Checklist

> Run top to bottom. Each test lists what to do and the expected result. Mark `[x]` when it
> passes. Related: `forecasting-build-plan.md`, `sales-model.md` §6.3bis.
> Scope of this phase: **deterministic** goal/quota and attainment (no AI). Verify constants
> and option-set values against the environment before testing.

## 0. Prerequisites (confirm before testing)

- [ ] Tables created with all columns, relationships, and alternate keys: `tavu_salesperiod`,
      `tavu_businessline`, `tavu_goaltype`, `tavu_salestarget`, `tavu_forecastsnapshot`, `tavu_reportingcache`.
- [ ] `tavu_goaltype` seeded with the **Revenue** row; every `tavu_salestarget` references it.
- [ ] The **Omitted** value exists on `tavu_forecastcategory` (opportunity and `tavu_salesstage`).
- [ ] `tavu_opportunity` has `tavu_forecastcategory`, `tavu_forecastcategoryismanual` (existing),
      plus new `tavu_businessline` and `tavu_salestarget` (system-managed, hidden or read-only).
- [ ] Four native rollups on `tavu_salestarget` defined (`tavu_closedwonamount`,
      `tavu_committedamount`, `tavu_bestcaseamount`, `tavu_openpipelineamount`) and the seven
      calculated columns (§5.1 of the build plan).
- [ ] Pre-op opportunity plugin built and registered with the added steps (forecast-category
      defaulting, business-line default, target stamp), Create + Update, Pre-Operation, Sync.
- [ ] `Fl.Forecast.SnapshotWeekly` imported and On; `Fl.Forecast.CacheNightly` imported and On
      (if used).
- [ ] PCF dashboard deployed on a Custom Page / dashboard with scope + period selectors.
- [ ] Seed loaded: `tavu_businessline` "General Practice"; current-year Quarter + Year periods;
      `tavu_systemsettings` (coverage target 2.0, period type Quarter, default business line set,
      snapshot day Friday).
- [ ] Test data: at least two sales reps under one manager; a Team; User-scope targets for both
      reps in the current quarter, a Team target, and a Company target linked via
      `tavu_parenttarget`. Plugin Trace Log enabled.

## 1. Schema, rollups, and calculated columns

- [ ] **S1, Committed sum.** Open opportunity, owner = Rep A, estimated close in the current
      quarter, Forecast Category = Committed, estimated value 100k. After the hourly recalc (or
      manual recalculate) Rep A's current-quarter target shows `Committed Amount` = 100k,
      `Open Pipeline Amount` = 100k, `Closed Won` = 0.
- [ ] **S2, Best Case sum.** Change the same opportunity to Best Case. → `Best Case Amount` = 100k,
      `Committed Amount` = 0.
- [ ] **S3, Closed Won by actual date.** Close the opportunity as Won with an actual close date in
      the current quarter, actual revenue 100k. → `Closed Won Amount` = 100k; `Committed`/`Best
      Case`/`Open Pipeline` = 0; the opportunity's `tavu_salestarget` still points to the
      current-quarter target.
- [ ] **S4, Calculated columns.** With Target = 250k and Closed Won = 100k: `Gap to Goal` = 150k,
      `Attainment %` = 40, `Forecast Amount` = Won + Committed, `Coverage Ratio` =
      OpenPipeline / GapToGoal. Set `Manager Adjusted Amount` = 120k → `Forecast (Final)` = 120k.
- [ ] **S5, Divide-by-zero.** A target with Target Amount = 0 → `Attainment %` renders as null/0,
      not an error. A target with Gap <= 0 (goal met) → coverage shows "Goal met," not an error.
- [ ] **S6, Omitted excluded.** Open opportunity in an **Omitted** stage/category, estimated value
      50k. → it is **not** counted in `Open Pipeline Amount`, coverage, or the forecast. Move it to
      Pipeline/Best Case → it now appears in Open Pipeline.

## 2. Pre-op plugin, defaulting and stamping

- [ ] **P1, Forecast-category default on create.** New opportunity in a stage whose forecast
      category is Best Case, leave category blank. → `Forecast Category` = Best Case,
      `Is Manual` = No.
- [ ] **P2, Re-default on stage change.** Move it to a Committed-category stage (Is Manual still
      No). → `Forecast Category` = Committed.
- [ ] **P3, Manual override sticks.** Set `Forecast Category` = Best Case by hand. → `Is Manual`
      = Yes. Change stage again. → category does **not** move.
- [ ] **P4, Reset button.** Click "Reset Forecast Category to Stage Default." → `Is Manual` = No,
      category = current stage default.
- [ ] **P5, Business-line default.** New opportunity with `Business Line` blank. → set to the System
      Settings default (General Practice).
- [ ] **P6, Stamp by estimated date (open).** New open opportunity, owner Rep A, estimated close in
      the current quarter. → `Sales Target` = Rep A's current-quarter target (trace shows the
      period + target chosen).
- [ ] **P7, Re-stamp on close across a period boundary.** Open opportunity estimated to close in the
      current quarter but Won with an actual close date in the **next** quarter. → after close,
      `Sales Target` re-stamps to Rep A's **next-quarter** target; the Won amount lands in that
      period.
- [ ] **P8, Reassignment.** Reassign an open opportunity from Rep A to Rep B. → after the nightly
      reconciliation (or the immediate handler if built), `Sales Target` points to Rep B's target;
      Rep A's rollups drop it, Rep B's pick it up.
- [ ] **P9, Per-practice quota.** Rep A has two current-quarter User targets, one with
      Business Line = Advisory and one with Business Line = Delivery. An Advisory opportunity stamps
      to the Advisory target only.
- [ ] **P10, No target yet.** Owner with no quota row for the period. → `Sales Target` left empty;
      no error; the opportunity still saves.

## 3. Flows, snapshot and cache

- [ ] **F1, Weekly snapshot.** Run `Fl.Forecast.SnapshotWeekly` manually. → one
      `tavu_forecastsnapshot` per active target per today's date, with captured amounts, target,
      attainment, gap, coverage, and `Expected Pace Amount`; owner = the target's subject.
- [ ] **F2, Idempotent re-run.** Run it again the same day. → no duplicate snapshot rows (upsert by
      `tavu_snapshotkey`).
- [ ] **F3, Higher-scope aggregation.** After the run, the Team snapshot = sum of its two reps'
      User snapshots; the Company snapshot = sum of teams. Verify against S-series numbers.
- [ ] **F4, Cache refresh.** `tavu_reportingcache` has current-state rows for User, Team, and
      Company scopes with matching numbers, `Win Rate`, and a fresh `Computed On`.
- [ ] **F5, Stamp reconciliation backstop.** Manually null a Won opportunity's `Sales Target`, run
      the flow. → the flow re-stamps it correctly.
- [ ] **F6, Nightly cache (if used).** Run `Fl.Forecast.CacheNightly`. → cache refreshes; no new
      snapshot history row is written.

## 4. Dashboard

- [ ] **D1, Attainment + gap tiles.** Open the dashboard as a manager, scope = User, period =
      current quarter, subject = Rep A. → attainment gauge and gap tile match S4.
- [ ] **D2, Coverage zones.** A target with coverage below `tavu_coveragetarget` shows the
      under-covered color; around 2x shows healthy; well above shows the over-selling warning.
- [ ] **D3, Pacing line.** With at least two weekly snapshots, the pacing chart plots cumulative
      Closed Won against Expected Pace over the period.
- [ ] **D4, Pipeline-by-category.** Stacked bar shows Pipeline / Best Case / Committed / Closed Won,
      with Pipeline = OpenPipeline minus Committed minus Best Case.
- [ ] **D5, Scope switch.** Switch scope to Team then Company. → tiles repopulate from the cache
      (not a live opportunity aggregate); no throttling error.
- [ ] **D6, Reads pre-aggregated only.** Confirm (network trace / Monitor) the PCF queries
      `tavu_reportingcache` / `tavu_forecastsnapshot`, never a raw `tavu_opportunity` aggregate.

## 5. Security isolation

- [ ] **X1, Rep sees only own.** As Rep A (non-manager), the dashboard and target/snapshot/cache
      rows show Rep A only; Rep B's numbers are not visible.
- [ ] **X2, Manager sees team.** As the manager, Rep A and Rep B and the Team/Company rows are
      visible via the hierarchy security model.
- [ ] **X3, No BI license.** Confirm no user was assigned a Power BI Pro/PPU license for any of the
      above.

## 6. Sign-off

- [ ] All attainment math is deterministic (native rollups + calculated columns + flow), no AI.
- [ ] Revenue is attributed to the period containing the opportunity's actual (Won) or estimated
      (open) close date, via the stamp.
- [ ] No rollup-over-rollup: Team / Business Line / Company come from the flow, not native rollups.
- [ ] Coverage flags both under-coverage and over-coverage against the ~2x PS target.
- [ ] Row-level security isolates reps; managers see reports via the hierarchy; no Power BI license.
- [ ] Flows are idempotent (safe manual re-run).
