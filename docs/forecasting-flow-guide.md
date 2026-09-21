# Fl.Forecast.SnapshotWeekly — build guide (Power Automate)

> Companion to `forecasting-build-plan.md` §7 and `forecasting-step-a-guide.md`. Builds the
> weekly forecast flow **in the maker/designer**, in stages you can test one at a time, rather
> than as a hand-authored import JSON (which rarely imports cleanly for multi-step Dataverse
> flows). The exported skeleton already has the right Recurrence trigger and the
> `shared_commondataserviceforapps` connection reference — keep those and build on them.
>
> Three time axes, never conflated: (1) the **snapshot** is weekly (pacing history); (2) the
> **target bucket** is Month/Quarter (what attainment is measured on); (3) the **annual** figure
> is a derived sum of buckets, never a bucket you target.

## What the flow does (recap of §7.1)

Idempotent by alternate keys, so a manual re-run is always safe:

1. **Stamp backstop** — re-stamp `tavu_salestarget` on open + last-90-days-Won opportunities where it drifted (safety net for the Assign message and any non-form path the plugin misses).
2. **User-scope snapshot** — for each active User-scope target in an active period, capture its rollups + formula columns into a `tavu_forecastsnapshot` row (upsert by `tavu_snapshotkey`).
3. **Higher-scope aggregation** — sum child User targets up the `tavu_parenttarget` chain into Team → Business Line → Company snapshot rows.
4. **Current-state cache** — upsert `tavu_reportingcache` for every scope (what the dashboard reads).

Build order below: **Stage A** (step 2 + step 4 for User scope) is testable *now* against "Sales target Q3-2026". **Stage B** adds the higher-scope aggregation (step 3). **Stage C** adds the backstop (step 1) and the nightly cache variant.

---

## Conventions used throughout

- **Upsert pattern** (idempotent): *List rows* filtered on the alternate key → *Condition* on `length(outputs('List_...')?['body/value'])` → **Create** in the "is 0" branch, **Update** in the "else" branch (Row ID = the id returned by the list). This is the reliable, designer-visible way to upsert; do not rely on Update-by-alternate-key.
- **Choice values** (publisher range): scope User=576600000, Team=576600001, Business Line=576600002, Company=576600003.
- **statecode**: Active=0, Inactive=1. **statuscode** Won=576600005.
- Money fields read from the target's rollups are plain decimals in the flow.
- All `tavu_salestarget` rollups are read straight off the row (they self-refresh hourly); the flow captures whatever the last recalc produced — acceptable per the freshness tiering.

---

## Stage A — User-scope snapshot + cache (build and test this first)

### A0. Trigger (already in your export)

Recurrence, Week / interval 1. For the real cadence, set it from
`tavu_systemsettings.tavu_forecastsnapshotdow` (default Friday) 17:00 America/Bogota. For testing,
leave it weekly and use **Run** manually.

> The skeleton's `List_rows` on `tavu_opportunities` belongs to Stage C (the backstop). For Stage
> A, add the actions below; you can delete or disable that opportunities list until Stage C.

### A1. List the User-scope targets to snapshot

**Action:** Dataverse → *List rows*.
- Table: **Sales Targets** (`tavu_salestargets`).
- **Filter rows** (OData):
  ```
  statecode eq 0 and tavu_scope eq 576600000
  ```
- **Select columns** (keep it lean):
  ```
  tavu_salestargetid,tavu_name,tavu_scope,tavu_targetamount,tavu_closedwonamount,tavu_committedamount,tavu_bestcaseamount,tavu_openpipelineamount,tavu_forecastamount,tavu_forecastfinal,tavu_attainmentpct,tavu_forecastattainmentpct,tavu_gaptogoal,tavu_coverageratio,_tavu_salesperiod_value,_tavu_businessline_value,_tavu_salesrep_value
  ```
  (Use the real lookup logical names: Sales Rep = `tavu_salesrep`, Period = `tavu_salesperiod`, Business Line = `tavu_businessline`. Drop `_tavu_user_value` if the field is not on the table.)
- **Expand Query** (to get period dates in one call, avoids a per-row read):
  ```
  tavu_SalesPeriod($select=tavu_startdate,tavu_enddate,tavu_name)
  ```
  Adjust the navigation property name to your relationship's schema (it is the PascalCase of the lookup, e.g. `tavu_SalesPeriod`).

### A2. For each target — compute pacing and upsert the snapshot

**Action:** *Apply to each* over `value` of A1.

Inside the loop, first compute the period-elapsed fraction. Add two **Compose** actions (calendar-linear for MVP; working-day refinement is a later enhancement):

**Compose — PeriodElapsedPct**
```
if(
  greaterOrEquals(ticks(utcNow()), ticks(items('Apply_to_each')?['tavu_SalesPeriod/tavu_enddate'])),
  100,
  if(
    lessOrEquals(ticks(utcNow()), ticks(items('Apply_to_each')?['tavu_SalesPeriod/tavu_startdate'])),
    0,
    div(
      mul(sub(ticks(utcNow()), ticks(items('Apply_to_each')?['tavu_SalesPeriod/tavu_startdate'])), 100.0),
      sub(ticks(items('Apply_to_each')?['tavu_SalesPeriod/tavu_enddate']), ticks(items('Apply_to_each')?['tavu_SalesPeriod/tavu_startdate']))
    )
  )
)
```

**Compose — ExpectedPaceAmount**
```
div(
  mul(coalesce(items('Apply_to_each')?['tavu_targetamount'], 0), outputs('Compose_-_PeriodElapsedPct')),
  100.0
)
```

**Compose — SnapshotKey**
```
concat(items('Apply_to_each')?['tavu_salestargetid'], '|', utcNow('yyyy-MM-dd'))
```

Then the upsert:

**A2a. List rows — existing snapshot** (Forecast Snapshots `tavu_forecastsnapshots`)
- Filter rows: `tavu_snapshotkey eq '@{outputs('Compose_-_SnapshotKey')}'`
- Select columns: `tavu_forecastsnapshotid`
- Row count: 1

**A2b. Condition** — `length(outputs('List_rows_-_existing_snapshot')?['body/value'])` **is equal to** `0`.

**If yes → Add a new row** (Forecast Snapshots). If no → **Update a row** (Row ID = `first(outputs('List_rows_-_existing_snapshot')?['body/value'])?['tavu_forecastsnapshotid']`). Both branches set the **same fields**:

| Field | Value (expression) |
|---|---|
| Name (`tavu_name`) | `concat(items('Apply_to_each')?['tavu_SalesPeriod/tavu_name'], ', ', utcNow('yyyy-MM-dd'))` |
| Sales Target (`tavu_salestarget`) | `/tavu_salestargets(@{items('Apply_to_each')?['tavu_salestargetid']})` |
| Sales Period (`tavu_salesperiod`) | `/tavu_salesperiods(@{items('Apply_to_each')?['_tavu_salesperiod_value']})` |
| Scope (`tavu_scope`) | `items('Apply_to_each')?['tavu_scope']` (carried from the target, not hardcoded) |
| Snapshot Date (`tavu_snapshotdate`) | `utcNow()` |
| Period Elapsed % (`tavu_periodelapsedpct`) | `outputs('Compose_-_PeriodElapsedPct')` |
| Closed Won (`tavu_closedwonamount`) | `items('Apply_to_each')?['tavu_closedwonamount']` |
| Committed (`tavu_committedamount`) | `items('Apply_to_each')?['tavu_committedamount']` |
| Best Case (`tavu_bestcaseamount`) | `items('Apply_to_each')?['tavu_bestcaseamount']` |
| Open Pipeline (`tavu_openpipelineamount`) | `items('Apply_to_each')?['tavu_openpipelineamount']` |
| Target Amount (`tavu_targetamount`) | `items('Apply_to_each')?['tavu_targetamount']` |
| Forecast Amount (`tavu_forecastamount`) | `items('Apply_to_each')?['tavu_forecastamount']` |
| Attainment % (`tavu_attainmentpct`) | `items('Apply_to_each')?['tavu_attainmentpct']` |
| Gap to Goal (`tavu_gaptogoal`) | `items('Apply_to_each')?['tavu_gaptogoal']` |
| Coverage Ratio (`tavu_coverageratio`) | `items('Apply_to_each')?['tavu_coverageratio']` |
| Expected Pace (`tavu_expectedpaceamount`) | `outputs('Compose_-_ExpectedPaceAmount')` |
| Snapshot Key (`tavu_snapshotkey`) | `outputs('Compose_-_SnapshotKey')` |

> Lookups are set with the `/pluralname(guid)` bind syntax in the Dataverse connector's
> lookup-binding fields (the ones labelled "…(Sales Target)"), not the display field.

### A3. Same loop — upsert the User-scope cache row

Still inside *Apply to each* (after the snapshot upsert), do the cache upsert.

**Compose — CacheKey** — `scope|subject|period|businessline`:
```
concat(string(items('Apply_to_each')?['tavu_scope']), '|',
       coalesce(items('Apply_to_each')?['_tavu_salesrep_value'], ''), '|',
       items('Apply_to_each')?['_tavu_salesperiod_value'], '|',
       coalesce(items('Apply_to_each')?['_tavu_businessline_value'], ''))
```

**A3a. List rows — existing cache** (Reporting Caches `tavu_reportingcaches`)
- Filter: `tavu_cachekey eq '@{outputs('Compose_-_CacheKey')}'`
- Select: `tavu_reportingcacheid`

**A3b. Condition** on `length(...) equals 0` → Create / Update (same fields):

| Field | Value |
|---|---|
| Name (`tavu_name`) | `concat('User · ', items('Apply_to_each')?['tavu_name'])` |
| Cache Key (`tavu_cachekey`) | `outputs('Compose_-_CacheKey')` |
| Scope (`tavu_scope`) | `items('Apply_to_each')?['tavu_scope']` |
| Sales Period (`tavu_salesperiod`) | `/tavu_salesperiods(@{items('Apply_to_each')?['_tavu_salesperiod_value']})` |
| Sales Rep (`tavu_user`) | `/systemusers(@{items('Apply_to_each')?['_tavu_salesrep_value']})` (only if present) |
| Business Line (`tavu_businessline`) | `/tavu_businesslines(@{items('Apply_to_each')?['_tavu_businessline_value']})` (only if present) |
| Sales Target (`tavu_salestarget`) | `/tavu_salestargets(@{items('Apply_to_each')?['tavu_salestargetid']})` |
| Target / Closed Won / Committed / Best Case / Open Pipeline / Forecast / Forecast Final / Attainment % / Forecast Attainment % / Gap / Coverage | copy each from the corresponding `items('Apply_to_each')?['...']` |
| Computed On (`tavu_computedon`) | `utcNow()` |
| Win Rate (`tavu_winrate`) | leave blank in Stage A (added in Stage C) |

> Setting a lookup only "if present": wrap the whole *Set* in a Condition, or use the connector's
> ability to leave the field blank when the source value is null. An empty `/systemusers()` bind
> will error, so guard it.

### A4. Test Stage A

Run the flow manually. Expected against your current data:
- One `tavu_forecastsnapshot` row for "Sales target Q3-2026" with Closed Won 30,000, Best Case 20,000, Open Pipeline 20,000, Target 100,000, Attainment 30, snapshotkey `<targetid>|<today>`.
- One `tavu_reportingcache` row (scope User) with the same numbers and a fresh `tavu_computedon`.
- Run it **twice**: the second run must **update** the same two rows (not create duplicates). That proves the alternate-key upsert works.

---

## Stage B — Higher-scope aggregation (Company / Business Line; Team is a later add)

Dataverse cannot roll up over a rollup, so the higher scopes are summed here from the child
User-scope targets. As-built this covers **Company** and **Business Line**; **Team** is a clean
addition later (same pattern, scope 576600001, grouping by team) once team membership /
`tavu_parenttarget` is defined. No redesign is needed to add it: the schema, the cache-key
convention, and this aggregation block already accommodate it.

**Prerequisite (mandatory to test):** a higher-scope **Sales Target** row must exist for each
scope you want tracked, because it holds that level's quota AND anchors the snapshot (the
snapshot's `tavu_salestarget` lookup is Required). So create a Company-scope target (period, quota,
no rep/line) and, optionally, a Business-Line-scope target (period, quota, Business Line set).
The flow fills their actuals; it never invents them.

**Cache-key convention (locked, forward-compatible):** `scope | subject | period | businessline`
- User: `576600000 | repid | period | lineid`
- Business Line: `576600002 | (empty) | period | lineid`  (the line is the subject)
- Company: `576600003 | (empty) | period | (empty)`
- Team (later): `576600001 | teamid | period | (empty)`  — never collides with the above.

As-built actions (top level, after the User `Apply_to_each`):

1. **`List_higher_scope_targets`** — List `tavu_salestargets` where
   `statecode eq 0 and (tavu_scope eq 576600002 or tavu_scope eq 576600003)`. Select id, name,
   scope, targetamount, `_tavu_salesperiod_value`, `_tavu_businessline_value`.
2. **`Apply_to_each_higher`** — per higher target:
   - **`Compose-B-ChildFetch`** builds a **FetchXML aggregate** that SUMs the User children of the
     same period (and same business line when the target is Business-Line scope). Using a FetchXML
     `aggregate="true"` sum avoids nested loops and variable accumulation entirely:
     ```
     <fetch aggregate="true"><entity name="tavu_salestarget">
       <attribute name="tavu_closedwonamount"   alias="won"  aggregate="sum"/>
       <attribute name="tavu_committedamount"   alias="com"  aggregate="sum"/>
       <attribute name="tavu_bestcaseamount"    alias="best" aggregate="sum"/>
       <attribute name="tavu_openpipelineamount" alias="opn" aggregate="sum"/>
       <filter type="and">
         <condition attribute="statecode"       operator="eq" value="0"/>
         <condition attribute="tavu_scope"      operator="eq" value="576600000"/>
         <condition attribute="tavu_salesperiod" operator="eq" value="<periodid>"/>
         <!-- Business-Line scope only: -->
         <condition attribute="tavu_businessline" operator="eq" value="<lineid>"/>
       </filter>
     </entity></fetch>
     ```
   - **`List_children_sum`** runs that FetchXML (List rows, `fetchXml` parameter).
   - Compose the sums (`coalesce(first(...body/value)?['won'],0)` etc.) and the derived metrics,
     measuring against the higher target's OWN `tavu_targetamount` (the manager-set quota), never
     the sum of the children's quotas. **Do NOT sum percentages** — recompute:
     - Gap = Target − ClosedWonΣ
     - Attainment % = ClosedWonΣ / Target × 100 (null when Target = 0)
     - Forecast = ClosedWonΣ + CommittedΣ; Forecast Attainment % = Forecast / Target × 100
     - Coverage = OpenPipelineΣ / Gap (null when Gap ≤ 0)
   - **Upsert snapshot** by `tavu_snapshotkey` (`<targetid>|yyyy-MM-dd`) and **upsert cache** by
     `tavu_cachekey` (convention above), both linking `tavu_salestarget` = this higher target and
     `tavu_scope` = its scope. On the cache, set `tavu_businessline` only for Business-Line scope
     (guarded if-null bind); Company rows leave rep/line empty.

Notes:
- Pacing (`tavu_periodelapsed` / `tavu_expectedpaceamount`) is left off the higher scopes for now
  (optional fields). To add a Company pacing line, `$expand` the period on
  `List_higher_scope_targets` and reuse the Stage A period-elapsed Compose. Additive, no rework.
- Keep aggregation to the handful of active targets per period; well inside all limits. Set each
  written row's **owner** to its subject where you want security to match (Company / Business-Line
  rows owned by a "Sales Leadership" team).

### 3b. Annual roll-up (optional, simplest client-side)

Do not target a year. Either sum the fiscal year's period rows into an annual cache row grouped on
`tavu_fiscalyear`, or (simpler, recommended for MVP) let the dashboard sum the period rows
client-side. Ship the dashboard-side sum first.

---

## Stage C — Stamp backstop + nightly cache

### C1. Stamp backstop (this is where the skeleton's opportunities List_rows goes)

Runs first in the weekly flow, before Stage A:
1. *List rows* on **Opportunities** (`tavu_opportunities`) where open OR won in the last 90 days:
   ```
   statecode eq 0 or (statecode eq 1 and statuscode eq 576600005 and tavu_actualclosedate ge @{addDays(utcNow(),-90,'yyyy-MM-dd')})
   ```
   Select: `tavu_opportunityid,_ownerid_value,tavu_estimatedclosedate,tavu_actualclosedate,_tavu_businessline_value,statuscode,statecode,_tavu_salestarget_value`.
2. For each, recompute the correct target exactly as `Pl.Opportunity.ForecastStamp` does (owner =
   Sales Rep, effective close date's period, Revenue goal type, matching business line else null).
   The cheapest implementation: don't reproduce the match in the flow — instead **touch the
   opportunity** (Update a no-op filtering attribute, e.g. write `statuscode` to its current value)
   so the pre-op plugin re-runs and re-stamps. Guard against a real change by writing the same
   value back.
3. Only needed because a pure reassignment fires **Assign**, not Update, which the plugin's Update
   step does not catch. If you later add an Assign step to `Pl.Opportunity.ForecastStamp`, this
   backstop becomes optional.

### C2. `Fl.Forecast.CacheNightly`

Clone the weekly flow; keep Stage C1 + Stage A step 4 (cache) + Stage B; **drop the snapshot
history writes** (Stage A step A2). Nightly recurrence. Rep rows already refresh ~hourly via native
rollups; this keeps Team/BL/Company at most one day stale between weekly snapshots.

---

## Gotchas checklist

- Verify every lookup's **navigation property** name in the `$expand` (PascalCase of the schema, not the logical name) — a wrong name silently returns null dates and breaks the pacing math.
- Guard every lookup-bind against null (`/systemusers()` with an empty guid errors the whole action).
- The alternate keys (`tavu_snapshotkey`, `tavu_cachekey`) MUST exist and be active, or the upsert list-filters return nothing and you get duplicates.
- Do not sum percentages across scopes — always recompute Attainment %, Coverage, etc. from the summed base amounts.
- Rollups are hourly; if a snapshot looks one recalc stale, that is expected, not a flow bug.
