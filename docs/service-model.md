# Service Model Operational Guide — OpenTavu

## How the AI-first case management system with configurable SLA works

**Audience:** Power Platform consultants, MVPs, integrators adopting OpenTavu, future contributors, and any AI system that needs context about the model.

**Purpose:** explain what each service model table does, what each field does, and when each field is populated. The guide is organized by tables with complete specifications, complemented by operational flow narrative and real examples.

**Last updated:** May 8, 2026

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
[Consultant closes → Status: Resolved/Cancelled]
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
| Description | tavu_description | Multiple Lines of Text | Optional |
| Sort Order | tavu_sortorder | Whole Number | Optional |
| Display Color | tavu_displaycolor | Single Line of Text (hex) | Optional |

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
| Response Target Hours | tavu_responsetargethours | Whole Number | Required |
| Resolution Target Hours | tavu_resolutiontargethours | Whole Number | Required |
| Coverage Hours | tavu_coveragehours | Choice (24x7, Business Hours 8x5, Extended Hours 12x5) | Optional |
| Evaluation Priority | tavu_evaluationpriority | Whole Number | Required |
| Description | tavu_description | Multiple Lines of Text | Optional |

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

| State (statecode) | Status Reasons (statuscode) |
|---|---|
| **Active** (default) | New, AI Processing, Categorized — Awaiting Assignment, In Progress, Manual Review Required, Waiting on Customer |
| **Resolved** | Solved, Information Provided, Duplicate, Out of Scope |
| **Cancelled** | Cancelled by Customer, Cannot Reproduce, Closed without Resolution |

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
| Applied SLA | tavu_appliedsla | Lookup → tavu_sla | Optional | Auto-filled by system |
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
- `statecode` changes to `Resolved` (with statuscode "Solved" / "Information Provided" / "Duplicate" / "Out of Scope")
- Or changes to `Cancelled` (with statuscode "Cancelled by Customer" / "Cannot Reproduce" / "Closed without Resolution")

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
statecode: Resolved
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

The `tavu_slastatus` field is dynamically updated based on case progress.

### Calculation logic

```
On Track:  More than 50% of target time remaining
At Risk:   Between 20% and 50% of time remaining
Breached:  Time expired, case not yet resolved
Met:       Case resolved before target date (successful final state)
```

### Recurring Power Automate flow

Every hour (configurable), a scheduled flow:
1. Queries Active cases with unexpired Resolution Target Date
2. Calculates remaining time percentage
3. Updates SLA Status according to the rules
4. Sends notifications when status changes to At Risk or Breached

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

*This document is the operational reference for OpenTavu's service model.*
