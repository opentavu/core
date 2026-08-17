# OpenTavu Configuration Guide

**Configuring OpenTavu after the managed solution is imported into a client tenant.**

---

## About this guide

This guide picks up where [installation.md](installation.md) ends: the OpenTavu managed solution is imported and verified. Here you wire up AI, set tenant-level system settings, verify and extend seed data, configure security and field-level protection, configure Module 1 (Smart Case Categorization) and the SLA matrix, and finally run an end-to-end smoke test that exercises the whole system.

Work through the sections **in order**. Later sections depend on earlier ones: Module 1 needs AI wiring, the SLA engine needs calendars, and the smoke test needs everything.

> **Conventions**
> - `> 📸 **Screenshot:**` marks a point where a screenshot belongs in the published version.
> - `> ✅ **Checkpoint:**` closes each section with a concrete "you are done when" test.
> - `tavu_*` names are the Dataverse schema names; use them to locate the exact table or column.

---

## 1. Configuration overview

OpenTavu splits configuration between two homes. Understanding the split prevents the most common mistakes (looking for AI keys in the tenant, or looking for business rules in the gateway).

| Configuration | Home |
|---|---|
| AI provider endpoints, keys, deployments | **Gateway** (in gateway mode, the key never enters the client tenant) or the `tavu_aimodel` record (direct mode) |
| Task prompts, model routing, parameters | Gateway (target) or the client task config today |
| Taxonomy (case types, business line / category / subcategory) | **Client** (sent to the model in the request payload) |
| Confidence threshold, AI kill switch | **Client** (`tavu_systemsettings`) |
| Business calendars, SLAs, customer tiers | **Client** (business configuration) |
| AI output fields on the case | **Client** (the data written by Module 1) |

Recommended order, which this guide follows:

1. Wire up AI (§2)
2. System settings (§3)
3. Verify and extend seed data (§4)
4. Security roles and Field Security Profiles (§5)
5. Module 1, Smart Case Categorization (§6)
6. SLA matrix and business calendars (§7)
7. End-to-end smoke test (§8)

---

## 2. Wire up AI

OpenTavu invokes AI through a provider-agnostic `IAIProvider` interface. You choose one of two wiring paths. This decision was made during installation prerequisites; now you apply it.

### 2.1 Option A, Gateway mode (recommended)

In gateway mode the client tenant holds only a base URL and a scoped per-tenant key. The real provider keys stay in the gateway.

1. Deploy or obtain access to a gateway. OpenTavu publishes a **self-hosted reference gateway** (MIT, single-tenant, bring-your-own-model) that you deploy to Azure Functions with the client's own provider keys. Follow that gateway project's README for its deployment; it is out of scope here on purpose so this guide stays focused on the client tenant.
2. From the gateway, obtain the **base URL** and generate a **per-tenant key** for this client.
3. In the client environment, open **Solutions**, then the OpenTavu solution, then **Environment variables**, and set:
   - `tavu_GatewayUrl` to the gateway base URL.
   - `tavu_GatewayKey` to the per-tenant key (store it as a secret value).
4. When **both** variables are set, OpenTavu's `IAIProvider` resolves to the `GatewayProvider` automatically. When they are not set, it falls back to the direct providers (Option B).

   > 📸 **Screenshot:** The two environment variables `tavu_GatewayUrl` and `tavu_GatewayKey` with values set (key masked).

The plugin sends the system prompt, the case text, and the client's taxonomy to the gateway and receives the completion plus token counts. The AI key is never present in the client tenant.

### 2.2 Option B, Direct mode (simplest)

In direct mode there is no gateway; the provider key lives in the client tenant on the `tavu_aimodel` record.

1. Leave `tavu_GatewayUrl` and `tavu_GatewayKey` **unset** (so the provider does not resolve to the gateway).
2. Open the `tavu_aimodel` record for your provider and set the provider endpoint, deployment, and **key**. The default provider is Azure OpenAI; alternative providers (Anthropic Claude, Google Gemini, local models) are selected here through the same `IAIProvider` abstraction.

Direct mode is the fastest path to a working demo. The trade-off is that the provider key is stored inside the client environment, so treat that environment's access accordingly.

> ✅ **Checkpoint:** Exactly one wiring path is active. In gateway mode, both environment variables are set and point at a reachable gateway. In direct mode, the `tavu_aimodel` record holds a valid provider endpoint and key, and the gateway variables are empty. You will confirm AI actually runs in §6 and §8.

---

## 3. System settings

OpenTavu keeps tenant-level settings in a single `tavu_systemsettings` record (a singleton enforced by the `Pl.SystemSettings.SingleRecordGuard` plugin).

1. Open the **System Settings** entry (Configuration area, or the settings web resource). If no record exists yet, create one; the guard ensures only one can exist.
2. Set **Customer Mode** (`tavu_customermode`) to match the client firm:
   - `B2B_Only` for firms that sell to companies (customer is an `account`).
   - `B2C_Only` for firms that sell to individuals (customer is a `contact`).
   - `Mixed` for firms that do both.
   This flag drives the behavior of the polymorphic `tavu_customer` lookup on opportunities and cases. Changing it later (for example B2B_Only to Mixed) does not affect existing records and needs no downtime.
3. Set the **AI Confidence Threshold** (`tavu_aiconfidencethreshold`). Default is **0.85**, and it is **inclusive**: a case scoring exactly 0.85 is auto-processed; below 0.85 it goes to Manual Review. Raise it for more caution (more human review), lower it for more automation.
4. Confirm the **AI Enabled** master kill switch (`tavu_aienabled`) is **Yes**. When set to No, cases skip AI entirely and route straight to Manual Review (graceful degradation), which is useful during maintenance but must be On for Module 1 to run.

   > 📸 **Screenshot:** The System Settings record showing Customer Mode, AI Confidence Threshold, and AI Enabled.

> ✅ **Checkpoint:** Exactly one `tavu_systemsettings` record exists. Customer Mode matches the client firm type, the confidence threshold is set (0.85 unless the client asked otherwise), and AI Enabled is Yes.

---

## 4. Verify and extend seed data

OpenTavu ships reference data pre-loaded in the managed solution. Verify it imported, then extend it for the client **without editing the shipped defaults** (so future upgrades stay clean).

### 4.1 Case types (`tavu_casetype`)

Confirm the seven default case types are present and active:

| Name | Code | Default Priority |
|---|---|---|
| General Inquiry | GEN | Standard |
| Support Request | SUP | Standard |
| RFP / Proposal Inquiry | RFP | Expedited |
| Billing Inquiry | BIL | Standard |
| Scope Change Request | SCO | Expedited |
| Complaint | CMP | Critical |
| Other | OTH | Standard |

To add a client-specific type (for example a QA boutique's "Bug Report" or "Test Cycle"), create a new `tavu_casetype` row with its own name, code, and default priority. Each case type also carries an `tavu_aihint` used by Module 1; you fill that in §6.

### 4.2 Customer tiers (`tavu_customertierdefinition`)

Confirm the three default tiers:

| Name | Sort Order | Meaning |
|---|---|---|
| Standard | 100 | Default tier for regular clients |
| Premium | 50 | Extended SLA or preferred contracts |
| Strategic | 10 | Top tier, maximum priority |

Add tiers as needed (for example a "Trial" tier for a software QA boutique). Lower sort-order values indicate higher priority.

### 4.3 Other seed data

- **Units of measure** (`tavu_uom`) and the product/pricing catalog ship pre-loaded for the quotation model.
- **Geography** seed (country, state/province, city) ships pre-loaded.
- **Business calendars and holidays are not shipped as canonical seed**; they are per-client configuration you create in §7.

> ✅ **Checkpoint:** The seven case types and three customer tiers are present and active, plus any client-specific rows you added. Shipped defaults are untouched.

---

## 5. Security roles and Field Security Profiles

OpenTavu separates two concerns: which users can use the app (security roles) and which users can see sensitive money fields (Field Security Profiles).

### 5.1 Security roles

Assign the appropriate Dataverse security roles to each user so they can access the OpenTavu tables and app. Map the client's people to the operational roles OpenTavu assumes:

- **Sellers / consultants:** daily work on contacts, opportunities, proposals, cases, and time entries.
- **Sales Manager / Operations Manager:** everything sellers do, plus visibility into cost and margin (see §5.2).
- **Administrators:** setup and configuration (system settings, case types, tiers, SLAs, calendars).

Assign roles from **Power Platform admin center → Environment → Settings → Users + permissions → Users**, or from the modern in-app user management.

### 5.2 Field Security Profile: hide cost and margin from sellers

OpenTavu's quotation model carries internal cost and margin fields that **must not** be visible to regular sellers. These are protected by a **Field Security Profile**, which masks the field at the platform level even if a view or form includes the column.

The fields to protect:

| Field | Where |
|---|---|
| `tavu_grossmargin` | Proposal header |
| `tavu_totalcost` | Proposal header |
| `tavu_linecost` | Proposal line |
| `tavu_unitcost` | Proposal line |
| `tavu_cost` | Product (internal unit cost) |
| `tavu_costrate` | Service role (internal cost per hour) |

Configuration:

1. Go to **Power Platform admin center → Environment → Settings → Security → Field security profiles** (or the classic settings area).
2. Create or open the OpenTavu profile.
3. For each field above, confirm it is set to secured, then grant **Read** on the profile only to the **Operations Manager** and **Sales Manager** roles' users.
4. Do **not** add sellers to this profile. A user not on the profile sees these fields blanked.

   > 📸 **Screenshot:** The Field Security Profile listing the six protected fields, with Read granted to the manager users.

> ✅ **Checkpoint:** Sign in (or use "view as") a **seller-role test user** and open a proposal with lines. The cost and margin columns are **blank**. Sign in as a manager and confirm the same fields are **visible**. If a seller can see cost or margin, the profile is misconfigured; fix it before go-live.

---

## 6. AI task configurations (Module 1 and the other live AI tasks)

Module 1 categorizes each incoming case, writes a confidence score and reasoning, and either auto-applies the result or flags the case for human review. It runs as the `Pl.Case.Categorize` plugin (asynchronous, on create of `tavu_case`).

### 6.1 Confirm AI wiring and the task prompt

1. Confirm AI is wired (§2) and AI Enabled is Yes (§3).
2. Review the Module 1 **task configuration** (`tavu_aitaskconfiguration`): the system prompt and model parameters used for categorization. In gateway mode these move to the gateway over time; today the prompt is built in the client and sent in the request payload. Adjust the prompt only if the client needs different categorization behavior, and keep it in the same language as the case content.

### 6.2 Confidence threshold behavior

Module 1 compares the model's confidence against `tavu_aiconfidencethreshold` from System Settings (§3):

- Score **at or above** the threshold, single clear intent: the categorization is **auto-applied**.
- Score **below** the threshold, or multiple intents detected, or any failure: the case is routed to **Manual Review Required** (the `tavu_ismanualreview` flag is set), so a human confirms, corrects, or splits it.

The fields Module 1 writes on each case:

| Field | Meaning |
|---|---|
| `tavu_aiconfidencescore` | 0 to 1 confidence score |
| `tavu_aisummary` | One-line executive summary |
| `tavu_aireasoning` | Chain-of-thought audit trail |
| `tavu_aisentiment` | Calm, Concerned, Frustrated, Critical, or Unknown |
| `tavu_multiintentdetected` | Yes when the case contains more than one request |

These render in the `AiAssessment` PCF panel on the case form.

### 6.3 Write a good `tavu_aihint` per case type

Each `tavu_casetype` carries an `tavu_aihint`: a short instruction that helps the model tell this type apart from similar ones. Fill it in for every active case type.

A good hint describes what genuinely distinguishes the type and gives an example. For instance, on **RFP / Proposal Inquiry** a hint might read: "Use for inbound requests to bid on or respond to a formal RFP, RFQ, or tender, including deadlines and scope documents. Do not use for general pricing questions, which are Billing Inquiry." Keep hints concise and unambiguous; vague hints reduce accuracy.

> 📸 **Screenshot:** A `tavu_casetype` form showing the `tavu_aihint` field filled in.

> ✅ **Checkpoint:** Every active case type has a written `tavu_aihint`. On a test case, the `AiAssessment` panel renders. You confirm live categorization end-to-end in §8.

---

### 6.4 The shared AI task configuration table

All of OpenTavu's AI features are configured the same way: one row per task in `tavu_aitaskconfiguration`, holding the model (lookup to `tavu_aimodel`), temperature, confidence threshold, max output tokens, and the prompt. Module 1 above is one such row; the tasks below are the others that ship live. Verify each row exists and points at a model your AI wiring (section 2) can reach.

| Task | Covered in |
|---|---|
| Case Categorization | Module 1 (6.1 to 6.3) |
| Lead Triage | 6.5 |
| Meeting Capture | 6.6 |
| Meeting Follow-up Email | 6.6 |
| Proposal email | 6.7 |

### 6.5 Lead Triage

`Pl.Lead.Triage` runs on each new `tavu_lead` (anonymous inbound). The model reads the lead, matches it against existing contacts and accounts, and recommends promote, link, or discard, writing the recommendation for the salesperson. Creating a brand new contact or account from anonymous inbound always requires a one-click human approval through the `tavu_PromoteLead` action. Configure its `tavu_aitaskconfiguration` row (model, temperature, confidence threshold) the same way as Module 1.

> ✅ **Checkpoint:** create a test `tavu_lead`; the AI recommendation appears and the Approve / Link / Discard ribbon works.

### 6.6 Meeting Capture and follow-up email

`Pl.Meeting.Capture` runs when a meeting transcript is captured. It extracts the discovery notes with AI, flags potential clients and their company, and lets the rep create an opportunity (need, contact, account) from the meeting with one button (`tavu_AssociateMeeting`). It also drafts a follow-up email for the rep to review and send. Two `tavu_aitaskconfiguration` rows drive this: Meeting Capture (extraction) and Meeting Follow-up Email (the draft).

Transcript source: the MVP supports Teams native transcripts plus manual paste as a first-class fallback. To use Teams, configure the source on the `tavu_meetingsource` Teams row (the setup wizard probes the connection through the `tavu_ProbeMeetingSource` API). Native Teams transcripts require a paid M365 tier; manual paste works on any tier and doubles as the test harness.

> ✅ **Checkpoint:** capture or paste a test transcript; the AI extraction populates, the create-opportunity button works, and a follow-up draft is produced.

### 6.7 Proposal Send-to-Client email

On **Send to Client**, OpenTavu drafts the client email (AI body grounded in the proposal) and attaches a branded PDF, then opens it for the seller to review and send. Gated by `tavu_systemsettings.tavu_proposalemaildraftenabled` (Yes/No, default on). Confirm the toggle is on and that the company profile used for the branded PDF is populated.

> ✅ **Checkpoint:** on a Sent proposal, the email draft opens with the branded PDF attached.

## 7. SLA matrix and business calendars

When a case is categorized, the `Pl.Case.SlaAssignment` plugin looks up the SLA for the case's **Tier and Type**, then computes response and resolution target dates against a **business calendar**. Configure calendars first, then the SLA matrix.

### 7.1 Business calendars

Calendars define working schedules so SLA clocks respect business hours and holidays. Three tables work together:

**`tavu_businesscalendar`** (schedule header): create one per distinct schedule (for example "Standard 8x5" and "Strategic 24x7"). Key fields:

- **Time Zone** for the calendar.
- **Is 24x7** (`tavu_is24x7`): Yes means the clock runs continuously and no working-hours rows are needed (only closures pause it).
- **Is Default**: the calendar used when an SLA does not specify one.

**`tavu_calendarworkinghours`** (working intervals, child of the calendar): one or more rows per weekday. Split shifts (for example a lunch break) are supported by adding multiple rows for the same day. Start Time (`tavu_starttime`) and End Time (`tavu_endtime`) are a "Time of Day" choice whose **option value is minutes from midnight** (540 = 09:00, 1080 = 18:00).

**`tavu_businessclosure`** (holidays and closures): one row per closed date. A closure with no calendar applies to all calendars; a closure with a calendar applies only to that one.

> 📸 **Screenshot:** A business calendar with its working-hours rows (Mon to Fri, 540 to 1080) and a couple of closure dates.

### 7.2 SLA matrix (`tavu_sla`)

Create one `tavu_sla` row per **Tier x Case Type** combination the client wants to govern. Key fields:

- **Tier** and **Type** (the matrix axes).
- **Response Target Hours** (`tavu_responsetargethours`): time to first response.
- **Resolution Target Hours** (`tavu_resolutiontargethours`): time to resolution.
- **Calendar** (`tavu_calendar`): the business calendar this SLA counts against. If left null, the Default calendar is used. (The older `tavu_coveragehours` choice is deprecated; use the calendar lookup.)

Start with the combinations that matter most (for example Strategic + Complaint gets the tightest targets on a 24x7 calendar; Standard + General Inquiry gets relaxed targets on an 8x5 calendar) and expand as the client's service policy requires. The reference README lists example counts by firm size (a small B2B consultancy needs roughly three SLA rows; a QA boutique roughly twelve).

### 7.3 How targets are computed

On categorization, `Pl.Case.SlaAssignment` resolves the SLA, then walks the calendar forward from the case's `createdon`, consuming the target hours only inside working intervals and skipping nights, weekends, gaps, and closure dates (with `Is 24x7 = Yes`, the clock is continuous). The results are written to **Response Target Date** (`tavu_responsetargetdate`) and **Resolution Target Date** (`tavu_resolutiontargetdate`), and **SLA Status** (`tavu_slastatus`) is set to On Track. The gateway's SLA Scheduler then holds durable timers that fire at the warning and breach moments. The `SlaCountdown` PCF shows a live countdown on each target. Anchoring to `createdon` means a re-categorization recomputes fairly from the customer's arrival time. The status choice values are fixed across tenants: On Track, Warning, Breached, Met, Paused.

> ✅ **Checkpoint:** At least one business calendar exists (with a Default marked), and the SLA matrix has rows for the client's key Tier x Type combinations, each pointing at a calendar. You confirm target dates compute correctly in §8.

---

## 8. End-to-end smoke test

This exercises the full loop: an anonymous inbound lead becomes a contact, an opportunity is created, and a case is categorized with an SLA applied. Run it in the dev or sandbox environment before promoting to production.

### Step 1, Create a lead

Create a `tavu_lead` record with realistic inbound data (name, company, a short message). This is the ingestion buffer for anonymous inbound.

**Expected:** the lead is created with an Active state. If Module 3 hygiene is enabled in the deployment, it may propose a recommendation (`tavu_airecommendation`); if not, you proceed manually.

### Step 2, Promote the lead to a contact

Use **Approve & Promote** on the lead. This creates the `contact` (and an `account` when appropriate) from the lead's data.

**Expected:** a new `contact` (plus `account` for a company) is created, the lead moves to **Inactive / Promoted to Contact**, and `tavu_promotedcontact` points at the new contact. Creating a new master record from anonymous inbound always requires this one human confirmation.

> 📸 **Screenshot:** The lead after Approve & Promote, showing Inactive / Promoted to Contact and the linked contact.

### Step 3, Create an opportunity

Create a `tavu_opportunity` and set its **Customer** (`tavu_customer`) to the contact or account from Step 2, consistent with the Customer Mode you set in §3.

**Expected:** the `Pl.Opportunity.CustomerSync` plugin mirrors the polymorphic customer into the typed `tavu_account` / `tavu_contact` lookups so Quick View forms load. The opportunity opens in an Open state with a sales stage.

### Step 4, Create a case and watch Module 1

Create a `tavu_case` for that customer with a realistic message (for example a support request or a complaint). Save it.

**Expected (after the async plugin runs, usually seconds):**

- The `AiAssessment` panel shows a categorization with a **confidence score** (`tavu_aiconfidencescore`), a **summary** (`tavu_aisummary`), **sentiment** (`tavu_aisentiment`), and **multi-intent** flag (`tavu_multiintentdetected`).
- If confidence is at or above the threshold and intent is single: the categorization is **auto-applied**.
- If below threshold or multi-intent: the case is at **Manual Review Required** (`tavu_ismanualreview` = Yes).

### Step 5, Confirm the SLA applied

On the same case, confirm the SLA engine ran.

**Expected:** **Response Target Date** and **Resolution Target Date** are populated, computed against the matching calendar; **SLA Status** is On Track; and the `SlaCountdown` bars show a live countdown.

> 📸 **Screenshot:** A categorized case showing the AiAssessment panel populated and both SlaCountdown bars counting down.

### Troubleshooting quick reference

| Symptom | Likely cause | Fix |
|---|---|---|
| AI never runs; case goes straight to Manual Review | AI Enabled is No, or AI not wired | Set `tavu_aienabled` = Yes (§3); confirm wiring (§2) |
| Categorization errors or empty | Gateway unreachable or `tavu_aimodel` key invalid | Re-check `tavu_GatewayUrl`/`tavu_GatewayKey` or the `tavu_aimodel` key |
| No target dates on the case | No `tavu_sla` row for that Tier x Type, or no calendar | Add the SLA row and a Default calendar (§7) |
| Everything goes to Manual Review | Threshold set too high, or weak `tavu_aihint`s | Review `tavu_aiconfidencethreshold` (§3) and the hints (§6.3) |
| Seller can see cost or margin | Field Security Profile misconfigured | Re-check §5.2; seller must not be on the profile |
| Quick View for customer is blank | CustomerSync did not run | Confirm `Pl.Opportunity.CustomerSync` / `Pl.Case.CustomerSync` are registered |

> ✅ **Checkpoint:** All five steps pass in sequence: lead promoted, opportunity created with a synced customer, case categorized with confidence and sentiment, and SLA target dates with a live countdown. The deployment is functional end-to-end.

---

## 9. Configuration checklist

A one-page recap to tick through per deployment:

- [ ] AI wired: gateway mode (`tavu_GatewayUrl` + `tavu_GatewayKey`) **or** direct mode (`tavu_aimodel` key)
- [ ] `tavu_systemsettings`: one record, Customer Mode set, confidence threshold set (0.85 default), AI Enabled = Yes
- [ ] Seed data verified: 7 case types, 3 customer tiers; client-specific rows added without editing defaults
- [ ] Security roles assigned to all users
- [ ] Field Security Profile protects `tavu_grossmargin`, `tavu_totalcost`, `tavu_linecost`, `tavu_unitcost`, `tavu_cost`, `tavu_costrate`; sellers excluded, verified with a test user
- [ ] Module 1: task prompt reviewed, `tavu_aihint` written for every active case type
- [ ] Calendars: at least one, with a Default; working hours and closures set
- [ ] SLA matrix: rows for the client's key Tier x Type combinations, each with a calendar
- [ ] End-to-end smoke test passed (lead → contact → opportunity → case → categorization + SLA)

Once every box is ticked in the dev or sandbox environment, promote the deployment to production and re-run the smoke test there.
