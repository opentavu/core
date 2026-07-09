# Migration Plan — Case Status as a Config Table (`tavu_casestatus`)

**Goal:** move the case's operational status off the native `statuscode` (status reason) into a config table `tavu_casestatus`, so status vocabulary is a single source of truth and per-status behaviors (pauses SLA, resolved/cancelled, resume target) are columns — not hardcoded values and not a mirrored optionset. This makes **SLA pause status-driven** (the industry norm) without any drift.

**Status:** PLAN — pending approval.
**Date:** July 7, 2026.

---

## 1. Target design

### New table `tavu_casestatus` (Case Status)

Common base: Ownership = Organization, Audit ✅, primary column `Name`, states Active/Inactive.

| Display Name | Schema | Type | Notes |
|---|---|---|---|
| Name | tavu_name | Single Line (primary) | e.g. "Waiting on Customer" |
| Code | tavu_code | Autonumber `CST-{SEQNUM:0000}` | stable key (config reference) |
| State Category | tavu_statecategory | Choice: **Active / Resolved / Cancelled** | drives the case `statecode` + the Resolved/Cancelled grouping (§6) |
| Pauses SLA | tavu_pausessla | Yes/No | **the pause driver** — a status with Yes stops the SLA clock |
| Is Default (new) | tavu_isdefaultnew | Yes/No | status a brand-new case gets (exactly one Yes) |
| Is Resume Target | tavu_isresumetarget | Yes/No | status set when work resumes / on auto-resume — i.e. "In Progress" (exactly one Yes) |
| Sort Order | tavu_sortorder | Whole Number | ordering in dropdowns |
| Display Color | tavu_displaycolor | Single Line (hex) | optional |
| Description | tavu_description | Multiple Lines | optional |

**Seed (maps the current §6 statuscodes):**

- **Active:** New *(isdefaultnew)*, AI Processing, Categorized — Awaiting Assignment, In Progress *(isresumetarget)*, Manual Review Required, **Waiting on Customer *(pausessla = Yes)***
- **Resolved:** Solved, Information Provided, Duplicate, Out of Scope
- **Cancelled:** Cancelled by Customer, Cannot Reproduce, Closed without Resolution

### Changes to `tavu_case`

- **Add** lookup `tavu_status` → `tavu_casestatus` — the operational status (replaces statuscode for stage).
- **Keep** `statecode` (Active/Inactive) — driven from `tavu_status.tavu_statecategory` by a plugin (Active → Active; Resolved/Cancelled → Inactive). `statuscode` reduced to vestigial (one reason per state).
- **Keep** `tavu_slapausedon` (pause timestamp).
- **Drop** `tavu_slaonhold` (pause is now derived from `tavu_status.tavu_pausessla`), and `tavu_setstatus` (the compose sets `tavu_status` directly). Drop `tavu_slapausestatuses` if it was created.
- **Keep** `tavu_slaautoresume` (systemsettings) — still governs whether an inbound auto-changes the status back to the resume target.

## 2. How pause/resume works now (status-driven, no boolean)

- **Pause:** when `tavu_status` changes to a status whose `tavu_pausessla = Yes`, `Pl.Case.SlaAssignment` runs the **guardrail** (block if no Outbound exists) then cancels the gateway timers, stamps `tavu_slapausedon`, sets `tavu_slastatus = Paused`.
- **Resume:** when `tavu_status` changes to a status whose `tavu_pausessla = No` while `tavu_slapausedon` is set → recompute remaining business time, re-anchor targets, clear pausedon, reschedule, `tavu_slastatus = On Track`.
- **Auto-resume:** on an Inbound interaction, if the case's current status has `tavu_pausessla = Yes` and `tavu_slaautoresume = Yes`, `Pl.CaseInteraction.CaseSync` sets `tavu_status` to the `IsResumeTarget` status (In Progress) → triggers resume.
- **statecode sync:** on `tavu_status` change, set `statecode` from the status's `StateCategory`.

Single source of truth: the status row's `tavu_pausessla` column. No boolean, no mirror, no hardcoded statuscode values.

## 3. Migration surface (phased)

**Phase A — Schema (maker portal, Gustavo):**
1. Create `tavu_casestatus` + columns + seed.
2. Add `tavu_status` lookup to `tavu_case`.
3. Reduce `statuscode` to Active/Inactive (leave as vestigial); keep `statecode`.
4. Drop `tavu_slaonhold`, `tavu_setstatus`, `tavu_slapausestatuses` (if created).

**Phase B — Plugins (code, Claude):**
5. `Pl.Case.SlaAssignment`: replace the `tavu_slaonhold` steps with `tavu_status` steps; derive pause/resume from `tavu_status.tavu_pausessla` (guardrail on the pause transition). Keep the business-time recompute.
6. **statecode sync**: on `tavu_status` change, set `statecode` from `StateCategory` (small handler — likely in an existing case plugin).
7. `Pl.Case.Categorize`: set `tavu_status` (Categorized — Awaiting Assignment / Manual Review Required) instead of `statuscode`.
8. `Pl.CaseInteraction.CaseSync`: auto-resume sets `tavu_status` to the resume-target status.

**Phase C — UX (code + maker):**
9. PCF compose: replace the "En espera del cliente" checkbox with a **status dropdown** populated from `tavu_casestatus` (active statuses); on send it updates `tavu_status` and stamps the delta. Changing to Waiting on Customer drives the pause via the plugin.
10. Form: hide Status Reason, show `tavu_status` (near the SLA panel).
11. Views: refilter by `tavu_status` / `statecode`.

**Phase D — Data + docs:**
12. Data migration: map existing cases' `statuscode` → `tavu_status`.
13. Docs: rewrite service-model §6 (state model), §7 (lifecycle moments), §11.1 (pause) for the table model.

## 4. Risks / decisions

- **statuscode can't be deleted** — only minimized. Accepted; it becomes vestigial mirroring statecode.
- **Two "In Progress" semantics:** the resume-target status is flagged on the table (`IsResumeTarget`), single source.
- **Existing automations/views** keyed to statuscode must be found and repointed (Phase C/D).
- **AI lifecycle states** (New/AI Processing/Categorized/Manual Review) move into the same table as the agent-operational ones — one status axis. (Alternative considered: keep AI states in statuscode; rejected for a single clean axis.)
- Guardrail + anti-gaming governance carry over unchanged (status-driven or not).

## 5. Sequencing

A (schema) → B (plugins) → C (UX) → D (data + docs). B depends on A; test pause/resume after B before C.
