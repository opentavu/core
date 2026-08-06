# Module 3, Part B (Activity Capture): End-to-End Test Checklist

> Run top to bottom. Each test lists what to do and the expected result. Mark `[x]` when it
> passes. Related: `module3b-activity-capture-build-plan.md`, `view-definitions.md` (§22).
> Reference, `tavu_meeting` statuscode: Captured 576600001 · AI Processing 576600002 ·
> Processed 576600003 · Manual Review Required 576600004 · Reviewed 576600005 · Discarded
> 576600006. Activity statecode: Open 0 · Completed 1 · Canceled 2.

## 0. Prerequisites (confirm before testing)

- [ ] `Pl.Meeting.Capture.dll` built (Release), registered: **Create / tavu_meeting / Post-operation (40) / Asynchronous / Server**, no image.
- [ ] `Pl.Meeting.Associate.dll` registered; Custom API **tavu_AssociateMeeting** active (Global; inputs MeetingId / OpportunityId / CreateNewOpportunity / OpportunityTopic; outputs OpportunityId / DiscoveryConsolidated).
- [ ] `Pl.Meeting.BuildEmailDraft.dll` registered; Custom API **tavu_BuildMeetingEmailDraft** active (Global; input MeetingId; output EmailId).
- [ ] `tavu_meeting_form.js` uploaded; **OnLoad** wired to `OpenTavu.Meeting.Form.onLoad` (pass execution context); 4 buttons wired (Associate to opportunity / Create opportunity / Review draft email / Discard) with enable rule Status Reason = Processed OR Manual Review Required.
- [ ] AI Task Configuration **"Meeting Capture"** active (task key 576600004), gateway or Azure OpenAI configured.
- [ ] AI Task Configuration **"Meeting Follow-up Email"** active (task key 576600005) with the follow-up system prompt.
- [ ] System Settings singleton exists; `tavu_meetingconsolidateddiscovery` = Yes (or blank to test the default-on).
- [ ] `tavu_draftemail` (lookup to email) exists on `tavu_meeting`; `tavu_discoverynotes` exists on `tavu_opportunity`.
- [ ] Form has these columns (visible or hidden) so the script/control can bind: `statuscode`, `tavu_suggestedopportunity`, `tavu_summary`, `tavu_aiconfidence`, `tavu_draftemail`. AI Meeting Summary PCF control bound on `tavu_summary`.
- [ ] Test data: an existing **Contact** with a corporate email under a parent **Account**; at least one **open Opportunity** on that account (to exercise the suggestion).
- [ ] Plugin Trace Log = All, so you can read the decision path.

## 1. Capture plugin (automatic on meeting create)

> Create each meeting (manual paste in a new `tavu_meeting`), then open it and check status +
> fields + trace. The trace shows the decision path, confidence, and threshold.

- [x] **C1, Happy path (real prospect).** New meeting: Subject set, Attendees = the test contact's email, a real transcript pasted. → AI runs → **Processed**; `tavu_summary`, `tavu_discoveryextract`, `tavu_aiconfidence` (0–100) and `tavu_lastaiprocessingdate` stamped; `tavu_contact` / `tavu_account` filled from the attendee; `tavu_suggestedopportunity` set to the open opp (if the transcript points to it).
- [ ] **C2, No transcript.** New meeting with an empty `tavu_transcript`. → **Manual Review Required**; `tavu_summary` carries the reason; no AI tokens spent (trace shows the early exit).
- [ ] **C3, Attendee match.** Meeting where `tavu_contact` is left blank but Attendees contains the contact's email. → the plugin fills `tavu_contact` and its parent `tavu_account`.
- [ ] **C4, Suggested-opportunity guard.** Transcript that mentions a deal name that is NOT an open opp on the account. → `tavu_suggestedopportunity` stays empty (the AI cannot invent an opp; it only picks from the candidate list).
- [ ] **C5, AI unavailable.** Set System Settings `AI Enabled = No` (or deactivate the Meeting Capture config). Create a meeting with a transcript. → **Manual Review Required**; the meeting survives with a reason; `tavu_aiconfidence = 0`. Re-enable AI afterward.
- [ ] **C6, Confidence as %.** On a Processed meeting, the AI Meeting Summary control header shows `Confidence NN%` (whole number), amber below 70, and the low-confidence MessageBar appears when < 70.

## 2. Form + ribbon buttons (the human gate)

> Use a meeting left in **Processed** (from C1) or **Manual Review Required** (from C2/C5).

- [ ] **F1, OnLoad.** Open a Processed meeting → **gray info** banner `AI: <summary> (confidence N%)`; 4 buttons visible. Open a Manual Review meeting → **yellow warning** banner (no confidence figure). Open a closed meeting → buttons hidden, form locked (read-only banner).
- [x] **F2, Associate (accept AI suggestion).** Processed meeting with a `tavu_suggestedopportunity` → click **Associate to opportunity** → confirm the suggested opp. → meeting → **Reviewed** (Completed); `regardingobjectid` and `tavu_opportunity` = that opp; the meeting appears in the **opportunity timeline**; buttons hide; form locks.
- [ ] **F3, Associate (no suggestion → picker).** Processed meeting with `tavu_suggestedopportunity` empty → **Associate to opportunity** → opportunity picker → choose one. → same result as F2 with the chosen opp.
- [x] **F4, Create opportunity (modal, in-flow).** Meeting with a matched account/contact and no fitting opp → **Create opportunity** → confirm. → a new opportunity is created (topic from subject, customer = matched account/contact) and **opens in a centered modal**; save/close the modal → you return to the **same meeting**, refreshed; meeting → Reviewed; `tavu_opportunity` set.
- [ ] **F5, Create opportunity guard.** Meeting with **no** matched account or contact → **Create opportunity**. → clear error ("no matched account or contact"); no opportunity created; meeting stays Processed.
- [ ] **F6, Discard.** Processed meeting → **Discard** → confirm. → meeting → **Discarded** (Canceled); nothing associated; form locks. No opportunity touched.
- [ ] **F7, Guards.** On a non-reviewable meeting, a button warns "not awaiting review". With unsaved edits, warns "save your pending changes first". Unsaved new record warns "save the meeting first".

## 3. Follow-up draft email (tavu_BuildMeetingEmailDraft)

> Do these while the meeting is still **Processed** (before associating, since Associate hides
> the buttons) unless you broadened the Review-draft enable rule to include Reviewed.

- [x] **E1, Generate + review (modal).** Processed meeting with a summary/discovery, `tavu_draftemail` empty → **Review draft email**. → the draft is generated and **opens in a modal**; `tavu_draftemail` now points to it; the email is **Draft**, regarding the associated opportunity (or the contact if not associated yet), **To** = the meeting contact, **From** = you; the body recaps the meeting + next steps and signs with your name; **no em dashes**. Close the modal → back on the meeting, refreshed.
- [ ] **E2, Reopen existing draft.** Click **Review draft email** again on the same meeting. → it opens the **existing** draft in a modal; **no duplicate** email is created.
- [ ] **E3, Nothing to draft.** A meeting with empty `tavu_summary` and `tavu_discoveryextract` → **Review draft email**. → clear error ("nothing to draft from yet"); no email created.
- [ ] **E4, AI unavailable for email.** Deactivate the "Meeting Follow-up Email" config → **Review draft email**. → clear error ("AI is not available"); no email created. Reactivate afterward.

## 4. Discovery consolidation (opportunity discovery notes)

- [ ] **D1, Consolidate across sessions.** Associate a **first** meeting to an opportunity (F2/F3), then capture and associate a **second** meeting to the **same** opportunity. → after the second associate, `opportunity.tavu_discoverynotes` reflects a consolidated view of BOTH sessions' discovery extracts (`DiscoveryConsolidated = true` in the trace / API output).
- [ ] **D2, Consolidation off.** Set System Settings `tavu_meetingconsolidateddiscovery = No` → associate a meeting. → the association still succeeds; `tavu_discoverynotes` is not changed by the AI; `DiscoveryConsolidated = false`. Restore to Yes afterward.
- [x] **D3, Proposal prefill loop.** On an opportunity whose `tavu_discoverynotes` was consolidated, create a new **Proposal**. → the proposal form prefills its `tavu_discoverynotes` from the opportunity (captured meetings feed the proposal draft, no manual re-entry).

## 5. Views (config)

- [ ] **V1, Meetings to Review.** The view lists only Open meetings in Processed / Manual Review Required, Manual Review first, oldest first. Associated/discarded meetings drop out.
- [ ] **V2, Reviewed Meetings.** Closed meetings appear with the correct outcome (Reviewed vs Discarded) and the associated Opportunity.

## 6. Sign-off

- [ ] No transcript or AI gap ever loses a meeting: it degrades to Manual Review Required.
- [ ] A meeting is only ever associated / completed by an explicit human click.
- [ ] The suggested opportunity is never invented (only picked from real open opps).
- [ ] Create opportunity and Review draft email keep the user inside the meeting (modal + refresh).
- [ ] Consolidated discovery flows into the opportunity and then into the proposal draft.
- [ ] No em dashes in any AI-generated meeting text (summary, discovery, follow-up email).

## Document control

| Version | Date | Author | Notes |
|---|---|---|---|
| 0.1 | 2026-08-06 | Gustavo González Villani (with Cowork) | Initial end-to-end test checklist for Module 3 Part B (Activity Capture): capture plugin, form + ribbon buttons with in-flow modal UX, follow-up draft email, discovery consolidation, views, and sign-off. |
