# Module 3, Lead Triage: End-to-End Test Checklist

> Run top to bottom. Each test lists what to do and the expected result. Mark `[x]` when it
> passes. Related: `module3-lead-triage-build-plan.md`, `lead-form-layout.md`.
> Reference values, statuscode: New 576600001 · AI Processing 576600002 · Awaiting Human
> Review 576600003 · Manual Review Required 576600004 · Promoted to Contact 576600005 ·
> Discarded as Noise 576600006 · Duplicate 576600007 · Not Qualified 576600008 · Stale
> 576600009. Buffer alert: Fresh 576600000 · Aging 576600001 · Stale 576600002.

## 0. Prerequisites (confirm before testing)

- [ ] `Pl.Lead.Triage.dll` built (Release), registered: **Create / tavu_lead / Post-operation (40) / Asynchronous / Server**, no image.
- [ ] `Pl.Lead.PromoteLead.dll` registered; Custom API **tavu_PromoteLead** active (Global; inputs LeadId/LinkToContactId/LinkToAccountId; outputs ContactId/AccountId).
- [ ] `tavu_lead_form.js` uploaded; **OnLoad** wired to `OpenTavu.Lead.Form.onLoad` (pass execution context); 3 buttons wired (Approve & Promote / Link to Existing / Discard), visible only on Awaiting Human Review.
- [ ] System Settings singleton record exists with **Lead buffer aging days = 7**, **stale days = 14** (or left blank to test defaults).
- [ ] AI Task Configuration "Lead Triage" active; gateway configured **or** you expect the degrade-to-Manual-Review path.
- [ ] Flow **OpenTavu, Lead Buffer Daily Maintenance** imported and **turned On**.
- [ ] Test data: at least one existing **Contact** (with a parent **Account**) and one **Account** with a corporate domain, to exercise matches.
- [ ] Plugin Trace Log enabled (Settings → Auditing/Plug-in trace = All) so you can read the decision path.

## 1. Triage plugin, automatic triage on lead create

> Create each lead (form or a Power Automate test), then open it and check status + fields +
> the trace log. The trace should show the decision path, confidence, and threshold.

- [ ] **T1, Exact contact email match.** New lead with `Email` = an existing contact's email. → **Inactive / Promoted to Contact**; `Matched Contact` set; `Matched Account` = that contact's parent; recommendation says "deterministic exact email match"; **zero AI tokens** (trace shows deterministic hit, no AI call).
- [ ] **T2, Junk / no-reply.** New lead with `Email` = `no-reply@example.com` (or empty subject/body). → **Inactive / Discarded as Noise**; no AI call.
- [ ] **T3, Duplicate.** Create a second **active** lead with the same email as another open lead. → the newer one → **Inactive / Duplicate**.
- [ ] **T4, Corporate-domain match.** New lead with email `@<domain>` matching an existing Account (non-free domain, i.e. not gmail/hotmail). → **Awaiting Human Review**; `Matched Account` set; recommendation "Promote, create new contact under matched account"; no AI needed.
- [ ] **T5, Unknown real prospect.** New lead, brand-new email + company, no match. → AI runs → **Awaiting Human Review**; recommendation "Promote, create new"; `AI Confidence Score` + `AI Recommendation` + `Last AI Processing Date` stamped; **no Contact/Account created**.
- [ ] **T6, AI unavailable.** Temporarily disable AI (System Settings `AI Enabled = No`, or no active Lead Triage config). Create a lead that would need AI (as T5). → **Manual Review Required**; the lead survives with a reason. Re-enable AI afterward.

## 2. Form + ribbon buttons, the human gate

> Use a lead left in **Awaiting Human Review** (from T4/T5) or set one there manually.

- [ ] **F1, OnLoad.** Open an Awaiting Human Review lead. → info banner shows `AI: <recommendation> (confidence N%)`; the 3 buttons are **visible**. Open a closed lead → buttons hidden, form locked (read-only banner).
- [ ] **F2, Approve & Promote (new account).** Lead with a Company Name and **no** Matched Account → click Approve & Promote → confirm. → new **Account** created from company name; new **Contact** under it (name/email/phone from the lead); the new contact form opens; lead → **Promoted to Contact**; `Promoted Contact` set. No duplicate account.
- [ ] **F3, Approve & Promote (matched account).** Lead with `Matched Account` already set → Approve & Promote. → new **Contact under that existing account**; **no** new account created.
- [ ] **F4, Approve & Promote (no company).** Lead with no company and no matched account → Approve & Promote. → Contact created with **no parent account**; lead Promoted.
- [ ] **F5, Link to Existing.** Click Link to Existing → contact picker → choose an existing contact → confirm. → **no** new Contact/Account (check totals before/after); lead → **Promoted to Contact**; `Promoted Contact` = the chosen contact.
- [ ] **F6, Discard.** Click Discard → confirm. → lead → **Inactive / Not Qualified**; nothing created; form locked.
- [ ] **F7, Guards.** On a non-Awaiting lead, any button warns "not awaiting review". With unsaved edits, warns "save your pending changes first". A closed lead opens locked.
- [ ] **F8, Idempotency.** On an already-promoted lead (if you can re-invoke), Approve does **not** create a duplicate contact, it returns the existing one.

## 3. Step E, daily buffer maintenance flow

> Run the flow manually (Flow → Run) instead of waiting for the schedule.

- [ ] **E1, Buffer fields refresh.** Run the flow. → every **active** lead gets `Days In Buffer` computed and `Buffer Alert` set (Fresh/Aging/Stale pill).
- [ ] **E2, Band boundaries.** Verify the pill matches the age against aging=7 / stale=14. **Tip:** `createdon` can't be backdated, so to force bands temporarily set aging/stale days low (e.g. 0/0) in System Settings, run the flow, confirm leads flip to Aging/Stale, then restore 7/14.
- [ ] **E3, Auto-stale.** With `stale days` temporarily = 0, an **Awaiting Human Review** lead → after the run → **Inactive / Stale** (576600009). Restore to 14 after.
- [ ] **E4, Non-awaiting leads are not auto-closed.** A lead in **New** (not Awaiting Human Review), even past the stale threshold, keeps its status but still gets `Days In Buffer` / `Buffer Alert` updated. (Only Awaiting Human Review leads auto-close.)
- [ ] **E5, Parameterization.** Change `stale days` in System Settings to a new value → rerun → behavior follows the new number. Clear the settings fields → rerun → falls back to defaults 7/14 (the flow's `coalesce`).

## 4. Sign-off

- [ ] Deterministic matches spend zero AI tokens (confirmed in trace).
- [ ] A new master record is **never** created without the human clicking Approve & Promote.
- [ ] Any failure degrades to Manual Review Required, no lead is ever lost.
- [ ] Buffer alert + auto-stale honor the System Settings values.
