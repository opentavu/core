# Proposal Lifecycle

How a `tavu_proposal` moves from Draft to a signed (or lost) proposal, and how the
header totals stay in sync. Deterministic mechanics; the AI Proposal Generator is a
separate roadmap module that will plug into the `AI Generated – Awaiting Review`
status. Grounded in the sales-model research (Draft → activate/lock → sent →
approved/lost, revise-as-new-version, one winning proposal).

## Status lifecycle

`tavu_proposal.statuscode` values:

| Status | Value | State | Notes |
|---|---|---|---|
| Draft | 576600001 | Active | Editable |
| AI Generated – Awaiting Review | 576600002 | Active | Editable (roadmap: AI writes it) |
| Under Internal Review | 576600003 | Active | **Hidden** (reversible; no button/no clear pain — un-hide + add a button if a firm needs an internal-review gate) |
| Sent to Client | 576600004 | Active | **Locked** |
| Awaiting Decision | 576600005 | Active | **Hidden** (reversible; reserved for a future follow-up feature) |
| Approved by Client | 576600006 | Inactive | Terminal (winner) |
| Rejected by Client | 576600007 | Inactive | Terminal |
| Superseded | 576600008 | Inactive | Terminal (replaced by a new version) |
| Withdrawn | 576600009 | Inactive | Terminal |

**Lifecycle is button-driven and Status Reason is read-only** (set read-only on the form;
the ribbon buttons + plugins change it via `updateRecord`/`setValue`, which bypass form
read-only). Active flow: Draft / AI Generated → **Send to Client** → Sent → **Approve /
Lost**. To edit a Sent proposal, create a new version (a fresh Draft record).

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
| Send to Client | `OpenTavu.Proposal.Form.sendToClient` | Draft / AI Generated |
| Mark as Approved | `OpenTavu.Proposal.Form.markApproved` | Sent to Client |
| Mark as Lost | `OpenTavu.Proposal.Form.markRejected` | Sent to Client |
| Create New Version | `OpenTavu.Proposal.Form.createNewVersion` | Sent / Rejected |

- **Send to Client**: sets Sent to Client + stamps Sent Date, re-applies the lock, and
  then (if the System Settings toggle `tavu_proposalemaildraftenabled` is on, default on)
  builds the client email draft and opens it in a modal OOB email dialog — see
  "Send to Client — client email draft" below.
- **Mark as Approved**: sets Approved, rolls the proposal total into the opportunity's
  `tavu_estimatedrevenue`, and offers **Close as Won** (reuses the opportunity close
  dialog `tavu_opportunityclosedialog_31702`). Single-Approved enforced server-side.
- **Mark as Lost**: sets Rejected (candidate for Create New Version).
- **Create New Version**: calls the Custom API and opens the new Draft.

## Send to Client — client email draft (AI body + branded PDF)

When a proposal is sent, OpenTavu prepares a ready-to-review client email so the seller
doesn't start from a blank page. Config-gated by
**`tavu_systemsettings.tavu_proposalemaildraftenabled`** (Yes/No, default on).

Flow (`sendToClient` → `maybeBuildEmailDraft` → `buildEmailDraft`):
1. After Sent + lock, read the toggle. If on, `showProgressIndicator("Preparing email draft…")`.
2. Call the **`tavu_BuildProposalEmailDraft`** Custom API (`ProposalId` → `EmailId`).
3. Open the returned email in a **modal dialog** (`Xrm.Navigation.navigateTo`, target 2)
   so the seller reviews/sends without leaving the proposal. On close (send or dismiss),
   the proposal is refreshed and the lock re-applied.

**`Pl.Proposal.BuildEmailDraft`** (Custom API plugin, net462):
- Reads the proposal + lines + **`tavu_companyprofile`** (branding) + logo bytes (File
  column via `InitializeFileBlocksDownload`/`DownloadBlock`).
- Resolves the recipient: `tavu_contact` → the account's primary contact → the
  opportunity's primary contact. From = current user; Regarding = the proposal.
- POSTs to `{tavu_GatewayUrl}/api/proposal/email-draft` (header
  `X-OpenTavu-Tenant-Key = tavu_GatewayKey`); gets `{subject, body, pdfBase64}`.
- Creates the `email` (Draft) + an `activitymimeattachment` with the PDF; returns `EmailId`.

**Gateway `POST /api/proposal/email-draft`** (private gateway + public `gateway-reference`, .NET 8):
- Reuses the AI path for `{subject, body}` (short email; greets the contact, signs with
  the sender; no bracketed placeholders; plain fallback if the model is off).
- Renders the PDF with **PdfSharpCore + MigraDocCore (MIT)** — logo + brand accent color,
  line-item table, subtotal/tax/total, terms. **Data-driven**: no physical template; all
  branding comes from `tavu_companyprofile`, passed in the request.

**Why the PDF renders in the gateway, not the plugin:** plugins run on **.NET Framework
4.6.2**; PdfSharpCore/ImageSharp don't load reliably in the plugin sandbox. The gateway
(.NET 8) renders cleanly. Data residency holds for firms that self-host the reference
gateway (Decision 42), and the email body already transits the gateway.

**`tavu_companyprofile`** (Organization-owned, single record) — seller branding:
`tavu_name`, `tavu_logo` (File — store a PNG, not SVG), `tavu_brandaccentcolor` (hex),
`tavu_address`/`tavu_email`/`tavu_phone`/`tavu_taxid`/`tavu_website`,
`tavu_defaultproposalterms`. Single-record via `Pl.CompanyProfile.SingleRecordGuard`;
opened by the `tavu_companyprofile_open` web resource.

**Sending** the email requires the tenant's **Server-Side Synchronization** (email server
profile + the user's mailbox approved/enabled) — M365, Google Workspace (OAuth 2.0), or a
generic SMTP host like Hostinger. Creating the draft does not; sending does. Tenant
onboarding config, not shipped by the solution.

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
- Custom API `tavu_BuildProposalEmailDraft` bound to `Pl.Proposal.BuildEmailDraft`
  (Global; request `ProposalId` String, response `EmailId` String).
- `tavu_companyprofile` (Organization-owned, single record) + register
  `Pl.CompanyProfile.SingleRecordGuard` (Create Pre-Op) + `tavu_companyprofile_open` web
  resource + sitemap link; create the single record with the firm's branding.
- `tavu_systemsettings.tavu_proposalemaildraftenabled` (Yes/No, default Yes).
- **Status Reason set to Read-only** on the proposal main form; hide the
  `Under Internal Review` and `Awaiting Decision` statuscode options.
- Gateway: deploy the `/api/proposal/email-draft` endpoint (private + reference gateways);
  each tenant configures Server-Side Sync to actually send.

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
6. **PDF libs need .NET 8** — PdfSharpCore/MigraDocCore + ImageSharp don't load reliably
   in the net462 plugin sandbox; render the PDF in the gateway, not a plugin.
7. **`data.refresh()` does not re-run OnLoad** — re-apply the lock explicitly
   (`applyLockdown`) after refresh, or a Sent proposal shows unlocked until a manual reload.
8. **Open the email as a dialog** (`navigateTo` target 2), not `openForm`, or it navigates
   away from the proposal instead of showing a review popup.
9. **MigraDocCore logo** needs the ImageSharp image-source registered once
   (`ImageSource.ImageSourceImpl = new ImageSharpImageSource<Rgba32>()`) + a PNG (not SVG);
   store the logo as a **File** column (exact bytes; Image columns downscale/thumbnail).
