# Module 3, Lead Triage: Build Plan & Handoff Context

> **Purpose of this file.** Self-contained context to build OpenTavu's Module 3 (AI Lead
> Triage) in a *fresh* Claude project. It captures the design decision already made
> (Option A), the exact repo patterns to mirror, what the human must provide, and the
> ordered build steps. Read the "Repo files to read first" section before writing any code.
> Do **not** invent schema names or option values, verify them in the environment.

---

## 0. Where this fits

OpenTavu is an AI-first CRM accelerator on **Microsoft Dataverse / Power Platform** for
professional-services SMBs. It has **two entry paths**:

- **Path A, Direct (the common case):** a consultant creates `account` + `contact`
  directly. `tavu_lead` is **never** used here.
- **Path B, Anonymous inbound (the exception):** a web form, a generic `info@`/`sales@`
  email, a LinkedIn message from someone not in the CRM, or a partner referral with
  incomplete data. Power Automate drops these into the **`tavu_lead` buffer** so
  low-quality anonymous inbound does **not** pollute the clean master Contact/Account DB.

**Module 3 = the AI that triages that `tavu_lead` buffer.** It is the third of OpenTavu's
initial AI modules (after Module 1 Smart Case Categorization and Module 2 Context-Aware
Customer Communication). Design source of truth: `core/docs/sales-model.md` §3 (Path B),
especially §3.2 (schema), §3.3 (flow), §3.4 (rationale).

**Design mindset (applies to every decision):** pain-point-driven, **AI-first (AI replaces
the human step; the human is only the 2nd-line reviewer on the irreversible write)**,
simplicity as advantage, configuration-over-code. If a proposed field/step doesn't map to a
pain point or isn't AI-first, cut it.

---

## 1. The decision already made, Option A (do not re-litigate)

Module 3 processing has a **deterministic-first, AI-second** pipeline and a strict
**promotion boundary** that protects the clean master DB:

**Step 2, Module 3 processing (target < 2 min), in order:**

1. **Deterministic first (zero AI tokens):**
   - Exact **email** match to an existing `contact`? → match found.
   - Exact **domain** match to an existing `account`? → match found.
   - Obvious junk (`no-reply@`, spam patterns, empty body)? → `Inactive / Discarded as Noise`.
   - Duplicate of another **open** lead (same email)? → `Inactive / Duplicate`.
2. **AI only when the deterministic pass is inconclusive:** fuzzy match (name/company
   variants), real-prospect-vs-noise judgement, field extraction → fills
   `tavu_aiconfidencescore` + `tavu_airecommendation`.

**Promotion boundary (Option A):**

- **Match to an EXISTING contact/account** (deterministic, *or* AI fuzzy ≥ threshold) →
  **auto-link** (`tavu_matchedcontact` / `tavu_matchedaccount`), also set `tavu_promotedcontact`
  = the matched contact so Promoted Contact is reliable across auto-link and human paths,
  notify owner, set `Inactive / Promoted to Contact`. Safe and reversible: **no new master
  record created.**
- **No existing match but looks like a real prospect** → `Awaiting Human Review`,
  `tavu_airecommendation = "Promote, create new"`. **Creating a brand-new account/contact
  from anonymous inbound is the one write to the clean master DB, so it ALWAYS needs a
  human's one-click Approve (Step 3), regardless of confidence.**
- **Low confidence / ambiguous** → `Awaiting Human Review`, notify.
- **Below the noise floor** → `Inactive / Discarded as Noise`.

**Step 3, Human decision (only when Awaiting Human Review):** one click, **Approve &
Promote** (creates contact + account from the lead, sets `Inactive / Promoted to Contact`),
**Link to existing** (point to the right record, same inactive status), or **Discard**.

**Step 4, Auto-cleanup (scheduled Power Automate, daily):** recompute
`tavu_daysinbuffer`, set `tavu_bufferalert` (Fresh/Aging/Stale), and move
`Awaiting Human Review` leads older than 14 days to `Inactive / Stale`.

**Why:** AI does the work (dedup + match + extract + recommendation); the human is the
2nd-line reviewer **only** on the single irreversible action (creating a new master record).

---

## 2. Repo files to read FIRST (mirror these; don't guess)

All paths under `C:\Code\OpenTavu\core\`.

| File | Why |
|---|---|
| `src/_Shared/Common/PluginBase.cs` | Base contract. Business logic goes **only** in `ExecuteInternal(LocalPluginContext)`. Handles service extraction, error handling, `MaxDepth` (=1, anti-recursion). |
| `src/_Shared/Common/LocalPluginContext.cs` | Exposes `PluginExecutionContext`, `UserService`, `SystemService`, `TracingService`, `Trace(...)`. Trace via `localContext.Trace(...)`, never `TracingService.Trace` directly. |
| `src/_Shared/AI/AIConfigResolver.cs` | Resolves AI config by **Task Key** → returns `AIResolvedConfig` (gateway or direct). Never throws for config gaps; sets `Usable=false` + `Reason`. |
| `src/Plugins/Pl.Case.Categorize/Categorize.cs` | **The template to mirror.** Module 1: async Post-Op on Create, `AIConfigResolver.Resolve(taskKey)`, `IAIProvider` (GatewayProvider or AIProviderFactory), JSON parse with `DataContractJsonSerializer`, validate AI output against real records (hallucination guard), degrade-to-manual-review on any failure. |
| `src/Plugins/Pl.Proposal.BuildEmailDraft/BuildEmailDraft.cs` | **Custom API template to mirror** for `tavu_PromoteLead` (bound custom action reading a record, doing writes, returning an output param). |
| `src/WebResources/js/tavu_proposal_form.js` | Reference for a form JS web resource that calls a Custom API and refreshes the form. Mirror for the lead "Approve & Promote" button. |
| `docs/sales-model.md` §3.2–3.4 | `tavu_lead` schema, the flow, and the rationale. |
| `templates/README.md` | The `dotnet new opentavu-plugin` template docs. |

**Plugin conventions (non-negotiable):** inherit `OpenTavu.Dataverse.Common.PluginBase`;
logic in `ExecuteInternal`; centralize schema constants at the top; **`UserService`** for
operations that must respect the triggering user's privileges, **`SystemService`** only when
a security bypass is justified (reading config tables, writing derived/audit fields).
Module 1 uses `SystemService` because AI writes derived fields the user shouldn't need
privileges for, Module 3 should do the same (a low-privilege Power Automate connection user
creates the lead). Group by functional category (one assembly per lifecycle concern), not
one plugin per field.

**The scaffold already exists:** `src/Plugins/Pl.Lead.Triage/Triage.cs` (generated by
`dotnet new opentavu-plugin -n Pl.Lead.Triage --entityName tavu_lead --actionName Triage
--entityShortName Lead`). It's a stub, replace its body. **Do not** re-scaffold. If a
Custom API project is needed, scaffold a **separate** one:
`Pl.Lead.PromoteLead` via the same template (run from `src\Plugins\`).

---

## 3. `tavu_lead` schema (from sales-model.md §3.2)

Primary column `tavu_subject`. Custom columns:

| Schema | Type | Role |
|---|---|---|
| `tavu_source` | Choice | Web Form / Email / LinkedIn / Partner Referral / Other |
| `tavu_sourcedetails` | Multiline | raw text of the received message (AI input) |
| `tavu_email` | Email | sender (deterministic email match key) |
| `tavu_phone` | Phone | if present |
| `tavu_firstname` / `tavu_lastname` | Text | extracted |
| `tavu_companyname` | Text | raw, unvalidated (domain/company match key) |
| `tavu_matchedaccount` | Lookup → account | AI/deterministic fills on match |
| `tavu_matchedcontact` | Lookup → contact | AI/deterministic fills on match |
| `tavu_aiconfidencescore` | Decimal (0–1) | AI fills |
| `tavu_airecommendation` | Multiline | AI fills: promote / discard / review + reasoning |
| `tavu_promotedcontact` | Lookup → contact | filled when promoted (Step 3) |
| `tavu_daysinbuffer` | Whole Number | scheduled flow |
| `tavu_bufferalert` | Choice | scheduled flow: 1 Fresh / 2 Aging / 3 Stale |
| `tavu_lastaiprocessingdate` | DateTime | AI fills after each run |

**State / Status Reason (native `statecode` / `statuscode`):**

- **Active (0):** New, AI Processing, Awaiting Human Review, Manual Review Required
- **Inactive (1):** Promoted to Contact, Discarded as Noise, Duplicate, Not Qualified, Stale

> ⚠️ These status-reason **numeric values are auto-assigned by Dataverse** and are needed as
> constants in the plugin. **The human must read them from the environment** (maker portal →
> `tavu_lead` → Status Reason column → each option's value) and provide them (see §4).
> `statecode` inactive requires setting `statecode=1` + the matching `statuscode`.

---

> **STATUS (2026-07-30):** Prerequisites §4 are **DONE** and Step B (`Triage.cs`) is
> **written** against the real environment values. See §9 for the reconciled constants,
> two column names still to confirm, and what remains (Steps C–G). Real values now baked
> into the plugin: `TaskKeyLeadTriage = 576600003`; status reasons New..Stale =
> 576600001..576600009.

## 4. PREREQUISITES the human (Gustavo) must provide before coding

1. **Task Key value for "Lead Triage".** Add an item **"Lead Triage"** to the global choice
   that feeds `tavu_aitaskconfiguration.tavu_taskkey` (the same choice that has
   "Case Categorization" = **576600000**). Dataverse assigns a number (likely 576600001).
   → **Provide the exact integer.** It becomes `private const int TaskKeyLeadTriage = <n>;`
   (mirrors `TaskKeyCaseCategorization = 576600000` in `Categorize.cs`).
   *Coupling:* `AIConfigResolver.Resolve()` filters `tavu_aitaskconfiguration` by
   `tavu_taskkey == <n>`. If the constant ≠ the option value → "No active config found" →
   everything degrades to Manual Review.

2. **`tavu_aitaskconfiguration` record for Lead Triage.** Create one active record:
   Task Key = "Lead Triage", a Model (or rely on System Settings default / gateway),
   Temperature (~0.2), Max Output Tokens (~600), Confidence Threshold (e.g. 0.75),
   Token Budget, and the **System Prompt** (draft in §7).

3. **`tavu_lead` status-reason integer values** (New, AI Processing, Awaiting Human Review,
   Manual Review Required, Promoted to Contact, Discarded as Noise, Duplicate, Not Qualified,
   Stale). → Provide all nine.

4. **`tavu_source` and `tavu_bufferalert` choice values** if the plugin/flow needs to read/set them.

5. **Confirm System Settings** has AI enabled (`tavu_aienabled = Yes`) and either a gateway
   (`tavu_GatewayUrl` + `tavu_GatewayKey` env vars) or a default model, else triage degrades
   to Manual Review by design.

---

## 5. Build steps (ordered)

### Step A, Choice + config record (human, maker portal)
Do prerequisites §4.1–§4.2. Nothing to code until the Task Key integer exists.

### Step B, Write `Pl.Lead.Triage/Triage.cs` (mirror `Categorize.cs`)

- **Registration:** Message **Create**, Primary Entity **`tavu_lead`**, Stage **40
  (Post-operation)**, Mode **Asynchronous**, Deployment **Server**. (The AI call is an
  outbound HTTP request + token cost → must be async; must never block lead creation.)
  *(Ignore the scaffold's default "Update / Pre-Op / Sync" remark, override to the above.)*
- **Guards:** MessageName == Create; Target is Entity; `target.LogicalName == "tavu_lead"`.
  (Same shape as `Categorize.ExecuteInternal`.)
- **Service:** use `localContext.SystemService` (derived/audit writes; low-privilege creator).
- **Pipeline (Option A):**
  1. Read `tavu_email`, `tavu_companyname`, `tavu_firstname`, `tavu_lastname`,
     `tavu_sourcedetails`, `tavu_source` from the Create target.
  2. **Deterministic pass (no AI):**
     - Query `contact` by `emailaddress1 == tavu_email` (exact) → if hit: set
       `tavu_matchedcontact` + its parent account into `tavu_matchedaccount`, set
       `Inactive / Promoted to Contact`, set recommendation text, **return** (no AI spend).
     - Else query `account` by primary domain (e.g. `websiteurl`/a domain field) or by
       `contact.emailaddress1` domain == the lead email domain → if a corporate-domain hit:
       set `tavu_matchedaccount`, `Awaiting Human Review` **or** auto-link per your rule
       (domain match links the account but the *person* is still new → recommend
       "Promote, create new contact under matched account").
     - Junk (`no-reply@`, empty body, spam pattern) → `Inactive / Discarded as Noise`, return.
     - Open-lead duplicate (another Active lead, same email) → `Inactive / Duplicate`, return.
  3. **Resolve AI config:** `AIConfigResolver.Resolve(svc, TaskKeyLeadTriage)`; if
     `!cfg.Usable` → route to Manual Review (set `Manual Review Required` + reason), return.
  4. **Call AI:** `IAIProvider provider = cfg.UseGateway ? new GatewayProvider(cfg.GatewayUrl,
     cfg.GatewayKey) : AIProviderFactory.Create(cfg.ProviderValue);` then
     `provider.Complete(AIConfigResolver.ToRequest(cfg, userContent, jsonResponse:true))`.
     Build `userContent` with the lead fields + a **candidate list** of near-match
     contacts/accounts (fuzzy on name/company) so the AI can pick an exact Name to link
     (same hallucination-guard pattern as Module 1: only link if the returned name maps to a
     real record).
  5. **Parse JSON** with `DataContractJsonSerializer` + `CleanJson` (copy the helpers from
     `Categorize.cs`). Define a `LeadTriageOutput` DataContract (see §7).
  6. **Apply Option A routing** from the parsed output:
     - `matchExistingName` resolves to a real contact/account → set matched lookup(s),
       `Inactive / Promoted to Contact`.
     - recommendation = "promote-create-new" (real prospect, no match) → `Awaiting Human
       Review`, `tavu_airecommendation = "Promote, create new"` (never auto-create).
     - "discard"/below noise floor → `Inactive / Discarded as Noise`.
     - low confidence / ambiguous → `Awaiting Human Review`.
  7. Always set `tavu_aiconfidencescore`, `tavu_airecommendation` (include AI reasoning),
     `tavu_lastaiprocessingdate = DateTime.UtcNow`, and extracted `tavu_firstname/lastname/
     companyname` if the AI improved them.
- **Fail-safe:** any exception / parse failure / unusable config → Manual Review (mirror
  `RouteToManualReview`). Never throw out of the async plugin in a way that loses the lead.

### Step C, `tavu_PromoteLead` Custom API (mirror `BuildEmailDraft.cs`)
- Scaffold `Pl.Lead.PromoteLead` (own project). Bound to `tavu_lead` (or unbound with a
  `LeadId` input). Register the plugin type against the Custom API via **Custom API Manager**
  (XrmToolBox), same as `tavu_BuildProposalEmailDraft`.
- **Inputs:** `LeadId` (or bound target), optional `LinkToContactId` / `LinkToAccountId` for
  "Link to existing".
- **Logic (Approve & Promote):** read the lead; if a matched account exists use it, else
  create `account` from `tavu_companyname`; create `contact` from
  first/last/email/phone under that account; set `tavu_promotedcontact`; set lead
  `statecode=1 / Promoted to Contact`. **Link to existing:** skip creation, just set
  `tavu_promotedcontact` = provided contact + inactive.
- **Output:** `ContactId` (+ `AccountId`). Use `UserService` here (the human is acting; the
  write must respect their privileges, this is the deliberate 2nd-line human gate).

### Step D, Ribbon buttons on the `tavu_lead` main form (JS web resource)
- New web resource `tavu_lead_form.js` (mirror `tavu_proposal_form.js` structure).
- **Approve & Promote** and **Link to existing** and **Discard** buttons, **visible only
  when** `statuscode == Awaiting Human Review`.
- Approve & Promote → progress indicator → call `tavu_PromoteLead` Custom API →
  `Xrm.Navigation` confirmation → refresh; re-apply any lockdown after refresh
  (`data.refresh()` does not re-run OnLoad, re-apply explicitly, a known gotcha from the
  proposal form).

### Step E, Scheduled Power Automate flow (no code), Step 4 cleanup
- Daily recurrence. For all Active leads: set `tavu_daysinbuffer = now - createdon`;
  set `tavu_bufferalert` (≤7 Fresh / 8–14 Aging / ≥15 Stale). For Active +
  `Awaiting Human Review` + `tavu_daysinbuffer > 14` → set `Inactive / Stale`; optional owner
  notification. (Choice colors render as pills in the view natively, no code.)

### Step F, Build, sign, register
- Build the plugin project(s); they resolve `Microsoft.Xrm.Sdk` via `HintPath` into the
  restored `packages\Microsoft.CrmSdk.CoreAssemblies.<ver>\` (see repo `_Shared/README.md`
  for the shared `.snk`). Register the Triage step via **Plugin Registration Tool** with the
  Step C/Step B registration settings above.

### Step G, Verify
- New anonymous lead with an email that **exactly matches** an existing contact → auto-linked,
  Promoted to Contact, **no AI tokens spent** (check trace: deterministic hit).
- New lead, unknown real prospect → `Awaiting Human Review` + `"Promote, create new"`;
  **no** account/contact created until the human clicks Approve & Promote.
- Junk `no-reply@` → Discarded as Noise.
- AI disabled / no config → Manual Review Required (never lost).
- Approve & Promote creates contact (+account), sets `tavu_promotedcontact`, lead Inactive.
- Trace log via `localContext.Trace` shows the decision path + confidence + threshold.

---

## 6. Gotchas / guardrails

- **Never create a new master record without the human gate** (the whole point of Option A).
- **Deterministic before AI**, an exact email match must short-circuit *before* any AI call
  (cost + latency). Verify in the trace that matched leads spend zero tokens.
- **Hallucination guard:** only set a matched lookup if the AI-returned name maps to a real
  active record (mirror `SetLookupIfFound` in `Categorize.cs`).
- **Degrade, don't block:** async plugin; any failure → Manual Review Required, lead survives.
- **`UserService` vs `SystemService`:** Triage plugin = `SystemService` (derived writes by a
  low-privilege creator); `tavu_PromoteLead` = `UserService` (human's privileged write).
- **Confidence threshold** comes from the task config / System Settings default, do not
  hardcode; read from `cfg.ConfidenceThreshold`.
- **AI-first path (roadmap link):** today the human confirms new-record creation. The
  architectural path forward is auto-promote above a very high confidence with post-hoc
  audit, but Option A keeps the human gate for now on purpose. State this in the narrative.

---

## 7. Draft System Prompt + AI output contract (for the `tavu_aitaskconfiguration` record)

**System Prompt (paste into the config record; tune later):**

```
You are the lead-triage assistant for a professional-services CRM. You receive one
anonymous inbound lead and a list of candidate existing Contacts and Accounts that might
match it. Your job: (1) decide whether the lead matches an EXISTING record, (2) judge
whether it is a real prospect or noise, (3) extract clean person/company fields.

Rules:
- Only claim a match using the EXACT Name from the candidate list provided. If nothing
  matches, leave the match fields empty. Never invent a record.
- Creating a new record is NOT your decision. If the lead is a real prospect with no
  existing match, recommend "promote-create-new"; a human will confirm.
- Mark "discard" only for clear noise (no-reply, automated, spam, empty).
- Return confidence in [0,1] reflecting how sure you are of the recommendation.
- Do not use em dashes or long dashes (—) anywhere in your text. Use commas, periods,
  colons, or parentheses instead.

Return ONLY this JSON object, no prose:
{
  "matchContactName": "",        // exact candidate Name or empty
  "matchAccountName": "",        // exact candidate Name or empty
  "recommendation": "",          // one of: link-existing | promote-create-new | discard | review
  "isRealProspect": true,
  "firstName": "",
  "lastName": "",
  "companyName": "",
  "confidence": 0.0,
  "reasoning": ""
}
```

**`LeadTriageOutput` DataContract (in the plugin, mirror `CategorizationOutput`):**

```csharp
[DataContract]
private sealed class LeadTriageOutput
{
    [DataMember(Name = "matchContactName")] public string MatchContactName { get; set; }
    [DataMember(Name = "matchAccountName")] public string MatchAccountName { get; set; }
    [DataMember(Name = "recommendation")]   public string Recommendation { get; set; }
    [DataMember(Name = "isRealProspect")]   public bool   IsRealProspect { get; set; }
    [DataMember(Name = "firstName")]        public string FirstName { get; set; }
    [DataMember(Name = "lastName")]         public string LastName { get; set; }
    [DataMember(Name = "companyName")]      public string CompanyName { get; set; }
    [DataMember(Name = "confidence")]       public double Confidence { get; set; }
    [DataMember(Name = "reasoning")]        public string Reasoning { get; set; }
}
```

---

## 9. Build progress + environment reconciliation (2026-07-30)

### 9.1 Prerequisites, DONE (real values from `opentavu.crm.dynamics.com`)

**Task Key** global choice `tavu_aitaskkeyconfig`: Case Categorization=576600000,
Response Drafting=576600001, Activity Extraction=576600002, **Lead Triage=576600003**.
Baked into the plugin as `TaskKeyLeadTriage = 576600003`.

**`tavu_lead` Status Reasons (statuscode):**

| Status Reason | statecode | statuscode |
|---|---|---|
| New | Active (0) | 576600001 |
| AI Processing | Active (0) | 576600002 |
| Awaiting Human Review | Active (0) | 576600003 |
| Manual Review Required | Active (0) | 576600004 |
| Promoted to Contact | Inactive (1) | 576600005 |
| Discarded as Noise | Inactive (1) | 576600006 |
| Duplicate | Inactive (1) | 576600007 |
| Not Qualified | Inactive (1) | 576600008 |
| Stale | Inactive (1) | 576600009 |

**AI Task Configuration record:** "Lead Triage (Open AI)", Task Key = Lead Triage,
Model = GPT-4o mini (Azure), Temperature 0.20, Max Output Tokens 600, Confidence
Threshold 0.75, System Prompt from §7. Active. ✔

### 9.2 Schema deltas vs the original §3 (the plan was slightly stale)

- The lead source is a **`tavu_source`** choice column on `tavu_lead`, bound to the global
  choice **`tavu_leadsource`** (Website / LinkedIn / Referral / Cold Outreach / AI Scraper /
  AI Chatbot / Other). The Triage plugin does **not** branch on source, so the constant is
  centralized and unused for now. (Confirmed 2026-07-31 from the solution form definition.)
- `tavu_lead` also has **`tavu_mobilephone`** (in addition to `tavu_phone`) and a
  calculated **`tavu_fullname`**. Not used by Triage; relevant to `tavu_PromoteLead` (Step C).
- **`tavu_bufferalert`** real option values are **576600000 / 576600001 / 576600002**
  (Fresh / Aging / Stale), *not* 1 / 2 / 3 as the plan drafted. Only the Step E scheduled
  flow sets these, update the flow accordingly.

### 9.3 Two column logical names (RESOLVED)

Both were confirmed during the build, so these are no longer open items:

1. **`tavu_sourcedetails`** is the raw inbound message body (the AI's main input). Confirmed.
2. The lead's source column is **`tavu_source`** (bound to the global choice `tavu_leadsource`).
   Confirmed. It is currently unused by the plugin.

The initial read still uses `ColumnSet(true)`, so an absent column reads as empty rather than
throwing (fail-safe retained).

### 9.4 Step B, DONE

- `src/Plugins/Pl.Lead.Triage/Triage.cs`, full Option A pipeline written (deterministic:
  junk / exact-email contact match / open-lead duplicate / corporate-domain match, then AI
  fuzzy-match + prospect-vs-noise + extraction, then Option A routing, degrade-to-Manual-
  Review fail-safe). Mirrors `Categorize.cs` (helpers `CleanJson`, `SetStatus`, `StampAi`,
  `Finalize`, `ResolveContact/Account`; `LeadTriageOutput` DataContract).
- `Pl.Lead.Triage.csproj`, **fixed**: the scaffold linked only `_Shared/Common`; added the
  `_Shared/AI` links (IAIProvider, AzureOpenAIProvider, OpenAIProvider, AIConfigResolver,
  AIProviderFactory, GatewayProvider), `System.Xml` reference, and broadened the Exclude to
  `..\..\_Shared\**\*.cs`. Without this the project would not compile.
- **Not yet built/registered**, no .NET Framework toolchain in the Cowork sandbox;
  verification was a static review against the real reference signatures. **Build + register
  on Windows** with the settings in §5 Step B (Create / tavu_lead / Stage 40 Post-op / Async).

### 9.5 Step C, DONE

- `src/Plugins/Pl.Lead.PromoteLead/` created (csproj + packages.config mirror
  `Pl.Proposal.BuildEmailDraft`; `_Shared/Common` only, no AI needed).
- `PromoteLead.cs`, Custom API `tavu_PromoteLead`. Two modes: **Link to existing**
  (`LinkToContactId` supplied → no record created, lead linked + closed) and **Approve &
  Promote** (resolve account: `LinkToAccountId` > lead Matched Account > create new from
  company name; then create contact under it). Idempotent (returns existing Promoted
  Contact on retry). Uses `UserService` (the human's privileged 2nd-line write). Inputs
  `LeadId` [req], `LinkToContactId` [opt], `LinkToAccountId` [opt]; outputs `ContactId`,
  `AccountId`. **Register the Custom API** (`tavu_PromoteLead`) + its params via Custom API
  Manager (XrmToolBox), then set this assembly's type as the plugin. **Discard is NOT in
  this API**, it's a plain status flip handled by the Step D form JS.
- **Not yet built/registered** (Windows step).

### 9.6 Step D, DONE

- `src/WebResources/js/tavu_lead_form.js`, mirrors `tavu_proposal_form.js`. Namespace
  `OpenTavu.Lead.Form`. Commands: **approveAndPromote** (calls `tavu_PromoteLead` with
  LeadId → opens the new contact), **linkToExisting** (contact picker via
  `Xrm.Utility.lookupObjects` → `tavu_PromoteLead` with LinkToContactId), **discard**
  (plain status flip to Not Qualified, no master record). **onLoad** surfaces the AI
  recommendation + confidence as an info banner and locks closed leads. All handlers
  self-guard on `statuscode == Awaiting Human Review`. `node --check` passed.
- Custom API call uses the dynamic-metadata pattern (only sent params appear in
  `parameterTypes`, so LeadId-only and LeadId+contact calls both match).
- **Discard target = Not Qualified (576600008)** by choice (human "reviewed, pass"); one
  constant `STATUS_NOT_QUALIFIED` to change if you'd rather use Discarded as Noise.

### 9.7 Module 3 close-out (2026-08-04)

**Status: built, registered, and tested end to end.** Steps A-E implemented; Section 3 (flow)
tests deferred. The module is live in `opentavu.crm.dynamics.com`.

**Refinements applied during testing (beyond the original plan):**

- **Confidence stored as a whole percentage (0-100), labeled "AI Confidence (%)"** on
  `tavu_lead` (0.90 read as confusing). `StampAi` scales by 100; the routing threshold stays
  on the raw 0-1 value; the form banner shows "N%". Column precision 0, min 0, max 100.
- **No em dashes** in any product text: fixed the plugin-composed strings and added a
  "no em dashes" rule to the Lead Triage system prompt (§7). Applies to all OpenTavu AI text.
- **Provenance: `tavu_originatinglead`** lookup on `contact` and `account`, set by
  `PromoteLead` only when it CREATES the record (never on link-to-existing or matched-account
  reuse). Shown only when populated and always read-only, via `tavu_contact_form.js` /
  `tavu_account_form.js` (`applyOriginatingLeadVisibility`, no business rule). Links a
  promoted record back to the raw inbound signal and the AI reasoning (AI-decision audit).
- **Promoted Contact reliable across routes:** auto-link paths (deterministic exact email
  match and confident AI fuzzy match) now also set `tavu_promotedcontact` = the matched
  contact, so a "Promoted to Contact" lead always carries Promoted Contact (§1 updated).
- **Buttons cover both human-review states:** Approve & Promote / Link to Existing / Discard
  are enabled on **Awaiting Human Review** AND **Manual Review Required** (both need a human).
  The two states are kept distinct on purpose (AI ran vs AI failed = an ops/health signal).
- **Discard target = Not Qualified** (human "reviewed, pass"), distinct from AI "Discarded as Noise".
- **Stale/aging parameterized** in System Settings (`tavu_leadbufferagingdays` default 7,
  `tavu_leadbufferstaledays` default 14); the Step E flow reads them with `coalesce` fallback.

**Testing (see `module3-lead-triage-test-checklist.md`):**

- **Section 1 (Triage) PASSED** T1-T6: exact contact match (0 tokens), junk, duplicate,
  domain match, AI real-prospect, AI-unavailable degrade to Manual Review.
- **Section 2 (buttons) PASSED** F1-F6: banners + visibility, Approve (new account / matched
  account / no account), Link to Existing, Discard. F7/F8 guards covered by design.
- **Section 3 (flow) DEFERRED** E1-E5: needs active leads older than one day (createdon can't
  be backdated); run later with the low-threshold trick documented in the checklist.

**Sign-off invariants (verified in testing):**

- Deterministic matches (junk / exact email / duplicate / domain) spend **zero AI tokens**.
- A brand-new master record is **never created** without either the human clicking Approve
  or a confident AI match to an existing record (auto-link creates nothing new).
- Any failure or unusable AI config **degrades to Manual Review Required**; no lead is lost.
- Buffer alert and auto-stale honor the System Settings values.

**Remaining / follow-ups (not blockers):**

- Run Section 3 (flow) tests when a lead is more than one day old.
- Sweep old em dashes out of the pre-existing shared form scripts (contact/account/opportunity/
  proposal/product) and the gateway proposal-email prompt (tracked separately).
- Optional: tune the confidence threshold or gate name-only fuzzy matches to review instead of
  auto-link (a name-only gmail match auto-linked to a corporate contact during F5).
- **Confirmed:** `tavu_sourcedetails` is the correct body column; `tavu_source` is the lead's
  source choice column. §9.3 VERIFY notes resolved.

---

## 8. Quick prompt to open the new project with

> "Build OpenTavu Module 3 (AI Lead Triage) on Dataverse. Follow
> `core/docs/module3-lead-triage-build-plan.md` exactly. Start by reading the repo files it
> lists in §2 (PluginBase, LocalPluginContext, AIConfigResolver, Pl.Case.Categorize,
> Pl.Proposal.BuildEmailDraft). The scaffold `Pl.Lead.Triage/Triage.cs` already exists,
> replace its body. My Task Key value for Lead Triage is <N>; my tavu_lead status-reason
> values are <...>. Confirm the plan with me before writing code."
