# AI-Assisted Forecasting, Step A: Maker-Portal Build Guide

> Execution checklist to create the forecasting schema in `make.powerapps.com` (solution:
> the OpenTavu core solution). Do the sections **in order**: choices first, then tables and
> scalar columns, then lookups/relationships, then keys, then the opportunity/settings columns,
> then the rollups and calculated columns. Reuses the existing `tavu_businessline` table.
> Schema names use the `tavu_` prefix; option values use the publisher `576600xxx` range.
> Source of truth: `docs/forecasting-build-plan.md` v1.4.

---

## Adjustments if you already built sections 0-7 (Aug 21 revision)

Three small fixes from review, all easy:

1. **`tavu_periodtype` choice:** remove the **Year** option (keep Month, Quarter). Year is not a
   target period; the annual is a derived sum.
2. **Sales Period table:** delete `tavu_isactive` and `tavu_parentperiod` (use the native
   Active/Inactive statecode instead of a Yes/No column; no year parent needed). **Add**
   `tavu_fiscalyear` (Whole Number, required).
3. **Goal Type table:** delete `tavu_isactive` (use native Active/Inactive).
4. **Sales Target `tavu_targetkey`:** change Business Required from Required to **Optional**, and
   keep it off the form (it is auto-stamped, see section 3).

---

## 0. Global choices (create/modify FIRST)

Create these as **Choices** (global option sets). Assign the exact values shown (New choice, add
each option, set its Value manually to the number below).

| Choice (schema) | Options (label = value) |
|---|---|
| `tavu_forecastcategory` **(exists, just ADD one)** | add **Omitted = 576600004** (Pipeline 576600000, Best Case 576600001, Committed 576600002, Closed 576600003 already exist) |
| `tavu_scope` **(new)** | User = 576600000, Team = 576600001, Business Line = 576600002, Company = 576600003 |
| `tavu_periodtype` **(new)** | Month = 576600000, Quarter = 576600001 (NO Year: the annual is a derived sum, not a bucket) |
| `tavu_metrickind` **(new)** | Currency Sum = 576600000, Record Count = 576600001 |

Existing choices you will reuse (do not recreate): `tavu_dayofweek` (for the snapshot day).

---

## 1. Table `tavu_salesperiod` (Sales Period)

- Ownership: **Organization**. Primary column: **Name** (`tavu_name`, Text).
- Enable Auditing.

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Text (Primary) | Yes | "Q3 2026", "Jul 2026" |
| Period Type | tavu_periodtype | Choice → `tavu_periodtype` | Yes | Month / Quarter |
| Fiscal Year | tavu_fiscalyear | Whole Number | Yes | e.g. 2026. Groups the quarters/months into the annual view (the annual is their sum). |
| Start Date | tavu_startdate | Date Only | Yes | |
| End Date | tavu_enddate | Date Only | Yes | |

Alternate key (section 7): `tavu_name`.

> **No `tavu_isactive`, no `tavu_parentperiod`.** Use the native **statecode / statuscode**
> (Active / Inactive, which every Dataverse table has) to deactivate an old period; deactivate the
> record with the native Deactivate command. And there is no Year period row, so no self-lookup
> parent is needed. The annual roll-up is computed by grouping periods on `tavu_fiscalyear`.

---

## 2. Table `tavu_goaltype` (Goal Type)

- Ownership: **Organization**. Primary column: **Name** (`tavu_name`). Enable Auditing.
- Phase 1 seeds only the **Revenue** row; the table exists so Phase-2 activity goals slot in.

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Text (Primary) | Yes | "Revenue" |
| Metric Kind | tavu_metrickind | Choice → `tavu_metrickind` | Yes | Currency Sum / Record Count |
| Source Entity | tavu_sourceentity | Text | Yes | logical name, e.g. `tavu_opportunity` |
| Date Property | tavu_dateproperty | Text | Yes | e.g. close date |
| Amount Property | tavu_amountproperty | Text | No | currency field summed |
| Uses Forecast Category | tavu_usesforecastcategory | Yes/No | Yes | Revenue = Yes |

Alternate key: `tavu_name`. (No `tavu_isactive`: use the native statecode Active/Inactive.)

---

## 3. Table `tavu_salestarget` (Sales Target)

- Ownership: **User or Team** (so row-level + manager-hierarchy security applies). Enable Auditing.
- Primary column: **Name** (`tavu_name`).

**Scalar + lookup columns:**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Text (Primary) | Yes | "Ana Perez, Q3 2026" |
| Goal Type | tavu_goaltype | Lookup → `tavu_goaltype` | Yes | Phase 1 always Revenue |
| Scope | tavu_scope | Choice → `tavu_scope` | Yes | User / Team / Business Line / Company |
| Sales Rep | tavu_user | Lookup → **User** (systemuser) | No | set for User scope |
| Team | tavu_team | Lookup → **Team** | No | set for Team scope |
| Business Line | tavu_businessline | Lookup → `tavu_businessline` (existing) | No | slice / Business-Line scope |
| Sales Period | tavu_salesperiod | Lookup → `tavu_salesperiod` | Yes | |
| Parent Target | tavu_parenttarget | Lookup → `tavu_salestarget` (self) | No | User to Team to Company |
| Target Key | tavu_targetkey | Text | **No** | machine dedup key, **auto-stamped, never hand-typed**; **alternate key** (see below) |
| Target Amount | tavu_targetamount | Currency | Yes | the quota |
| Stretch Target | tavu_stretchtarget | Currency | No | |
| Manager Adjusted Amount | tavu_manageradjustedamount | Currency | No | manager override |
| Manager Adjustment Note | tavu_manageradjustednote | Multiline Text | No | why |

Alternate key: `tavu_targetkey`.

**What `tavu_targetkey` is, and why it will not cause typos.** It is a machine-generated
uniqueness key, NOT a field a person fills in. Its only job is to stop two targets for the same
(goal type, scope, rep/team, business line, period) from existing, and to let the quota grid and
the flow do create-or-update (upsert) safely. A small pre-op plugin **`Pl.SalesTarget.KeyStamp`**
(which I write next) builds it from the record's own fields on save, so you **leave it blank** and
it fills itself. Do NOT put it as an editable field on the form; keep it off the form or read-only.
Format (pipe-joined, empty slot when a dimension does not apply):
`goaltype | scope | userId | teamId | businessLineId | periodId`.

**Full example record (Ana's Q3 revenue quota):**

| Field | Value |
|---|---|
| Name | Ana Perez, Q3 2026, Advisory |
| Goal Type | Revenue |
| Scope | User |
| Sales Rep | Ana Perez |
| Team | (empty) |
| Business Line | Advisory |
| Sales Period | Q3 2026 |
| Parent Target | (empty, or Ana's Team target) |
| Target Amount | 75,000 |
| Target Key | (leave blank; plugin stamps `revenue\|user\|<ana-guid>\|\|<advisory-guid>\|<q3-guid>`) |
| Closed Won / Committed / etc. | (auto, the rollups fill these) |

The **rollups and calculated columns** on this table are section 8 (create them after the
opportunity `tavu_salestarget` lookup exists).

---

## 4. Table `tavu_forecastsnapshot` (Forecast Snapshot)

- Ownership: **User or Team** (the flow stamps owner = subject). Enable Auditing.
- Primary column: **Name** (`tavu_name`).

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Text (Primary) | Yes | auto-composed by the flow |
| Sales Target | tavu_salestarget | Lookup → `tavu_salestarget` | Yes | |
| Sales Period | tavu_salesperiod | Lookup → `tavu_salesperiod` | Yes | |
| Scope | tavu_scope | Choice → `tavu_scope` | Yes | denormalized |
| Snapshot Date | tavu_snapshotdate | Date and Time | Yes | |
| Period Elapsed % | tavu_periodelapsedpct | Decimal (2) | No | |
| Closed Won Amount | tavu_closedwonamount | Currency | No | captured |
| Committed Amount | tavu_committedamount | Currency | No | captured |
| Best Case Amount | tavu_bestcaseamount | Currency | No | captured |
| Open Pipeline Amount | tavu_openpipelineamount | Currency | No | captured |
| Target Amount | tavu_targetamount | Currency | No | captured |
| Forecast Amount | tavu_forecastamount | Currency | No | captured Won+Committed |
| Attainment % | tavu_attainmentpct | Decimal (1) | No | captured |
| Gap to Goal | tavu_gaptogoal | Currency | No | captured |
| Coverage Ratio | tavu_coverageratio | Decimal (2) | No | captured |
| Expected Pace Amount | tavu_expectedpaceamount | Currency | No | on-track line |
| Snapshot Key | tavu_snapshotkey | Text | Yes | **alternate key** (`targetid|yyyy-MM-dd`) |

Alternate key: `tavu_snapshotkey`.

---

## 5. Table `tavu_reportingcache` (Reporting Cache)

- Ownership: **User or Team** (Company/Business-Line rows owned by a Sales Leadership team).
- Primary column: **Name** (`tavu_name`). Enable Auditing.

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Text (Primary) | Yes | auto-composed |
| Cache Key | tavu_cachekey | Text | Yes | **alternate key** (`scope|subject|period|line`) |
| Scope | tavu_scope | Choice → `tavu_scope` | Yes | |
| Sales Period | tavu_salesperiod | Lookup → `tavu_salesperiod` | Yes | |
| Sales Rep | tavu_user | Lookup → **User** | No | subject |
| Team | tavu_team | Lookup → **Team** | No | subject |
| Business Line | tavu_businessline | Lookup → `tavu_businessline` | No | subject / slice |
| Sales Target | tavu_salestarget | Lookup → `tavu_salestarget` | No | link back |
| Target Amount | tavu_targetamount | Currency | No | |
| Closed Won Amount | tavu_closedwonamount | Currency | No | |
| Committed Amount | tavu_committedamount | Currency | No | |
| Best Case Amount | tavu_bestcaseamount | Currency | No | |
| Open Pipeline Amount | tavu_openpipelineamount | Currency | No | |
| Forecast Amount | tavu_forecastamount | Currency | No | |
| Forecast (Final) | tavu_forecastfinal | Currency | No | |
| Attainment % | tavu_attainmentpct | Decimal (1) | No | |
| Forecast Attainment % | tavu_forecastattainmentpct | Decimal (1) | No | |
| Gap to Goal | tavu_gaptogoal | Currency | No | |
| Coverage Ratio | tavu_coverageratio | Decimal (2) | No | |
| Win Rate | tavu_winrate | Decimal (1) | No | trailing Won/(Won+Lost), percent |
| Computed On | tavu_computedon | Date and Time | Yes | freshness |

Alternate key: `tavu_cachekey`.

---

## 6. New columns on EXISTING tables

### 6.1 `tavu_opportunity`

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Forecast Category | tavu_forecastcategory | Choice → **global** `tavu_forecastcategory` | No | bind to the SAME global choice as the stage |
| Forecast Category Is Manual | tavu_forecastcategoryismanual | Yes/No | No | default No |
| Business Line | tavu_businessline | Lookup → `tavu_businessline` | No | defaulted on create by the plugin |
| Sales Target | tavu_salestarget | Lookup → `tavu_salestarget` | No | system-managed stamp; hide from sellers |

### 6.2 `tavu_systemsettings`

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Coverage Target | tavu_coveragetarget | Decimal (1) | No | default **2.0** |
| Forecast Period Type | tavu_forecastperiodtype | Choice → **global** `tavu_periodtype` | No | default **Quarter** (use Month/Quarter only) |
| Default Business Line | tavu_defaultbusinessline | Lookup → `tavu_businessline` | No | pre-fills opportunity business line |
| Forecast Snapshot Day | tavu_forecastsnapshotdow | Choice → **global** `tavu_dayofweek` | No | default **Friday** |

---

## 7. Alternate keys (create after the columns exist)

| Table | Key column(s) |
|---|---|
| tavu_salesperiod | tavu_name |
| tavu_goaltype | tavu_name |
| tavu_salestarget | tavu_targetkey |
| tavu_forecastsnapshot | tavu_snapshotkey |
| tavu_reportingcache | tavu_cachekey |

(Business line reuses its existing keys; no new key needed.)

---

## 8. Rollup + formula columns on `tavu_salestarget` (Step B, after the opportunity lookup exists)

**Native rollups** (Data type: Rollup). All aggregate over the 1:N from `tavu_salestarget` to
`tavu_opportunity` created by the `tavu_opportunity.tavu_salestarget` lookup. The period window is
enforced by the plugin stamp, so the rollup filters on status and category only. Use the real
money fields: **`tavu_actualrevenue`** (Won) and **`tavu_estimatedrevenue`** (open).

| Rollup (schema) | Type | Aggregate | Filter (related opportunity) |
|---|---|---|---|
| tavu_closedwonamount | Currency | SUM `tavu_actualrevenue` | Status Reason = **Won** |
| tavu_committedamount | Currency | SUM `tavu_estimatedrevenue` | Status = **Open** AND Forecast Category = **Committed** |
| tavu_bestcaseamount | Currency | SUM `tavu_estimatedrevenue` | Status = **Open** AND Forecast Category = **Best Case** |
| tavu_openpipelineamount | Currency | SUM `tavu_estimatedrevenue` | Status = **Open** AND Forecast Category **≠ Omitted** |
| tavu_opencount (optional) | Whole Number | COUNT | Status = **Open** AND Forecast Category **≠ Omitted** |

**Formula columns (Power Fx), NOT classic calculated columns.** Rollups stay (they are the only
way to aggregate; not deprecated). The derived math uses **formula columns**, Microsoft's
successor to calculated columns. Create each as a column with data type = the base type, then pick
**Formula** and paste the Power Fx. They read the same-record rollup values above and
`tavu_targetamount`.

**Important, currency limitation:** formula columns do NOT accept Currency fields directly ("Direct
use of currency fields is not yet supported"). The fix (Microsoft's own workaround): set each
column's **data type to Decimal** and wrap every currency field in `Decimal(...)`. Decimal precision
2; its range (~100 billion) is plenty for quota amounts. So the four money outputs become Decimal
too (you lose the currency symbol on the form, but the number and the dashboard are correct).

| Formula column (schema) | Type | Power Fx |
|---|---|---|
| tavu_gaptogoal | Decimal (2) | `Decimal(tavu_targetamount) - Decimal(tavu_closedwonamount)` |
| tavu_forecastamount | Decimal (2) | `Decimal(tavu_closedwonamount) + Decimal(tavu_committedamount)` |
| tavu_upsideamount | Decimal (2) | `Decimal(tavu_closedwonamount) + Decimal(tavu_committedamount) + Decimal(tavu_bestcaseamount)` |
| tavu_forecastfinal | Decimal (2) | `If(!IsBlank(tavu_manageradjustedamount) && Decimal(tavu_manageradjustedamount) <> 0, Decimal(tavu_manageradjustedamount), Decimal(tavu_closedwonamount) + Decimal(tavu_committedamount))` |
| tavu_attainmentpct | Decimal (1) | `If(Decimal(tavu_targetamount) = 0, Blank(), Decimal(tavu_closedwonamount) / Decimal(tavu_targetamount) * 100)` |
| tavu_forecastattainmentpct | Decimal (1) | `If(Decimal(tavu_targetamount) = 0, Blank(), (Decimal(tavu_closedwonamount) + Decimal(tavu_committedamount)) / Decimal(tavu_targetamount) * 100)` |
| tavu_coverageratio | Decimal (2) | `If(Decimal(tavu_targetamount) - Decimal(tavu_closedwonamount) <= 0, Blank(), Decimal(tavu_openpipelineamount) / (Decimal(tavu_targetamount) - Decimal(tavu_closedwonamount)))` |

> Notes. Rollups recalc async (hourly), so a formula column that reads them shows the last rollup
> value; that is fine for KPIs. Formula columns cannot aggregate (that is why the sums stay as
> rollups) and cannot use Currency directly (wrap in `Decimal()`, output Decimal). **Fallback:** if
> the maker will not let a formula column reference a rollup column, leave these off
> `tavu_salestarget` and let the reporting-cache flow compute them into `tavu_reportingcache`
> (already the path for Team/Business-Line/Company scopes). Do NOT use classic calculated columns.

---

## 9. Seed rows (minimum to start)

- `tavu_goaltype`: one row **"Revenue"**: Metric Kind = **Currency Sum**, Uses Forecast Category =
  **Yes**, Source Entity = **`tavu_opportunity`**, Date Property = **`tavu_actualclosedate`**, Amount
  Property = **`tavu_actualrevenue`**, Status = Active. (For Revenue these three are metadata only:
  attainment comes from the Sales Target rollups, which already split actual/Won vs estimated/Open.
  The Source/Date/Amount fields become functional for the Phase-2 generic activity-goal engine, and
  a `Pl.GoalType.Validate` plugin will validate them against metadata then, see build plan §14.)
- `tavu_businessline`: if the firm has none, add **"General Practice"** (Active).
- `tavu_salesperiod`: this year's **Quarter** rows (Q1..Q4), each with `tavu_fiscalyear` = 2026 and
  its Start/End dates. No Year row. (The annual view sums the four quarters of the same fiscal year.)
- `tavu_systemsettings` (the single record): Coverage Target = 2.0, Forecast Period Type =
  **Quarter**, Default Business Line = General Practice, Forecast Snapshot Day = Friday.

---

## 10. After Step A

Publish all customizations. Then: (Step B is folded into section 8 above) and the code steps
resume, the pre-op plugin (`Pl.Opportunity.LifecycleTracker`, already committed) can be built and
registered, and the target stamp (`Pl.Opportunity.ForecastStamp`) + the snapshot flow + the PCF
dashboard follow. Registration for the plugin is in the file's header comments.
