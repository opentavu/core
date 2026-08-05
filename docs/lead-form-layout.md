# OpenTavu, Lead Form Layout (`tavu_lead` main form)

> Living doc for the `tavu_lead` main form design. The form is the **human gate** of Module 3
> (AI Lead Triage): the AI triages the lead, and when it needs a person it leaves the lead in
> *Awaiting Human Review* with its call in **AI Recommendation**. The reviewer reads the AI's
> verdict and acts with the ribbon buttons, they do not hand-edit the lifecycle.
> Related: `module3-lead-triage-build-plan.md`, `service-model.md`, `sales-model.md` §3.

## Design intent

Three things at a glance, left to right: **the signal that came in**, **what the AI concluded**,
and **who it might already be**. Consistent with the other primary OpenTavu forms (opportunity,
proposal), which use a **three-column** body and a **header** carrying Status Reason.

## Header (always visible)

| Slot | Field | Notes |
|---|---|---|
| Title | `tavu_subject` | Primary column |
| 1 | `statuscode` (Status Reason) | Read-only, the ribbon buttons drive transitions, not the picklist |
| 2 | `tavu_aiconfidencescore` (AI Confidence Score) | AI-set |
| 3 | `tavu_bufferalert` (Buffer Alert) | Fresh / Aging / Stale colored pill; set by the daily flow (Step E) |

## Tab "General", three columns

**Column 1, Signal Information** (the inbound data; editable so the reviewer can fix a
name/email before promoting):

`tavu_subject` · `tavu_firstname` · `tavu_lastname` · `tavu_email` · `tavu_phone` ·
`tavu_mobilephone` · `tavu_companyname` · `tavu_source` · `tavu_sourcedetails` (raw message,
tall) · `ownerid`

**Column 2, AI Assessment** (what the AI concluded; **read-only**):

`tavu_airecommendation` (the reasoning; also echoed in the OnLoad banner) · `tavu_lastaiprocessingdate`

**Column 3, Match Context** (who this might be / the outcome; **read-only**):

`tavu_matchedcontact` · `tavu_matchedaccount` · `tavu_promotedcontact` (filled after
Approve/Link) · `tavu_daysinbuffer`

## Field settings

- **Read-only (locked):** everything AI/system-derived, Status Reason, AI Recommendation,
  Last AI Processing Date, Matched Contact, Matched Account, Promoted Contact, Days in Buffer.
- **Editable:** the Signal Information column only.
- **Not on the form:** `tavu_fullname` (calculated; First + Last already shown).

## Ribbon + events (see `tavu_lead_form.js`, `OpenTavu.Lead.Form`)

- **OnLoad** → `OpenTavu.Lead.Form.onLoad` (pass execution context): surfaces the AI
  recommendation + confidence as an info banner; locks the form when the lead is closed.
- Commands, visible when the lead needs a human (*Awaiting Human Review* `576600003` **or**
  *Manual Review Required* `576600004`), Run JavaScript passing PrimaryControl:
  - **Approve & Promote** → `OpenTavu.Lead.Form.approveAndPromote` → `tavu_PromoteLead`
  - **Link to Existing** → `OpenTavu.Lead.Form.linkToExisting` → `tavu_PromoteLead` (LinkToContactId)
  - **Discard** → `OpenTavu.Lead.Form.discard` → status flip to **Not Qualified** (no master record)

## Document control

| Version | Date | Author | Notes |
|---|---|---|---|
| 1.0 | 2026-07-31 | Gustavo González Villani (with Cowork) | Initial layout doc. Records the implemented three-column form: header (Status Reason + AI Confidence + Buffer Alert), Signal Information / AI Assessment / Match Context. Fixed empty AI Assessment + Match Context sections and empty header from solution v25; kept three-column for consistency with opportunity/proposal forms. |
