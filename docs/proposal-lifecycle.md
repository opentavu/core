# Proposal Lifecycle

How a `tavu_proposal` moves from Draft to a signed (or lost) proposal, and how the
header totals stay in sync. Deterministic mechanics; the AI Proposal Generator is a
separate roadmap module that will plug into the `AI Generated – Awaiting Review`
status. Grounded in the sales-model research (Draft → activate/lock → sent →
approved/lost, revise-as-new-version, one winning proposal).

## Status lifecycle

`tavu_proposal.statuscode` values:

| Status | Value | State | Editable? |
|---|---|---|---|
| Draft | 576600001 | Active | Yes |
| AI Generated – Awaiting Review | 576600002 | Active | Yes (roadmap: AI writes it) |
| Under Internal Review | 576600003 | Active | Yes |
| Sent to Client | 576600004 | Active | **Locked** |
| Awaiting Decision | 576600005 | Active | **Locked** (optional step) |
| Approved by Client | 576600006 | Inactive | Terminal (winner) |
| Rejected by Client | 576600007 | Inactive | Terminal |
| Superseded | 576600008 | Inactive | Terminal (replaced by a new version) |
| Withdrawn | 576600009 | Inactive | Terminal |

Advancement is button-driven (see Ribbon commands); the Status Reason picklist is not
the intended path to advance.

## Components

**`Pl.Proposal.LifecycleTracker`** (Pre-Op on `tavu_proposal` Create/Update):
- Create: defaults `tavu_version` to `v1`.
- Transition guard: can't reach Approved/Rejected without having been Sent; terminal
  statuses (Approved/Superseded/Withdrawn) can't change further.
- Lock: once Sent/Awaiting/closed, blocks edits to **business (`tavu_*`) fields**;
  system/standard fields (statecode, statuscode, modifiedon, ownerid, tavu_sentdate)
  pass through. To edit a locked proposal, create a new version.
- Single winner: only one Approved proposal per opportunity.
- Registration: Create + Update Pre-Op, no filtering attributes on Update (must fire
  on any field so the lock catches everything), Pre-Image `PreImg` = `statecode,
  statuscode, tavu_opportunity`.

**`Pl.ProposalLine.Calculator`** (Pre-Op/Post-Op on `tavu_proposalline`):
- Computes line money fields + rolls up header totals (existing).
- Added: `GuardParentLock` — blocks line create/update/delete when the parent proposal
  is locked (reuses existing steps).

**`Pl.Proposal.CloneVersion`** — implements the **`tavu_CloneProposalVersion` Custom
API** (Global action; request `ProposalId` String, response `NewProposalId` String).
Clones the header + active lines into a new Draft, increments `tavu_version`,
supersedes the source. Copies the already-computed totals because Calculator/
LifecycleTracker auto-abort at depth 2 (MaxDepth = 1).

**`WebResources/js/tavu_proposal_form.js`** — form UX (see below).

## Ribbon commands (Run JavaScript, param PrimaryControl)

| Button | Function | Visibility (statuscode) |
|---|---|---|
| Send to Client | `OpenTavu.Proposal.Form.sendToClient` | Draft / AI Generated / Under Internal Review |
| Mark as Approved | `OpenTavu.Proposal.Form.markApproved` | Sent to Client / Awaiting Decision |
| Mark as Lost | `OpenTavu.Proposal.Form.markRejected` | Sent to Client / Awaiting Decision |
| Create New Version | `OpenTavu.Proposal.Form.createNewVersion` | Sent / Awaiting / Rejected |

- **Send to Client**: sets Sent to Client + stamps Sent Date, then refreshes → the
  lock applies immediately.
- **Mark as Approved**: sets Approved, rolls the proposal total into the opportunity's
  `tavu_estimatedrevenue`, and offers **Close as Won** (reuses the opportunity close
  dialog `tavu_opportunityclosedialog_31702`). Single-Approved enforced server-side.
- **Mark as Lost**: sets Rejected (candidate for Create New Version).
- **Create New Version**: calls the Custom API and opens the new Draft.

## Form events (designer → handler; pass execution context)

| Event | Handler | Purpose |
|---|---|---|
| OnLoad | `OpenTavu.Proposal.Form.onLoad` | Visual lock + **captures the parent form (`_parentForm`)** + wires the subgrid refresh |
| OnChange `statuscode` | `OpenTavu.Proposal.Form.onStatusReasonChange` | Re-applies the visual lock as status changes |
| Grid (lines) OnSave | `OpenTavu.Proposal.Form.onLineGridSave` | Refreshes header totals after inline add/edit |

> **Critical dependency:** the form **OnLoad handler must be registered**. Both the
> visual lock and the totals auto-refresh depend on `_parentForm`, which is only set in
> OnLoad. If both stop working at once, the OnLoad handler is the first suspect.

## Grid & totals auto-refresh

The Proposal Lines subgrid uses the **Power Apps grid control** (modern, not the
deprecated Editable Grid) with **Enable editing = Yes**.

Header rollup totals are computed server-side by the Calculator plugin. To reflect
them on the form without a manual refresh, two hooks combine (there is no single
first-party "row CRUD" event on the modern grid):

- **Grid OnSave** (`onLineGridSave`) → covers inline **add/edit**. OnSave fires before
  the row commits, so the handler re-reads the 5 totals twice (1.5 s and 3.5 s) via a
  lightweight `retrieveRecord` + `setValue` (`setSubmitMode("never")`) — no full-form
  refresh, no "Save & Continue" dialog.
- **Subgrid `addOnLoad` + row-count change** (`onSubgridLoad`) → covers **add/delete**
  (the grid reloads and the count changes).

Both use `_parentForm` (the grid OnSave context's `getId()` is the line, not the
proposal).

## Registration / config checklist

- `tavu_version` (Single Line of Text) column on `tavu_proposal`.
- Register `Pl.Proposal.LifecycleTracker` (Create + Update Pre-Op; Update Pre-Image
  `PreImg` = statecode, statuscode, tavu_opportunity; no filtering attributes).
- Update assembly for `Pl.ProposalLine.Calculator` (line-lock).
- Custom API `tavu_CloneProposalVersion` bound to `Pl.Proposal.CloneVersion`.
- Proposal Lines subgrid → Power Apps grid control, Enable editing = Yes.
- Form events wired: OnLoad → onLoad, OnChange statuscode → onStatusReasonChange,
  Grid OnSave → onLineGridSave.
- Ribbon buttons with the visibility rules above.
- Proposals subgrid on the opportunity: view shows all states (not Active-only) so
  approved/superseded proposals stay visible.

## Gotchas learned

1. **Modern Power Apps grid events fire inconsistently** for row CRUD (documented). The
   OnSave hook does fire on inline edits; add/delete are caught by the addOnLoad
   row-count net. A fully first-party single "data changed" event does not exist (even
   D365 Sales lacks it) — a custom PCF grid would be the only single-hook alternative.
2. **Grid OnSave context ≠ parent form.** `executionContext.getFormContext().data
   .entity.getId()` returns the LINE id. Use the `_parentForm` captured in OnLoad.
3. **Classic Read-only/Editable grids are deprecated (March 2026)** — do not revert to
   them; use the Power Apps grid control (editable) instead.
4. **State transitions need both statecode + statuscode** together (setting a terminal
   statuscode alone is rejected). The buttons set both via `Xrm.WebApi.updateRecord`.
5. **`tavu_proposalcontent`** (not `tavu_content`); **no `tavu_documenttype`** — the
   sales-model §8.3 mentioned both; only `tavu_proposalcontent` is real.
