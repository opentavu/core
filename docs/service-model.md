# Service Model Operational Guide — OpenTavu

## How the AI-first case management system with configurable SLA works

**Audience:** Power Platform consultants, MVPs, integrators adopting OpenTavu, future contributors, and any AI system that needs context about the model.

**Purpose:** explain what each service model table does, what each field does, and when each field is populated. The guide is organized by tables with complete specifications, complemented by operational flow narrative and real examples.

**Last updated:** July 4, 2026

---

## 1. Model overview

OpenTavu models the post-sales service operation of a Professional Services SMB firm with six interrelated tables, explicitly supporting three client models: B2B, B2C/individuals, and hybrid.

### Service model tables

| Table | Role | Who edits |
|---|---|---|
| `tavu_customertierdefinition` | Client tier catalog (Standard, Premium, Strategic) | Admin during setup; rarely afterward |
| `tavu_casetype` | Inquiry type catalog (RFP, Complaint, Support Request, etc.) | Admin during setup; adjusted to client's business |
| `tavu_sla` | Matrix defining the SLA for each Tier + Type combination | Admin during setup; adjusted when service policy changes |
| `account` (Dataverse standard) | Client accounts with assigned tier | Sales / Ops when onboarding a client |
| `tavu_case` | Incoming inquiries (cases) managed by the team | System (AI) and consultants in daily operation |
| `tavu_timeentry` (Activity) | Record of hours worked on cases/opportunities | Consultants in daily operation |

The core idea: **AI categorizes the new case, the system looks up the applicable SLA based on the client's tier and the case type, and the team resolves it within the defined timeframe.**

### Mental diagram of the flow

```
[Email arrives]
     ↓
[tavu_case created → Status: New]
     ↓
[AI processes: categorizes, prioritizes, summarizes]
     ↓
[System looks up SLA in tavu_sla matching: tier + type]
     ↓
[Applies SLA to case: response/resolution targets]
     ↓
[Assigned to queue or consultant]
     ↓
[Consultant works → Status: In Progress]
     ↓
[During work, consultant logs time entries → Actual Hours accumulates]
     ↓
[Consultant closes → statecode: Inactive (Resolved or Cancelled reason)]
```

---

## 2. Table `tavu_customertierdefinition` — Client tier levels

**Purpose:** define the client segments that exist in the firm. This master table feeds the tiers assignable to Account and Contact.

### Base configuration

| Property | Value |
|---|---|
| Display name | `Customer Tier` |
| Plural | `Customer Tiers` |
| Schema name | `tavu_customertierdefinition` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | Organization |
| Audit | ✅ |

### State + Status Reason

| State (statecode) | Status Reasons (statuscode) |
|---|---|
| **Active** (default) | Available |
| **Inactive** | Deprecated, Replaced |

### Custom columns

| Display Name | Schema Name | Type | Required |
|---|---|---|---|
| Name | tavu_name | Single Line of Text (Primary) | Required |
| Code | tavu_code | Autonumber (`CTD-{SEQNUM:4}`) | System (read-only) |
| Description | tavu_description | Multiple Lines of Text | Optional |
| Sort Order | tavu_sortorder | Whole Number | Optional |
| Display Color | tavu_displaycolor | Single Line of Text (hex) | Optional |

> **`Code` as a stable config key.** `tavu_code` (e.g. `CTD-1000` = Standard) is the tenant-stable identifier for a tier — useful for referencing a specific tier from configuration without a hardcoded GUID.

### Initial seed data

| Name | Sort Order | Description |
|---|---|---|
| Standard | 100 | Default tier for regular clients |
| Premium | 50 | Clients with improved SLA or extended contracts |
| Strategic | 10 | Top-tier — highest priority clients |

### When to modify

When the firm changes its client segmentation. For example, an agency might add a fourth "Trial" tier for clients in a trial period. To deprecate a tier, change its statecode to Inactive (do not delete) to preserve historical data.

---

## 3. Table `tavu_casetype` — Inquiry types

**Purpose:** classify the nature of incoming work for routing and SLA application.

### Base configuration

| Property | Value |
|---|---|
| Display name | `Case Type` |
| Plural | `Case Types` |
| Schema name | `tavu_casetype` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | Organization |
| Audit | ✅ |
| Quick create | ❌ |

### State + Status Reason

| State (statecode) | Status Reasons (statuscode) |
|---|---|
| **Active** (default) | Available |
| **Inactive** | Deprecated, Replaced |

### Custom columns

| Display Name | Schema Name | Type | Required |
|---|---|---|---|
| Name | tavu_name | Single Line of Text (Primary) | Required |
| Code | tavu_code | Single Line of Text (5-10 chars) | Optional |
| Description | tavu_description | Multiple Lines of Text | Optional |
| Default Priority | tavu_defaultpriority | Choice (Standard, Expedited, Critical) | Optional |
| Default Owner Team | tavu_defaultownerteam | Lookup → Team | Optional |
| Display Color | tavu_displaycolor | Single Line of Text (hex) | Optional |
| AI Categorization Hint | tavu_aihint | Multiple Lines of Text | Optional |
| Sort Order | tavu_sortorder | Whole Number | Optional |
| Is Default | tavu_isdefault | Yes/No (Two Options) | Optional |

### Initial seed data

| Name | Code | Default Priority | Is Default |
|---|---|---|---|
| General Inquiry | GEN | Standard | Yes |
| Support Request | SUP | Standard | No |
| RFP/Proposal Inquiry | RFP | Expedited | No |
| Billing Inquiry | BIL | Standard | No |
| Scope Change Request | SCO | Expedited | No |
| Complaint | CMP | Critical | No |
| Other | OTH | Standard | No |

### Operational notes

- **Code:** short code for internal use and reporting (3-5 characters).
- **AI Categorization Hint:** text included in the AI prompt to help it understand when to apply this type. Example for "Complaint": *"Apply when customer expresses dissatisfaction, frustration, or reports a service failure that affected their operations."*
- **Is Default:** flag indicating which type is used when AI cannot categorize with confidence. Only one should have Yes.

### When to modify

When the firm identifies a recurring inquiry type not currently covered. For example, a Software QA firm might add "Bug Report" and "Test Cycle Request" as specific types.

---

## 3.1 Tables `tavu_businessline`, `tavu_category`, `tavu_subcategory` — Case classification cascade

**Purpose:** the per-firm, AI-populated topical taxonomy that describes *what a case is about* (its subject domain). It is a different axis from `tavu_casetype`: Case Type is the **operational** axis (drives SLA matching + routing); this cascade is the **topical** axis (Business Line → Category → Subcategory). Module 1 (Smart Case Categorization) classifies each case into this cascade.

### Depth is optional; integrity is not

The cascade is a strict hierarchy: a Category belongs to a Business Line; a Subcategory belongs to a Category. A firm chooses depth by **how many levels it populates**, not by relaxing integrity:

- A firm using only Business Line + Category simply creates **no** Subcategory records; cases leave Subcategory empty.
- The parent lookups are **Required on the child** (`Category.Business Line`, `Subcategory.Category`) to prevent orphans, which would break the cascade, reporting, and Module 1's validation. Requiredness on a child never forces a deeper level to exist.

### Common base configuration (all three tables)

| Property | Value |
|---|---|
| Ownership | Organization |
| Audit | ✅ |
| Primary column | `Name` (`tavu_name`) |
| Quick create | ✅ (for adding children from the parent subgrid) |

### State + Status Reason (all three)

| State (statecode) | Status Reasons (statuscode) |
|---|---|
| **Active** (default) | Available |
| **Inactive** | Deprecated, Replaced |

### Columns

| Display Name | Schema Name | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line of Text (Primary) | Required | |
| Code | tavu_code | Autonumber | System (read-only) | Reference code |
| Sort Order | tavu_sortorder | Whole Number | Optional | Orders siblings within a parent |
| Description | tavu_description | Multiple Lines of Text | Optional | |
| AI Categorization Hint | tavu_aihint | Multiple Lines of Text (**Plain text**) | Optional | Injected into the Module 1 prompt |
| Business Line | tavu_businessline | Lookup → tavu_businessline | **Required** | **`tavu_category` only** (parent) |
| Category | tavu_category | Lookup → tavu_category | **Required** | **`tavu_subcategory` only** (parent) |

### AI Categorization Hint

Plain-text instructions injected into the Module 1 prompt to disambiguate nodes whose names alone are insufficient (e.g., "System Outage" vs "Performance"). Written by the implementer at setup; not shown to end users (kept off the default form — edit via Data grid or Excel import). Optional per node — fill only where the name is ambiguous.

### Relationship to the case

`tavu_case` carries `tavu_businessline`, `tavu_category`, `tavu_subcategory` as independent optional lookups (the form cascade-filters child by selected parent). Module 1 validates that the proposed chain exists and is active before persisting.

---

## 3.2 AI configuration layer (`tavu_aimodel`, `tavu_aitaskconfig`, `tavu_systemsettings`)

**Purpose:** the provider-agnostic, configuration-over-code layer that lets each tenant choose **which AI model runs each task** without code changes. Module 1 (and future AI modules) resolve their model + parameters + prompt from these tables at runtime through the `IAIProvider` abstraction. *(Schema names below are indicative — match what exists in the environment.)*

### `tavu_aimodel` — model catalog (one row per usable model)

| Display Name | Schema Name | Type | Notes |
|---|---|---|---|
| Name | tavu_name | Single Line of Text (Primary) | e.g. "GPT-4o mini (Azure)" |
| Provider | tavu_provider | Choice | Azure OpenAI / OpenAI / Anthropic / Google Gemini — selects the `IAIProvider` implementation |
| Deployment / Model ID | tavu_deploymentname | Single Line of Text | Azure deployment name or model id, e.g. `gpt-4o-mini` |
| Endpoint | tavu_endpoint | Single Line of Text | Provider base URL |
| API Version | tavu_apiversion | Single Line of Text | e.g. `2024-10-21` (Azure) |
| Secret Name | tavu_secretname | Single Line of Text | **Name** of the env-variable / Key Vault secret holding the API key — never the key itself |
| Cost Tier | tavu_costtier | Choice | Economy / Standard / Premium |
| Is Default | tavu_isdefault | Yes/No | Default model when a task doesn't specify |

*Deferred (add when metering cost for billing): Input/Output cost per 1K tokens, Max context tokens.*

### `tavu_aitaskconfig` — task → model mapping

| Display Name | Schema Name | Type | Notes |
|---|---|---|---|
| Name | tavu_name | Single Line of Text (Primary) | e.g. "Case Categorization" |
| Task Key | tavu_taskkey | Choice | Stable key the code looks up (Case Categorization / Response Drafting / Activity Extraction…) |
| Model | tavu_model | Lookup → tavu_aimodel | Which model this task uses |
| Temperature | tavu_temperature | Decimal | 0.0–0.2 for categorization (determinism) |
| Max Output Tokens | tavu_maxoutputtokens | Whole Number | Output cap |
| Confidence Threshold | tavu_confidencethreshold | Decimal (0–1) | Per-task override (blank → inherits the global) |
| Token Budget | tavu_tokenbudget | Whole Number | Optional, per window |
| System Prompt | tavu_systemprompt | Multiple Lines (**Plain text**) | Prompt template — tunable without redeploy |

### `tavu_systemsettings` — AI globals (fields added to the singleton)

| Display Name | Schema Name | Type | Notes |
|---|---|---|---|
| AI Enabled | tavu_aienabled | Yes/No | **Master kill switch.** If No → cases skip AI and go to Manual Review (graceful degradation) |
| Default AI Model | tavu_defaultaimodel | Lookup → tavu_aimodel | Fallback when a task has no model |
| Default Confidence Threshold | tavu_aiconfidencethreshold | Decimal | Global default (0.85) |
| Default Customer Tier | tavu_defaultcustomertier | Lookup → tavu_customertierdefinition | **SLA fallback.** When a case's customer has no tier, `Pl.Case.SlaAssignment` uses this tier so an SLA is still applied (§4 matching step c). Set it to Standard. |

### How a module resolves its AI config at runtime

1. Read `tavu_aitaskconfig` for the task key (e.g., Case Categorization).
2. If active → use its Model + Temperature + Max Output Tokens + System Prompt + Confidence Threshold; if Confidence Threshold is blank, inherit `tavu_systemsettings.Default Confidence Threshold`.
3. If no task config → fall back to `Default AI Model`.
4. If `AI Enabled = No` → skip AI entirely and route the case to Manual Review.
5. Resolve the provider connection from `tavu_aimodel` (Provider, Endpoint, Deployment, API Version, Secret Name). The **secret value** is read from the environment variable / Key Vault named in Secret Name — never stored in Dataverse.

### Security & forward-compatibility

- Secrets never live in a Dataverse column; only the secret's **name** does.
- Under the commercial managed-service model, `Endpoint` / `Secret Name` later point to the **OpenTavu AI gateway** (a service holding per-tenant keys, doing model routing, budget, and usage metering) instead of Azure directly. The `IAIProvider` abstraction absorbs that change without touching the modules.

---

## 4. Table `tavu_sla` — The SLA matrix

**Purpose:** define the specific SLA for each combination of Customer Tier and Case Type. This is the most important table operationally because it defines the firm's implicit service contract.

### Base configuration

| Property | Value |
|---|---|
| Display name | `SLA Definition` |
| Plural | `SLA Definitions` |
| Schema name | `tavu_sla` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | Organization |
| Audit | ✅ |

### State + Status Reason

| State (statecode) | Status Reasons (statuscode) |
|---|---|
| **Active** (default) | Available |
| **Inactive** | Deprecated, Replaced |

### Custom columns

| Display Name | Schema Name | Type | Required |
|---|---|---|---|
| Name | tavu_name | Single Line of Text (Primary) | Required |
| Customer Tier | tavu_customertier | Lookup → tavu_customertierdefinition | Required |
| Case Type | tavu_casetype | Lookup → tavu_casetype | Optional (null = applies to all types for this tier) |
| Response Target Hours | tavu_responsetargethours | Decimal (2 dp) | Required |
| Resolution Target Hours | tavu_resolutiontargethours | Decimal (2 dp) | Required |
| Calendar | tavu_calendar | Lookup → tavu_businesscalendar | Optional (null → use the Default Calendar) |
| Coverage Hours | tavu_coveragehours | Choice (24x7, Business Hours 8x5, Extended Hours 12x5) | **Deprecated** — superseded by the Calendar lookup |
| Evaluation Priority | tavu_evaluationpriority | Whole Number | Required |
| Description | tavu_description | Multiple Lines of Text | Optional |

**On `Calendar` vs `Coverage Hours`:** the working schedule now comes from a **Business Calendar** (Section 4.1), referenced per SLA — the same pattern Dynamics 365 Customer Service uses (`SLA.BusinessHours`). `Coverage Hours` (the old Choice) is kept only for backward compatibility and is no longer read by the SLA engine.

### Initial seed data

| Name | Tier | Type | Response (hrs) | Resolution (hrs) | Coverage | Eval Priority |
|---|---|---|---|---|---|---|
| Standard - Default | Standard | (empty) | 8 | 48 | Business Hours 8x5 | 100 |
| Standard - Complaint | Standard | Complaint | 4 | 24 | Business Hours 8x5 | 50 |
| Premium - Default | Premium | (empty) | 4 | 24 | Extended Hours 12x5 | 100 |
| Premium - Complaint | Premium | Complaint | 1 | 8 | Extended Hours 12x5 | 50 |
| Strategic - Default | Strategic | (empty) | 1 | 8 | 24x7 | 100 |
| Strategic - Complaint | Strategic | Complaint | 0.5 | 4 | 24x7 | 50 |
| Strategic - Support Request | Strategic | Support Request | 0.5 | 4 | 24x7 | 50 |

### Matching logic

When a new case is created, the system finds the applicable SLA as follows:

```
1. Get customer from case → identify if it's Account or Contact
   - If tavu_account is not empty → use Customer Tier from Account
   - If tavu_contact is not empty → use Customer Tier from Contact (B2C case)
2. Get type of case (e.g.: Support Request)
3. Search in tavu_sla:
   a. Exact match: Tier=Strategic AND Type=Support Request
      → Finds "Strategic - Support Request"
   b. If no exact match:
      Generic match: Tier=Strategic AND Type=(empty)
      → Finds "Strategic - Default"
   c. If nothing found: use system default (configurable in tavu_systemsettings)
4. Calculate target dates adjusted by Coverage Hours
5. Fill SLA fields in the case
```

**Key:** the algorithm looks up the Customer Tier from Account OR Contact depending on where `tavu_customer` points. This naturally supports the B2C case (individual client with their own tier) without additional code.

### The architectural trick

Leaving `Case Type` empty converts the record into the tier's "default." This allows each firm to only create the specific records it needs. If a firm does not differentiate SLAs by type, it only configures 3 records (Standard-Default, Premium-Default, Strategic-Default).

The **Evaluation Priority** column defines the evaluation order when there are multiple potential matches. Lower = evaluated first. By convention: specific records (Tier+Type) use 50; defaults (Tier only) use 100.

---

## 4.1 Business calendars (`tavu_businesscalendar`, `tavu_calendarworkinghours`, `tavu_businessclosure`)

**Purpose:** define working schedules so SLA target dates respect business hours and holidays. Each SLA references a calendar (Section 4). This mirrors Dynamics 365 Customer Service's *Customer Service Schedule* + *Holiday Schedule* (`SLA.BusinessHours`), but implemented custom so it runs on Power Apps Premium without a Customer Service license. Multiple reusable calendars are supported; per-business-line/team assignment is deferred (the SLA→calendar lookup already gives differentiation, e.g. Strategic → 24x7, Standard → 8x5).

**Common base (all three tables):** Ownership = Organization, Audit ✅, `Code` = Autonumber (read-only), primary column = `Name`, states Active/Inactive.

### `tavu_businesscalendar` — schedule header

| Display Name | Schema Name | Type | Notes |
|---|---|---|---|
| Name | tavu_name | Single Line of Text (Primary) | e.g. "Standard 8x5 (Colombia)" |
| Code | tavu_code | Autonumber `CAL-{SEQNUM:000}` | read-only |
| Time Zone | tavu_timezone | **Whole Number, Format = Time Zone** | standard TZ picker; stores the TimeZoneCode |
| Is 24x7 | tavu_is24x7 | Yes/No | if Yes, clock runs continuously (no working-hours rows needed) |
| Is Default | tavu_isdefault | Yes/No | fallback when an SLA has no Calendar |
| Description | tavu_description | Multiple Lines of Text | |

### `tavu_calendarworkinghours` — working intervals (child of calendar)

| Display Name | Schema Name | Type | Notes |
|---|---|---|---|
| Name | tavu_name | Single Line of Text (Primary) | label, e.g. "Monday Morning" |
| Code | tavu_code | Autonumber `CWH-{SEQNUM:000}` | |
| Calendar | tavu_calendar | Lookup → tavu_businesscalendar | **Required** (parent) |
| Day of Week | tavu_dayofweek | Choice (Monday=1 … Sunday=7) | |
| Start Time | tavu_starttime | Choice "Time of Day" | option **value = minutes from midnight** (540 = 09:00) |
| End Time | tavu_endtime | Choice "Time of Day" | option value = minutes (1080 = 18:00) |

**Multiple rows per day are allowed** → split shifts / lunch break (e.g. Monday 08:00–12:00 and Monday 13:00–17:00; the 12:00–13:00 gap is excluded from SLA time). A day with no row = closed.

### `tavu_businessclosure` — holidays / closures

| Display Name | Schema Name | Type | Notes |
|---|---|---|---|
| Name | tavu_name | Single Line of Text (Primary) | e.g. "Año Nuevo" |
| Code | tavu_code | Autonumber `CLO-{SEQNUM:000}` | |
| Date | tavu_date | Date Only | |
| Calendar | tavu_calendar | Lookup → tavu_businesscalendar | optional; null = applies to all calendars |

### "Time of Day" global choice

A reusable global Choice where **labels are HH:MM** and **values are minutes from midnight** (00:00=0, 09:00=540, 18:00=1080 … 24:00=1440). Gives an intuitive dropdown while the plugin reads the option value directly as minutes.

### How the SLA engine consumes the calendar

1. Resolve the SLA (Tier + Type) → its `Calendar` (or the Default Calendar).
2. Convert `createdon` (UTC) to the calendar's local time via `LocalTimeFromUtcTimeRequest` (using the TimeZoneCode).
3. Walk forward from that moment, consuming the SLA's target hours **only inside working intervals**, skipping nights/weekends/gaps and `tavu_businessclosure` dates. `Is 24x7 = Yes` → continuous clock (only closures pause it).
4. Convert the resulting local target datetime back to UTC (`UtcTimeFromLocalTimeRequest`) and store it as Response/Resolution Target Date. Anchoring to `createdon` means re-categorization recomputes fairly from the customer's arrival time (already-elapsed time counts). DST is handled by those SDK messages.

**Seed:** specific calendars and holidays are per-vertical/per-client configuration, not shipped as canonical seed (same rationale as the classification cascade).

---

## 5. Custom columns added to the standard `account` table

**Purpose:** enrich the standard Dataverse `account` table to support service operations.

### Custom columns added

| Display Name | Schema Name | Type | Required | Default |
|---|---|---|---|---|
| Customer Tier | tavu_customertier | Lookup → tavu_customertierdefinition | Optional | Standard |
| Is Customer | tavu_iscustomer | Yes/No (Two Options) | Optional | No |
| Customer Since | tavu_customersince | Date Only | Optional | (empty) |
| Last Engagement Date | tavu_lastengagementdate | DateTime | Optional | (empty) |

### Automatic logic

**`tavu_iscustomer`:**

```
IF tavu_opportunity changes to state = Won
  AND opp_customer points to Account:
    → account.tavu_iscustomer = Yes (if it was No)
    → account.tavu_customersince = today (if null, NOT overwritten)

IF opp_customer points to Contact:
    → logic applies to Contact, not Account (see Sales Model guide, Section 5)
```

`tavu_iscustomer` is NEVER automatically changed to No. That is a human decision when the relationship formally ends.

**`tavu_lastengagementdate`:** updated by Module 3 (Activity Capture) when engagement is detected (emails sent/received, meetings, calls).

### Why these columns matter in a service context

- Filter cases to only active clients (`tavu_iscustomer = Yes`) vs. prospects
- "Client tenure" metrics for retention reports
- Detect historical clients with no recent engagement (re-engagement candidates post-support)

> **Note:** the same columns exist on `contact` (with additional ones like `tavu_engagementstatus`) to support B2C cases where the client is a natural person with no associated Account. Full detail in the Sales Model guide, Section 5.

---

## 6. Table `tavu_case` — The operational heart

**Purpose:** represent each request, problem, or inquiry from a client that the team must address.

### Base configuration

| Property | Value |
|---|---|
| Display name | `Case` |
| Plural | `Cases` |
| Schema name | `tavu_case` |
| Primary column | `Title` (`tavu_title`) |
| Ownership | User or team |
| Activities | ✅ |
| Notes | ✅ |
| Connections | ✅ |
| Audit | ✅ |
| Quick create | ✅ |
| Enable for queues | ✅ |

### State + Status Reason

> **Important:** `tavu_case` is a custom table, so `statecode` has only **Active / Inactive** (Dataverse does not allow custom states on a table). The "resolved" vs "cancelled" outcomes are distinguished by **status-reason groups within the Inactive state**, not by separate states. Throughout this guide, "Resolved" and "Cancelled" refer to those status-reason groups, not to statecodes.

| State (statecode) | Status Reasons (statuscode) |
|---|---|
| **Active** (default) | New, AI Processing, Categorized — Awaiting Assignment, In Progress, Manual Review Required, Waiting on Customer |
| **Inactive** | **Resolved group:** Solved, Information Provided, Duplicate, Out of Scope · **Cancelled group:** Cancelled by Customer, Cannot Reproduce, Closed without Resolution |

### Custom columns — Client identification (hybrid architecture)

| Display Name | Schema Name | Type | Required | Notes |
|---|---|---|---|---|
| Customer | tavu_customer | Customer (polymorphic Account+Contact) | Required | Single source of truth |
| Account (auto) | tavu_account | Lookup → Account | Optional | Auto-populated when Customer=Account |
| Contact (auto) | tavu_contact | Lookup → Contact | Optional | Auto-populated when Customer=Contact |
| Primary Contact | tavu_primarycontact | Lookup → Contact | Optional | The person who communicated (human interlocutor) |

**Plugin/Flow logic when creating or modifying `tavu_customer`:**

```
IF tavu_customer points to Account (B2B case):
  → tavu_account = that Account
  → tavu_contact = (empty)
  → tavu_primarycontact NOT auto-populated
     (consultant fills manually, or Module 1 AI extracts from email)

IF tavu_customer points to Contact (B2C case):
  → tavu_account = (empty)
  → tavu_contact = that Contact
  → tavu_primarycontact = that Contact (auto, EDITABLE)
```

**Why this hybrid architecture:**
- **Simple UX:** user only interacts with `tavu_customer` (single field). Via `tavu_systemsettings.tavu_customermode` the firm configures whether the lookup shows only Accounts (B2B), only Contacts (B2C), or both (Mixed).
- **Simple reporting:** Power BI connects directly to `tavu_account` and `tavu_contact` (typed auto-populated fields), without handling polymorphism.
- **Microsoft standard pattern:** Dynamics 365 Quote, Order, and Invoice use exactly this pattern.

### Custom columns — Case data

| Display Name | Schema Name | Type | Required | Notes |
|---|---|---|---|---|
| Title | tavu_title | Single Line of Text (Primary) | Required | |
| Case Number | tavu_casenumber | Autonumber | System (read-only) | Short human-readable id, format `OTC-{SEQNUM:5}-{RANDSTRING:4}` → `OTC-01000-A3F9` (SEQNUM:n = digit count, not a zero mask). Used as the email **threading token** (§6.1). The RANDSTRING suffix makes it non-guessable. |
| Description | tavu_description | Multiple Lines of Text | Optional | Case content |
| Origin | tavu_origin | Choice | Optional | Email, Web Form, Phone, Manual, Internal |
| Type | tavu_type | Lookup → tavu_casetype | Required | Default: General Inquiry |
| Priority | tavu_priority | Choice | Optional | Standard, Expedited, Critical |
| Priority Reason | tavu_priorityreason | Multiple Lines of Text | Optional | Justification when Expedited/Critical |
| Business Line | tavu_businessline | Lookup → tavu_businessline | Optional | Cascading |
| Category | tavu_category | Lookup → tavu_category | Optional | Cascading from Business Line |
| Subcategory | tavu_subcategory | Lookup → tavu_subcategory | Optional | Cascading from Category |
| Related Opportunity | tavu_relatedopportunity | Lookup → tavu_opportunity | Optional | Connects service to sale |

### Custom columns — Time tracking and billing

| Display Name | Schema Name | Type | Required | Notes |
|---|---|---|---|---|
| Is Billable | tavu_isbillable | Yes/No (Two Options) | Required | Default: No |
| Estimated Hours | tavu_estimatedhours | Decimal | Optional | Initial budget estimate |
| Actual Hours | tavu_actualhours | Decimal | Optional | Auto-summed from tavu_timeentry |

### Custom columns — SLA tracking

| Display Name | Schema Name | Type | Required | Notes |
|---|---|---|---|---|
| Applied SLA | tavu_sla | Lookup → tavu_sla | Optional | Auto-filled by system (the `Pl.Case.SlaAssignment` plugin) |
| Response Target Date | tavu_responsetargetdate | DateTime | Optional | Calculated by system |
| Resolution Target Date | tavu_resolutiontargetdate | DateTime | Optional | Calculated by system |
| First Response Date | tavu_firstresponsedate | DateTime | Optional | Auto on first email replied |
| Resolution Date | tavu_resolutiondate | DateTime | Optional | Auto on state change to Resolved |
| SLA Status | tavu_slastatus | Choice | Optional | On Track, At Risk, Breached, Met |

### Custom columns — AI processing

| Display Name | Schema Name | Type | Required | Notes |
|---|---|---|---|---|
| AI Confidence Score | tavu_aiconfidencescore | Decimal (0-1) | Optional | Default threshold: 0.85 |
| AI Reasoning | tavu_aireasoning | Multiple Lines of Text | Optional | Chain of thought (audit trail) |
| AI Problem | tavu_aiproblem | Multiple Lines of Text (1000 chars) | Optional | Distilled core problem |
| AI Business Impact | tavu_aibusinessimpact | Multiple Lines of Text (500 chars) | Optional | Translated risk/impact |
| AI Missing Info | tavu_aimissinginfo | Multiple Lines of Text (1000 chars) | Optional | List of missing information |
| AI Sentiment | tavu_aisentiment | Choice | Optional | Calm, Concerned, Frustrated, Critical, Unknown |
| AI Summary | tavu_aisummary | Multiple Lines of Text (500 chars) | Optional | Executive one-liner |
| Is Automated | tavu_isautomated | Yes/No (Two Options) | Optional | Whether processed by AI |
| Multi-Intent Detected | tavu_multiintentdetected | Yes/No (Two Options) | Optional | AI flags when multiple intents detected |

### Custom columns — Resolution

| Display Name | Schema Name | Type | Required | Notes |
|---|---|---|---|---|
| Resolution Notes | tavu_resolutionnotes | Multiple Lines of Text | Optional | Closure documentation |

---

## 6.1 Case conversation model (`tavu_caseinteraction` + native `annotation` + native `appointment`)

**Purpose:** give a case a single, self-contained conversation thread — customer emails, agent replies, internal notes, and system events (status changes, scheduled sessions) — rendered in one pane, with per-interaction state traceability. This replaces the native Dynamics timeline as the primary case-work surface.

### Why not the native timeline

The native timeline mixes activity types in a generic feed and scatters attachments into Notes; it answers "what activities exist" but not "what is the conversation and how did the case state move with it." OpenTavu's thread is purpose-built for the Professional Services support loop: bubbles (in/out/note), interleaved system events, and a **status delta on the very interaction that caused it** (Aranda-style traceability). The timeline stays available on a secondary tab as a power-user/fallback view, but is not where agents work.

> **Design-mindset note.** This passes the Quick Test: it targets the CRM-hygiene / context-aware-communication pain points, it is *simpler* than the OOTB timeline (one axis: the conversation), and it is the render surface for **Module 2 (Context-Aware Customer Communication)** — the AI drafts the reply and proposes the status delta; the human is the second-line reviewer, not the composer. It is deliberately **not** a manual field with an "Ask AI" button.

### Table `tavu_caseinteraction`

| Property | Value |
|---|---|
| Display name | `Case Interaction` |
| Plural | `Case Interactions` |
| Schema name | `tavu_caseinteraction` |
| Primary column | `Name` (`tavu_name`) — auto-filled from the first 80 chars of the body |
| Ownership | **User or team** (mirrors case ownership; never Organization — an interaction is authored by a person) |
| Notes (attachments) | ✅ **required** (this is where email/agent attachments live — see below) |
| Audit | ✅ |
| Quick create | ❌ (created by the compose control or by flows, not by a form) |

**State + Status Reason:** Active (default) / Inactive. An interaction is an immutable historical record — it is not "resolved."

#### Columns

| Display Name | Schema Name | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line of Text (Primary) | Required | First ~80 chars of the body; for display/search only |
| Case | tavu_case | Lookup → tavu_case | **Required** | Parent case (the thread) |
| Body | tavu_body | Multiple Lines of Text (**Plain text**) | Optional | Message content. Plain by design — attachments live as annotations, not inline HTML (Module 2 vision reads the files, not a rich-text blob) |
| Direction | tavu_direction | Choice | Required | Inbound / Outbound / Internal Note (values below) |
| Channel | tavu_channel | Choice | Required | Email / Phone / Portal / Chat / System (values below) |
| From Contact | tavu_fromcontact | Lookup → Contact | Optional | Sender on **Inbound**; the customer interlocutor |
| Status Before | tavu_statusbefore | (as live schema) | Optional | Case status label immediately before this interaction, when it changed the case |
| Status After | tavu_statusafter | (as live schema) | Optional | Case status label immediately after |
| Changed Fields | tavu_changedfields | Multiple Lines of Text | Optional | Human-readable summary of other field changes, e.g. `Priority: Standard → Critical` |

#### Choice values (fixed across tenants)

**`tavu_direction`** — Message Interaction Direction:

| Label | Value |
|---|---|
| Inbound | 576600000 |
| Outbound | 576600001 |
| Internal Note | 576600002 |

**`tavu_channel`** — Channel Message:

| Label | Value |
|---|---|
| Email | 576600000 |
| System | 576600004 |

*(Additional channels — Phone, Portal, Chat — are firm-configurable additions; labels vary, the two values above are canonical for the email + system-event paths the flows use today.)*

### Presentation — the `CaseConversation` PCF control

A virtual React/Fluent (v9) control (`OpenTavu.Controls.CaseConversation`) bound to a subgrid of `tavu_caseinteraction` filtered by `tavu_case`. Behavior:

- **Compose at the top**, thread **newest-first (descending)**. The subgrid **view must be sorted Created On descending** — paging follows the view, so page 1 = the newest interactions; the control also sorts descending as a safety net.
- **Pagination:** primes a page of **10** and grows in increments of 10 via a subtle "Cargar más antiguos" button (avoids loading the full history blindly).
- **Bubbles:** Outbound (right, brand tint), Inbound (left, neutral), Internal Note (left, amber, "internal note" tag). System-only interactions (no body) render as a centered pill line.
- **State delta:** when `Status Before → Status After` or `Changed Fields` are stamped, they render as a caption under the bubble that caused the change.
- **Compose:** a `Textarea` + a public/internal `Switch` + Enviar. On send it `createRecord`s a `tavu_caseinteraction` (Outbound/Email for a public reply, Internal Note/System for a note) linked to the parent case, then refreshes. **The compose only records the interaction — it does not send email; a flow does (below).**

### Attachments — native `annotation`, rendered inline (no trip to Notes)

Attachments are stored as **native `annotation` (Note) records on the `tavu_caseinteraction`** (Notes enabled on the table). The storage location does not force the timeline UI: the PCF reads the annotations by `objectid` via WebAPI and renders them as chips/links **inside the interaction bubble**, and the compose's clip button uploads new files as annotations on the interaction being created. The agent never leaves the control.

**Storage cost (verified):** annotation `documentbody` in modern Dataverse environments lands in the **File** capacity tier (~$2/GB/mo), **not** the expensive **Database** tier (~$40/GB/mo). Reusing annotations therefore has the same cost as a custom File column, so the decision is UX, not cost — and reusing annotations is the simpler MVP (zero new tables).

**When a custom `tavu_interactionattachment` table earns its place (deferred):** only when an AI module must persist **per-attachment metadata** (extracted text, vision summary, confidence). `annotation` is a system table — adding custom columns affects every note tenant-wide, which is not acceptable. Until then, keep annotations as the pure file store and hold AI output elsewhere. This is a roadmap item, not MVP.

### Scheduled sessions — native `appointment`, mirrored into the thread

Booking a working session with the customer is a calendar problem, not a CRM-record problem: it needs Exchange/Outlook sync, attendees, reminders, invitations. OpenTavu **does not rebuild that** — it uses the native `appointment` activity (`regardingobjectid` = the case).

- **Create** from a compose action ("Agendar sesión") that `createRecord`s an `appointment` regarding the case; Exchange sync sends the invite.
- **Show** in the thread: a flow stamps a system-line `tavu_caseinteraction` ("📅 Sesión agendada con {contact} — {when}") so the appointment appears in the single pane without duplicating the calendar.
- **AI-first path:** Module 2 detects scheduling intent in the thread and proposes the slot.

### Email intake and outbound — provider-agnostic by design

Intake must **not** assume the client uses Microsoft email. A firm runs the CRM on Power Platform, but its mailbox may be Google Workspace, Zoho, Hostinger, etc. So the canonical paths use no Microsoft mailbox at all; an Office 365 flow is only a convenience when the client already lives in Microsoft.

**Inbound (email → case/interaction). Three mechanisms, one per client — all end at `/api/intake`:**

1. **Gateway IMAP-pull (canonical, provider-agnostic).** A timer function in the gateway connects by **IMAP** to the client's support mailbox (any provider), fetches unseen messages + attachments, and creates the `tavu_case` / `tavu_caseinteraction` directly via **S2S** (an Entra Application User in the client environment). **No Power Automate flow is involved.** Latency = the poll interval (1–2 min), which is fine for SMB support.
2. **Inbound-parse webhook (optional, push / near-real-time).** The support address's MX points to an inbound-parse service (SendGrid / Mailgun / Cloudflare Email Workers) that POSTs the parsed mail + attachments to the gateway. No flow either; adds an MX/SaaS dependency, so reserved for clients wanting real-time.
3. **Office 365 flow (Microsoft-only convenience).** `When a new email arrives (V3)` on an Exchange Online shared mailbox → calls `/api/intake`. Only applicable when the client is already on Microsoft 365.

In all three, the **gateway is the brain**: given no threading token, it decides new-case-vs-append and returns proposed Type / Business Line / Category / Priority (confidence-gated); given a token `[<case number>]` in the subject, it resolves the case by `tavu_casenumber` and appends an Inbound/Email interaction to it. Incoming attachments become annotations on the interaction. An auto-acknowledgement carrying the token is sent so the customer's replies thread.

> The dedicated support address must be a **separate mailbox**, never an alias onto a person's working inbox — otherwise IMAP-pull would ingest personal mail and create spurious cases.

**Outbound (agent reply → email). A flow, but not necessarily Office 365.** Triggered **on create of `tavu_caseinteraction` where `Direction = Outbound` AND `Channel = Email`**, it composes the mail (token in subject, body = `tavu_body`, attaching the interaction's annotations) and sends it through the **client's own SMTP** via Power Automate's generic **SMTP connector** (Hostinger, Zoho, Gmail SMTP…). The Office 365 Outlook connector is used only if the client is on Microsoft.

> **Why send lives in the client-tenant flow, not the gateway (no-lock-in).** Sending from the gateway would couple outbound comms to OpenTavu's private infra (and, on Microsoft, need `Mail.Send` over the client's mailbox). Keeping send in the client-tenant flow (SMTP or O365) means the mail path survives off-boarding untouched. The gateway is invoked for **receiving + decisioning** (it is the brain and lives outside the tenant), never for sending.

**Threading:** a subject token `[<case number>]` (the `tavu_casenumber` autonumber, e.g. `[OT-00042-A3F9]`) is used rather than parsing `In-Reply-To`/`References` headers — more robust across providers, short enough for a subject line, and non-guessable thanks to the random suffix.

---

## 7. `tavu_case` operational flow by moment

This section complements the spec with narrative: when each field is populated during the life of a case.

### 7.1 Moment "New" — Case creation

When a client email arrives or a case is manually created:

| Field | Who fills it | Example |
|---|---|---|
| Title | System (extracts email subject) or consultant (manual) | "Cannot export monthly report" |
| Customer | System (auto-lookup by sender email) or consultant | XYZ Industries (Account) or Carolina López (Contact) |
| Account (auto) | Plugin/Flow | XYZ Industries |
| Contact (auto) | Plugin/Flow | (empty in this case) |
| Primary Contact | Module 1 AI (extracts from email) or manual | Pedro Sánchez (signed the incoming email) |
| Description | System (email body) or consultant | Full text of reported problem |
| Origin | System (Email/Web Form) or consultant | Email |

**State at this point:**
- `statecode = Active`
- `statuscode = New`
- All other fields are empty.

### 7.2 Moment "AI Processing" — Automatic categorization

The plugin/Power Automate detects the case in `New` state and processes it with Azure OpenAI. The AI receives the case content + available context (list of types, categories, tiers, customer tier from Account or Contact). Returns structured JSON.

**State at this point:**
- `statuscode` changes from `New` to `AI Processing` momentarily
- After processing:
  - If confidence ≥ 0.85 → `Categorized — Awaiting Assignment`
  - If confidence < 0.85 → `Manual Review Required`

**Fields filled by AI:**

| Field | Content | Example |
|---|---|---|
| Type | Identified type | Lookup to "Support Request" |
| Business Line | Identified business line | Lookup to "IT Consulting" |
| Category | Identified L1 category | Lookup to "Technical Issue" |
| Subcategory | Identified L2 subcategory | Lookup to "Server Down" |
| Priority | Calculated priority | Critical |
| Priority Reason | Priority reason | "Strategic customer + sentiment Critical + keywords 'system down'" |
| Is Billable | Suggestion whether billable | Yes (suggested) — is a Scope Change Request |
| AI Confidence Score | How confident the AI is | 0.92 |
| AI Reasoning | AI's internal reasoning | "Subject and description indicate technical failure. Customer in Strategic tier. Sentiment shows critical urgency." |
| AI Problem | Distilled core problem | "Client reports error 500 when exporting monthly report." |
| AI Business Impact | Translated risk | "Blocks accounting close. Urgent impact." |
| AI Missing Info | Missing info to respond | "- Report version\n- Affects one user or all?" |
| AI Sentiment | Detected emotion | Frustrated |
| AI Summary | One-line executive summary | "Strategic customer's monthly export is blocking accounting close." |
| Is Automated | Whether processed by AI | Yes |
| Multi-Intent Detected | Whether multiple intents detected | No |

**Why this level of detail:**
- **AI Reasoning** is for audit: if AI miscategorizes something, we can see why.
- **AI Summary** is what the consultant reads first when opening the case (zero-friction UX).
- **AI Problem / Business Impact / Missing Info** give the consultant all information in pre-digested structure.
- **AI Sentiment** allows filtering/reporting (e.g.: "all cases with Frustrated or Critical clients").

### 7.3 Moment "SLA Assignment" — SLA calculation

After AI categorization, the system finds the applicable SLA using the matching logic documented in Section 4.

**Fields populated:**

| Field | Content | Example |
|---|---|---|
| Applied SLA | Lookup to found SLA | Lookup to "Strategic - Support Request" |
| Response Target Date | Maximum date/time for first response | May 6, 2026, 11:30 AM |
| Resolution Target Date | Maximum date/time for resolution | May 6, 2026, 6:30 PM |
| SLA Status | Current SLA state | On Track |

**How target dates are calculated:**
- If the case arrives Friday at 4pm and coverage is Business Hours 8x5 with 24hr resolution, the counter pauses Friday at 5pm, resumes Monday 9am, and the target is approximately Tuesday 5pm.
- If coverage is 24x7, no pause. Friday 4pm + 24hr target = Saturday 4pm deadline.

### 7.4 Moment "In Progress" — Working the case

The consultant takes the case (manually or assigned by AI/queue) and starts working it.

**State at this point:** `statuscode = In Progress`

**Fields populated operationally:**

| Field | Who fills it | When |
|---|---|---|
| First Response Date | System (auto on first email replied) or consultant | When consultant responds for the first time |
| Estimated Hours | Consultant | Initial estimate when starting |
| Related Opportunity | Consultant | If linked to an opportunity |

**Field derived from activities:**
- **Actual Hours** accumulates automatically from the `tavu_timeentry` activity. Each time a time entry is logged on the case, Power Automate sums all entries and updates this field.

### 7.5 Moment "Resolved" or "Cancelled" — Case closure

The consultant finishes the work and closes the case.

**State at this point:**
- `statecode` changes to `Inactive` with a **Resolved-group** statuscode ("Solved" / "Information Provided" / "Duplicate" / "Out of Scope")
- Or `statecode = Inactive` with a **Cancelled-group** statuscode ("Cancelled by Customer" / "Cannot Reproduce" / "Closed without Resolution")

**Fields populated:**

| Field | Who fills it | Example |
|---|---|---|
| Resolution Date | System (timestamp of state change) | May 6, 2026, 5:45 PM |
| Resolution Notes | Consultant | "Issue caused by missing index. Created index, validated export works." |
| SLA Status | System | Met (if Resolution Date < Resolution Target Date) or Breached |

---

## 8. Table `tavu_timeentry` — Basic time tracking

**Purpose:** record hours worked on cases (and opportunities, proposals, etc.). It is the foundation of billing and utilization metrics.

### Base configuration

| Property | Value |
|---|---|
| Display name | `Time Entry` |
| Plural | `Time Entries` |
| Schema name | `tavu_timeentry` |
| **Type** | **Activity** (NOT standard table) |
| Primary column | `Subject` (standard for activities) |
| Ownership | User or team |
| Notes | ✅ |
| Audit | ✅ |
| Quick create | ✅ |

**Why Activity Type and not Standard Table:**
- Appears automatically in the timeline of the parent record (case, opportunity)
- Supports `Regarding` polymorphic lookup OOTB — a time entry can be linked to a case, opportunity, account, etc.
- Integrates with Dataverse's native activity feed

### State + Status Reason

| State (statecode) | Status Reasons (statuscode) | Meaning |
|---|---|---|
| **Open** (default) | Draft | Consultant is editing, not yet confirmed |
| | Submitted | Consultant confirmed the entry (counts toward case Actual Hours) |
| **Completed** | Approved | Supervisor approved the entry |
| | Billed | Included in client invoice (immutable) |
| **Cancelled** | Discarded, Duplicate, Adjustment | Entry does not count toward Actual Hours |

### Custom columns

| Display Name | Schema Name | Type | Required |
|---|---|---|---|
| Subject | subject (OOTB) | Single Line of Text (Primary) | Required |
| Description | description (OOTB) | Multiple Lines of Text | Optional |
| Activity Date | actualstart (OOTB) | Date Only | Required |
| Duration (Hours) | tavu_duration | Decimal | Required |
| Is Billable | tavu_isbillable | Yes/No | Required |
| Resource | tavu_resource | Lookup → SystemUser | Required |
| Work Type | tavu_worktype | Choice | Optional |
| Billable Rate | tavu_billablerate | Currency | Optional |
| Calculated Amount | tavu_calculatedamount | Currency | Optional |
| Internal Notes | tavu_internalnotes | Multiple Lines of Text | Optional |
| Regarding | regardingobjectid (OOTB) | Lookup polymorphic | Required |

**Choice values for `tavu_worktype`:**

Discovery, Implementation, Configuration, Support, Training, Documentation, Travel, Admin, Meeting, Other.

### Automatic accumulation logic

**C# Plugin or Power Automate flow trigger** executes when a time entry changes state:

```
IF tavu_timeentry.statecode = Open (Submitted)
  OR tavu_timeentry.statecode = Completed (Approved/Billed)
AND tavu_timeentry.regardingobjectid = a tavu_case
THEN:
  1. Sum tavu_duration of ALL active entries for the case (except Cancelled)
  2. Update tavu_case.tavu_actualhours with the total
```

Cancelled entries do NOT count in the total.

### Why granularity matters

Some firms log a single time entry per case ("worked 2.5 hrs"). This loses valuable information: work type (Discovery vs Implementation have different rates), time distribution, and billable/non-billable mix. Granularity enables precise billing, utilization reporting by work type, and efficiency analysis.

### Operational restrictions

- **Time entries in Billed state CANNOT be edited.** If you need to adjust, create a new entry with state Cancelled / status Adjustment.
- **Case Actual Hours total does NOT include Cancelled entries.** This allows adjustments without losing history.
- **Resource must be an active system user.**
- **Activity Date cannot be a future date** (validate in Business Rule).

---

## 9. Complete step-by-step examples

### Example 1 — Strategic client reports a critical complaint (B2B case)

**Incoming email (Saturday 11pm):**

> From: jorge.martinez@megacorp.com
> Subject: URGENT - Production system completely down
> Body: "Our production system has been down since 10:30pm. We cannot process orders. We need immediate help."

**Step 1 — Creation (Saturday 11:00 PM):**

```
Title: URGENT - Production system completely down
Customer: MegaCorp Inc (Account)
Account (auto): MegaCorp Inc
Contact (auto): (empty)
Primary Contact: Jorge Martinez (extracted from email by Module 1 AI)
Description: [email body]
Origin: Email
statecode: Active
statuscode: New
```

**Step 2 — AI Processing (Saturday 11:01 PM):**

```
Type: Complaint
Business Line: IT Operations
Category: System Outage
Subcategory: Production Down
Priority: Critical
Priority Reason: "Strategic customer + Critical sentiment + keywords 'production down', 'urgent'"
Is Billable: No (covered support)
AI Confidence Score: 0.96
AI Problem: "Client's production system has been completely down since 10:30 PM."
AI Business Impact: "Total blockage of order processing. Direct impact on client's revenue."
AI Sentiment: Critical
AI Summary: "MegaCorp's production system fully down — revenue impact — needs immediate engineering intervention."
```

**Step 3 — SLA Assignment (Saturday 11:01 PM):**

- Customer = MegaCorp → Account → Tier = Strategic
- Type = Complaint
- Match: "Strategic - Complaint" → Response 0.5hr, Resolution 4hr, Coverage 24x7

```
Applied SLA: Strategic - Complaint
Response Target Date: May 6, 2026, 11:31 PM (30 minutes)
Resolution Target Date: May 7, 2026, 3:00 AM (4 hours)
SLA Status: On Track
```

**Step 4 — María (on-call consultant) takes the case (Saturday 11:08 PM):**

Reads AI Summary → understands context in 5 seconds. Responds to client requesting logs.

```
First Response Date: May 6, 2026, 11:08 PM
statuscode: In Progress
```

**Step 5 — Work and time tracking:**

```
Time Entry 1: 0.5h - "Diagnostic and triage" - Submitted
Time Entry 2: 0.75h - "Server restart and validation" - Submitted
Time Entry 3: 0.5h - "Customer communication" - Submitted

(Power Automate sums → Actual Hours = 1.75)
```

**Step 6 — Closure (Sunday 1:45 AM):**

```
statecode: Inactive
statuscode: Solved
Resolution Date: May 7, 2026, 1:45 AM
Resolution Notes: "Root cause: Application server OOM. Restarted server. Confirmed processing recovered."
SLA Status: Met (Resolution 1:45 AM < Target 3:00 AM)
Actual Hours: 1.75
```

---

### Example 2 — Standard client asks a general question

**Incoming email (Tuesday 10:30 AM):**

> From: ana@abccorp.com
> Subject: Question about the reports module
> Body: "Could you explain how to change the default view in the reports module?"

**Processing:**

```
Type: General Inquiry
Customer: ABC Corp (Account, Tier = Standard)
Priority: Standard
AI Sentiment: Calm
SLA Match: NO "Standard - General Inquiry" exact match exists
SLA fallback: "Standard - Default" → Response 8hr, Resolution 48hr
SLA Status: On Track
```

**Result:** Tier 1 consultant takes the case, responds with screenshot in 2 hours, closure in ~26 hours. SLA comfortably met.

---

### Example 3 — Individual client (B2C case)

**Law firm configuration:**

```
tavu_systemsettings.tavu_customermode = Mixed
```

**Incoming email:**

> From: carolina.lopez@gmail.com
> Subject: Divorce inquiry
> Body: "I need initial guidance about a divorce process."

**Carolina already exists in the system as a Contact (no associated Account), Tier = Standard.**

**Processing:**

```
Title: Divorce inquiry
Customer: Carolina López (Contact)
Account (auto): (empty — individual person)
Contact (auto): Carolina López
Primary Contact: Carolina López (auto, unchanged — same person)
Type: General Inquiry
SLA Match: Customer Tier from Contact = Standard → "Standard - Default"
Response Target: 8hr Business Hours
```

**Key:** SLA lookup uses the Contact's Tier (not Account, because there is no Account). The model naturally supports this case without additional code or a fictitious Account.

---

### Example 4 — Multi-intent detected

**Incoming email:**

> From: roberto@xyzindustries.com
> Subject: Several things
> Body: "Three topics: (1) error 500 when exporting report, (2) unrecognized charge on invoice, (3) quote for inventory module."

**Processing:**

```
AI Confidence Score: 0.62 (LOW)
Multi-Intent Detected: Yes
AI Reasoning: "Email contains 3 distinct concerns: technical issue, billing dispute, sales opportunity."
statuscode: Manual Review Required
```

**Human action:** supervisor creates 3 separate cases, closes the original as Cancelled / Duplicate documenting the split.

---

## 10. Queues — Native Dataverse routing

OpenTavu uses native queues rather than building custom routing logic.

### Basic concept

A queue is a "shared inbox" where cases wait for assignment. Any consultant with permission can:
- View cases in the queue
- "Pick" a case → assign themselves as Worked By
- "Release" a case they took but cannot continue
- Re-assign to another queue or consultant

### Typical queue examples

- Tier 1 Support Queue (all Support Requests)
- Sales Queue (all RFP/Proposal Inquiries)
- Finance Queue (all Billing Inquiries)
- Strategic Customer Queue (all cases from Strategic clients)
- On-Call Queue (all Critical priority cases)

### When is a case assigned to a queue?

After AI Processing and SLA Assignment:

```
1. If Case Type has Default Owner Team → assign to that team's queue
2. If not, use generic queue by priority (Critical → On-Call, Standard → General)
3. On-call consultant receives notification
```

---

## 11. SLA Status — How compliance is monitored

The `tavu_slastatus` choice tracks each case's SLA state. Option values (fixed across tenants):

| Label | Value | Meaning |
|---|---|---|
| On Track | 576600000 | Default at SLA assignment; within target |
| Warning | 576600001 | Early-warning threshold before the resolution target reached |
| Breached | 576600002 | Target time expired, case not yet resolved |
| Met | 576600003 | Case resolved before the target (successful final state) |

*(The 576600001 option was originally labeled "At Risk"; the label is firm-configurable, the value is not.)*

### How it is updated — push, not polling

Status transitions are driven by the **OpenTavu SLA Scheduler**, a central Azure Durable Functions service (see `architecture.md` §2b), **not** by a recurring query.

1. On categorization, the `Pl.Case.SlaAssignment` plugin resolves the SLA (Tier + Type), computes the **calendar-aware** Response/Resolution Target Dates from `createdon` (business hours + closures, DST-aware), and sets SLA Status = **On Track**.
2. The plugin calls the gateway `POST /api/sla/schedule` with the case id and the timed transitions — e.g. `{ warningTimeUtc → Warning }` and `{ resolutionTargetUtc → Breached }` — where each status is the numeric option value above.
3. The gateway holds **durable timers** that fire exactly at those times (push; survives host restarts). On fire, the write-back activity confirms the case is still open (`statecode = Active`) and sets `tavu_slastatus`. A case resolved before a timer fires is left untouched — no false Warning/Breached.
4. When a case is resolved within target, SLA Status is set to **Met**. If the SLA changes (re-categorization), the plugin calls `POST /api/sla/cancel` with the stored orchestration instance id and reschedules — so targets stay anchored to the original `createdon`, not the change date.

> **Why push over a recurring flow:** a polling flow is only as precise as its interval and burns runs continuously; durable timers fire to the second, cost nothing while idle, and cannot drift. The earlier hourly-flow design is superseded.

### Suggested Power BI reports

- % of cases meeting SLA by tier
- Cases by SLA Status (On Track / At Risk / Breached / Met)
- Monthly compliance trend by team
- Top-N customers with most SLA breaches

---

## 12. Configuration by firm type

### Small IT consultancy (12 people, B2B only)

**Minimum configuration:**
- 3 Customer Tiers (Standard, Premium, Strategic)
- 5 Case Types (General Inquiry, Support Request, RFP, Billing, Complaint)
- 3 SLA records (one default per tier)
- `tavu_customermode = B2B_Only`
- No Business Lines (only Category L1 + L2)

**Result:** simple but structured model. AI categorizes based on Type and Categories.

### Mid-size B2B agency (25 people)

**Intermediate configuration:**
- 3 Customer Tiers
- 7 Case Types (includes Scope Change Request, Account Update, Strategy Question)
- 8 SLA records (default per tier + specific for Complaint and Strategy)
- `tavu_customermode = B2B_Only`
- 2 Business Lines (Branding, Performance Marketing)

### Software QA boutique (40 people)

**Advanced configuration:**
- 4 Customer Tiers (includes "Trial" for evaluations)
- 9 Case Types (includes Bug Report, Test Cycle Request, Defect Triage)
- 12 SLA records (extensive Tier × Type matrix)
- `tavu_customermode = B2B_Only`
- 4 Business Lines (Manual Testing, Automation, Performance, Security)

**Result:** robust model. Every critical bug has a 1-hour response SLA.

### Boutique law firm (8 people, B2B + B2C hybrid)

**Configuration:**
- `tavu_customermode = Mixed`
- 2 Customer Tiers (Standard, Premium)
- 6 Case Types (includes Initial Consultation, Case Follow-up, Collections)
- 6 SLA records
- Some clients are Accounts (companies with corporate advisory); many are direct Contacts (divorces, estates, criminal defense)

**Result:** model supports both client types without artificial distinction.

---

## 13. Frequently asked questions

**Why not use the standard `incident` table from Dynamics?**

Because it requires a Dynamics 365 Customer Service license ($95+/user/month). OpenTavu operates on Power Apps Premium ($20+/user/month). The custom `tavu_case` table replicates essential functionality without the additional license.

**Why does Customer use a hybrid architecture (polymorphic + auto-populated typed fields)?**

After evaluating three options (pure separate fields, pure polymorphic, and hybrid), OpenTavu adopts hybrid because it simultaneously resolves UX and reporting without trade-offs:
- **Simple UX:** user only interacts with `tavu_customer` (single field). Via `tavu_systemsettings.tavu_customermode` the firm configures whether the lookup shows only Accounts (B2B), only Contacts (B2C), or both (Mixed).
- **Simple reporting:** Power BI connects directly to `tavu_account` and `tavu_contact` (typed auto-populated fields), without handling polymorphism.
- **Microsoft standard pattern:** Dynamics 365 Quote, Order, Invoice use exactly this pattern.

**How is a case handled for an individual client with no associated Account?**

When the client is an individual (B2C — law firms, coaches, accountants with individual clients), `tavu_customer` points directly to the Contact. Plugin auto-fills `tavu_contact` with the same value; `tavu_account` remains empty. `tavu_primarycontact` auto-fills with the same Contact (client and interlocutor are the same individual), editable. SLA is looked up matching against the Contact's Customer Tier.

**What happens if the AI Confidence Score is exactly 0.85?**

By convention: **0.85 is the inclusive threshold**. Cases with score = 0.85 ARE automatically processed. Cases with < 0.85 go to Manual Review.

**Why are some AI fields Multiple Lines (long text) instead of Choice or numbers?**

Because AI needs expressiveness. AI Reasoning, AI Problem, AI Business Impact, AI Missing Info are natural language outputs. AI Sentiment IS a Choice because the categories are finite and discrete.

**Can Status Reasons of statuscode be modified?**

Yes, they are configurable per client. The proposed values are seed data. For example, a firm could add "Pending Customer Approval" as an additional Status Reason in Active.

**How does `tavu_case` relate to `tavu_opportunity`?**

Through the `Related Opportunity` field. When a case comes from work sold in an opportunity, it is linked. This enables traceability (see all cases from an opportunity), reporting (how many cases each opportunity generates), and upsell signals (if an opportunity generates many Scope Change Request cases, that signals an upsell opportunity).

**What if a Premium client reports a case of an unspecified type (e.g.: General Inquiry)?**

System uses "Premium - Default" SLA (Response 4hr, Resolution 24hr, Extended Hours). You don't need a specific record for every combination.

**Why is `tavu_timeentry` an Activity Type and not a Standard Table?**

Because activities integrate natively into the timeline of the parent record (case, opportunity), have polymorphic `Regarding` OOTB, and appear in the activity feed without building custom UI. It is the idiomatic Dataverse way to record "things that happen TO records over time."

**What happens if a Time Entry is deleted after submission?**

Best practice: do NOT hard-delete time entries directly. Instead, change state to Cancelled / Adjustment. This preserves the complete change history and enables auditing. Hard-delete is only appropriate if state is Open / Draft.

**Why are work types like "Travel" and "Admin" in the list if they're not technical?**

Because the firm needs to report REAL utilization. If a consultant spends 8 hours weekly on Travel and 4 on Admin, that impacts capacity planning. Capturing this explicitly is better than hiding it. For billing, the Is Billable flag separates what is charged vs. what is operational overhead.

---

## Document control

| Version | Date | Author | Notes |
|---|---|---|---|
| 1.0 | May 6, 2026 | Gustavo González Villani | Initial operational guide. Covers tavu_customertierdefinition, tavu_casetype, tavu_sla, tavu_case, tavu_timeentry. |
| 1.1 | May 6, 2026 | Gustavo González Villani | Added complete `tavu_timeentry` documentation: base configuration as Activity Type, custom columns with types and purpose, state/statuscode pattern for approval flow, accumulation logic in `tavu_case.tavu_actualhours`, granular example, operational restrictions, and 3 additional FAQs. |
| 1.2 | May 8, 2026 | Gustavo González Villani | Full restructuring to unify format with Sales Guide. Adopted hybrid Customer field architecture in tavu_case; added tavu_primarycontact; added tavu_iscustomer, tavu_customersince, tavu_lastengagementdate to account; adjusted SLA matching algorithm to support Customer Tier from Account OR Contact (B2C case); expanded FAQ; added B2B+B2C hybrid law firm vertical as example; complete spec for tavu_casetype, tavu_customertierdefinition, tavu_sla with seed data. |
| 1.3 | June 17, 2026 | Gustavo González Villani (revision with Claude) | **Statecode correction.** Verified against the live schema that `tavu_case`, being a custom table, has only Active / Inactive statecodes — there are no Resolved or Cancelled states. Section 6 State/Status-Reason table consolidated into Active + Inactive, with "Resolved" and "Cancelled" reclassified as status-reason groups inside Inactive; added an explanatory note; updated Section 7.5 and Example 1 closure to set `statecode = Inactive`; fixed the Section 1 flow diagram. Aligns with view-definitions v1.3 (Resolved/Cancelled views filter by `statecode = Inactive` + a `statuscode` group). |
| 1.4 | June 17, 2026 | Gustavo González Villani (revision with Claude) | Added Section 3.1 specifying the case classification cascade (`tavu_businessline`, `tavu_category`, `tavu_subcategory`): common base config (Name primary, Code as read-only autonumber, Active/Inactive states, Quick create), columns, **required parent lookups** (Category→Business Line, Subcategory→Category) for referential integrity, Sort Order, and the **AI Categorization Hint** (plain text, injected into the Module 1 prompt, kept off the default form). Documented that cascade depth is optional by how many levels a firm populates — not by relaxing requiredness — and the two-axis model (this cascade = topical "what it's about" vs `tavu_casetype` = operational "how it's handled"). |
| 1.5 | June 17, 2026 | Gustavo González Villani (revision with Claude) | Corrected `tavu_responsetargethours` and `tavu_resolutiontargethours` from Whole Number to **Decimal (2 dp)** so sub-hour SLA targets work (e.g., 0.5h = 30 min, 0.25h = 15 min) as used in the Strategic-Complaint seed. |
| 1.6 | June 17, 2026 | Gustavo González Villani (revision with Claude) | Added Section 3.2 documenting the AI configuration layer: `tavu_aimodel` (model catalog with provider, endpoint, deployment, API version, secret name, cost tier, is-default), `tavu_aitaskconfig` (task→model mapping with temperature, max output tokens, confidence threshold, token budget, plain-text system prompt), and the AI fields added to the `tavu_systemsettings` singleton (AI Enabled kill switch, Default AI Model, Default Confidence Threshold). Documented the runtime resolution order, the secret-by-name rule (keys live in env var / Key Vault, never Dataverse), and forward-compatibility with the managed-service AI gateway via the `IAIProvider` abstraction. |
| 1.10 | July 2, 2026 | Gustavo González Villani (revision with Claude) | Corrected calendar field logical names against the live schema: **Is 24x7** = `tavu_is24x7` (was `tavu_is247`), working-hours **Start Time** = `tavu_starttime` and **End Time** = `tavu_endtime` (were `tavu_startminutes`/`tavu_endminutes`). Values are still minutes-from-midnight. Consumed by `Pl.Case.SlaAssignment`. |
| 1.9 | July 2, 2026 | Gustavo González Villani (revision with Claude) | Corrected the case's **Applied SLA** lookup logical name from `tavu_appliedsla` to **`tavu_sla`** (verified against the live schema) and noted it is auto-filled by the `Pl.Case.SlaAssignment` plugin. Customer tier for SLA matching is read from the polymorphic `tavu_customer` (account or contact), each carrying `tavu_customertier`. |
| 1.8 | July 2, 2026 | Gustavo González Villani (revision with Claude) | Rewrote **Section 11 — SLA Status** to reflect the implemented push architecture: documented the `tavu_slastatus` option values (On Track 576600000 / Warning 576600001 / Breached 576600002 / Met 576600003), replaced the superseded hourly Power Automate polling flow with the **OpenTavu SLA Scheduler** (Azure Durable Functions, `architecture.md` §2b) — `Pl.Case.SlaAssignment` computes calendar-aware targets and calls `/api/sla/schedule` with timed transitions; durable timers fire push-based, the write-back only acts if the case is still open, and re-categorization cancels/reschedules via `/api/sla/cancel`. |
| 1.11 | July 4, 2026 | Gustavo González Villani (revision with Claude) | Added **Section 6.1 — Case conversation model** (`tavu_caseinteraction` + native `annotation` + native `appointment`). Documented: the interaction table (User/Team owned, Notes required, plain-text body, `tavu_direction` Inbound/Outbound/Internal-Note and `tavu_channel` Email/System choice values, `tavu_fromcontact`, `tavu_statusbefore`/`After`, `tavu_changedfields`); the `CaseConversation` PCF (compose at top, newest-first descending, 10-item pagination + "load older", bubbles, per-interaction status delta); **attachments reusing native annotations rendered inline by the PCF** (verified File-tier storage ~$2/GB vs Database ~$40/GB, so cost-neutral vs a custom table; custom `tavu_interactionattachment` deferred until AI per-attachment metadata is needed); **scheduled sessions via native `appointment`** mirrored into the thread as a system-line interaction (no calendar rebuild); and the **email intake/outbound flows** with the transport-vs-brain split (flow = SMTP transport, gateway `/api/intake` = Module 1 decisioning) and the no-lock-in rule that outbound send stays in the client-tenant flow, never the gateway. Rationale tied to Module 2 and the design mindset. |
| 1.12 | July 4, 2026 | Gustavo González Villani (revision with Claude) | **Made email intake provider-agnostic in Section 6.1.** Replaced the Office-365-flow-centric intake with three mechanisms — (1) **gateway IMAP-pull** as the canonical, provider-agnostic path (timer function fetches by IMAP from any mailbox and writes via S2S / Entra Application User, **no Power Automate flow**), (2) optional inbound-parse webhook (SendGrid/Mailgun/Cloudflare) for push/near-real-time, (3) Office 365 flow only as a Microsoft-only convenience — all converging on `/api/intake` as the brain. Added the rule that the support address must be a **dedicated mailbox, not an alias** (else IMAP-pull ingests personal mail). Reworked **outbound** to send via Power Automate's generic **SMTP connector** (client's own SMTP: Hostinger/Zoho/Gmail), with O365 Outlook only if the client is on Microsoft; reaffirmed the no-lock-in rule (send stays in the client tenant; gateway does receive + decisioning, never send). Rationale: a client's mailbox may not be Microsoft even though the CRM runs on Power Platform. |
| 1.13 | July 6, 2026 | Gustavo González Villani (revision with Claude) | Added **`tavu_casenumber`** (Autonumber, e.g. `OT-{SEQNUM:00000}-{RANDSTRING:4}`) to `tavu_case` (Section 6) and switched the §6.1 email **threading token** from the case GUID to this short, human-readable, non-guessable case number (`[OT-00042-A3F9]`). The gateway resolves appends by `tavu_casenumber`. Aligns with the intake code (ThreadToken + DataverseClient.FindCaseIdByNumberAsync). |
| 1.14 | July 6, 2026 | Gustavo González Villani (revision with Claude) | Documented **`tavu_code`** on `tavu_customertierdefinition` (Section 2; autonumber, e.g. `CTD-1000` = Standard) — it existed in the live schema but wasn't in the guide. Noted its use as the tenant-stable config key for the email-intake default tier (`Intake:DefaultCustomerTierCode`), resolved to the id at runtime (no hardcoded GUID) so auto-created inbound contacts get a tier and the SLA plugin can resolve an SLA. |
| 1.15 | July 6, 2026 | Gustavo González Villani (revision with Claude) | **Implemented the SLA system-default fallback (reverses the intake tier-stamping from v1.14).** `Pl.Case.SlaAssignment` now, when the customer has no tier, reads `tavu_systemsettings.tavu_defaultcustomertier` and applies that tier instead of skipping — covering unknown inbound senders, tier-less existing contacts, and manual cases. Correspondingly, the email-intake gateway **no longer stamps a tier** on auto-created contacts (a blank tier is honest; the machine must not rewrite customer master data), and the `Intake:DefaultCustomerTierCode` setting was removed. Added `tavu_defaultcustomertier` to the `tavu_systemsettings` spec (§3.2). This realizes §4 matching step (c), previously documented but not implemented. |
| 1.7 | July 1, 2026 | Gustavo González Villani (revision with Claude) | Added **Section 4.1 — Business calendars** (`tavu_businesscalendar`, `tavu_calendarworkinghours`, `tavu_businessclosure`): schedule header (Time Zone as Whole Number/Time Zone format, Is 24x7, Is Default), working intervals (multiple per weekday for split shifts/lunch; Start/End as a "Time of Day" choice whose value = minutes from midnight), holidays; all with autonumber Code. Added a `tavu_calendar` lookup to `tavu_sla` and **deprecated `tavu_coveragehours`** (superseded by the calendar, mirroring Dynamics' `SLA.BusinessHours`). Documented the SLA engine's calendar-aware, DST-aware target-date calculation anchored to `createdon`, and that specific calendars/holidays are per-client config (not canonical seed). |

*This document is the operational reference for OpenTavu's service model.*
