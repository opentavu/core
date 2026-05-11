# Sales Model Operational Guide — OpenTavu

## How the Single Lifecycle, Dual Entry model works for Professional Services SMBs

**Audience:** Power Platform consultants, MVPs, integrators adopting OpenTavu, future contributors, and any AI system that needs context about the model.

**Purpose:** explain what each sales model table does, what each field does, and when each field is populated. The guide is organized around real operational flows, not tables in isolation — because no field exists in a vacuum; every field is part of a business process.

**Last updated:** May 8, 2026

---

## 1. Commercial model overview

Unlike traditional B2C CRMs — where everything enters as a "Lead" and must be qualified before becoming a "Contact" — OpenTavu is designed for **Professional Services SMBs**, explicitly supporting three client models:

- **B2B only** — the firm sells exclusively to companies (IT consultancies, B2B agencies, software/QA boutiques)
- **B2C / individuals** — the firm sells to natural persons (some law firms, independent coaches)
- **Hybrid** — the firm serves both simultaneously (boutique law firms with corporate and individual clients, accountants with business and personal clients, wealth managers)

OpenTavu adopts a hybrid architecture that covers all three models without forcing artificial adaptations in any case.

In B2B consulting, relationships typically begin with a direct contact, a referral, or a meeting at an event. Forcing the consultant to create a "Lead" and then "convert" it introduces administrative friction and CRM abandonment (Pain #1 — the most documented pain point in the evidence triangulation).

OpenTavu resolves this with the **Single Lifecycle, Dual Entry** model, using twelve interrelated tables.

> **Terminology note:** "Single Lifecycle" refers to the lifecycle of **opportunities** (where the commercial cycle truly lives), NOT to a Stage field on Contact. Early versions of the model proposed a Lifecycle Stage field on Contact; this was corrected when it became clear that in Professional Services the commercial subject is the Account (B2B) or the Contact (B2C), and that its state is better derived from relationships with opportunities and cases than from a label field. See Section 5 for detail.

### Sales model tables

| Table | Role | Who edits |
|---|---|---|
| `account` (Dataverse standard) | Corporate client accounts; each has an assigned Customer Tier | Sales / Ops during client onboarding |
| `contact` (Dataverse standard) | People; carries engagement status and customer flags | Sales daily |
| `tavu_lead` | Ingestion buffer for low-quality anonymous inbound | System (AI) first, then sales |
| `tavu_opportunity` | Discovery-driven commercial pipeline | Sales / Sales Manager |
| `tavu_opportunityclose` (Activity) | Historical log of every close attempt (Won/Lost/Reopen) | Sales via guided pop-up |
| `tavu_proposal` | SOWs and proposals linked to opportunities | Sales + future AI Proposal Generator |
| `tavu_proposalline` | Quotation lines — the single grid the seller sees | Sales when building a proposal |
| `tavu_product` | Master catalog of services, licenses, and kits | Admin / Operations Manager |
| `tavu_uom` | Units of measure (Hour, Month, License…) | Admin during setup |
| `tavu_kitcomponent` | BOM: internal composition of kits (hidden recipe) | Admin / Operations Manager |
| `tavu_pricelist` + `tavu_pricelistitem` | Price lists by currency and rate | Admin / Operations Manager |
| `tavu_servicerole` | Delivery roles with rate and cost per profile | Admin / Operations Manager |

### Single Lifecycle, Dual Entry flow

```
[Path A: Outbound / Networking] ──→ Contact created directly (Engagement Status: Engaged)
                                            ↓
                                    tavu_opportunity created
                                            ↓
[Path B: Anonymous Inbound] ──────→ tavu_lead created (Buffer)
                                            ↓
                                    AI Hygiene evaluates and promotes to Contact
                                            ↓
                                    tavu_opportunity created
                                            ↓
[Opportunity Management] ─────────→ Advances through: Discovery → Proposal → Negotiation
                                            ↓
[Close] ───────────────────────────→ "Close" button opens guided pop-up
                                            ↓
[Transactional Orchestration] ────→ Creates tavu_opportunityclose Activity (historical log)
                                       + Updates mirror fields in tavu_opportunity
                                            ↓
[If signed] ───────────────────────→ tavu_proposal created (SOW linked to opportunity)
```

---

## 2. Path A — Outbound / Networking (the common case in Professional Services)

This is the natural flow: the consultant identifies a real opportunity through their network, an event, a referral, or an existing relationship.

### 2.1 Direct Contact creation

**When it happens:** consultant meets someone at an event, receives a referral from an existing client, identifies a prospect on LinkedIn who knows their work.

**Action:** consultant creates the `contact` directly, linked to the `account` (existing or new).

**Fields populated on contact:**

| Field | Schema | Who fills it | Example |
|---|---|---|---|
| First Name + Last Name | OOTB standard | Consultant | "Carlos Méndez" |
| Email | OOTB standard | Consultant | "carlos@megacorp.com" |
| Phone | OOTB standard | Consultant | "+1 555-0142" |
| Job Title | OOTB standard | Consultant | "CTO" |
| Account | parentcustomerid (OOTB) | Consultant | Lookup → MegaCorp |
| **Engagement Status** | **tavu_engagementstatus** | **Consultant** | **Engaged** |

**State at this point:** contact exists in the system. NO opportunity yet.

### 2.2 When the opportunity is created

**When it happens:** the conversation with Carlos progresses. There is concrete interest in a project. There is a tentative budget. There is a defined timeline.

**Action:** consultant creates `tavu_opportunity` from the contact.

**Key point about Path A:** **`tavu_lead` is NEVER used in this flow.** The consultant works with a full Account + Contact from the first minute.

---

## 3. Path B — Anonymous Inbound (when `tavu_lead` IS used)

This flow applies when an external signal arrives that cannot be cleanly attributed to an existing Account/Contact.

### 3.1 Typical inbound cases that generate a tavu_lead

- Website form submission ("I'd like more information")
- Generic email received at `info@company.com` or `sales@`
- LinkedIn message from someone NOT in the CRM
- Lead sent by a partner with incomplete data

### 3.2 The `tavu_lead` table — Configuration

**Base configuration:**

| Property | Value |
|---|---|
| Display name | `Lead` |
| Plural | `Leads` |
| Schema name | `tavu_lead` |
| Primary column | `Subject` (`tavu_subject`) |
| Ownership | User or team |
| Activities ✅ | Notes ✅ |

**State + Status Reason:**

| State (statecode) | Status Reasons (statuscode) |
|---|---|
| **Active** (default) | New, AI Processing, Awaiting Human Review, Manual Review Required |
| **Inactive** | Promoted to Contact, Discarded as Noise, Duplicate, Not Qualified, Stale |

**Custom columns:**

| Display Name | Schema | Type | When populated |
|---|---|---|---|
| Subject | tavu_subject | Single Line (Primary) | On creation (auto-extracted from email subject) |
| Source | tavu_source | Choice | On creation (Web Form, Email, LinkedIn, Partner Referral, Other) |
| Source Details | tavu_sourcedetails | Multiple Lines | On creation (raw text of received message) |
| Email | tavu_email | Single Line (Email) | On creation (extracted from sender) |
| Phone | tavu_phone | Single Line (Phone) | On creation (if in message) |
| First Name | tavu_firstname | Single Line | On creation (extracted or manual) |
| Last Name | tavu_lastname | Single Line | On creation (extracted or manual) |
| Company Name (raw) | tavu_companyname | Single Line | On creation (extracted, unvalidated) |
| Matched Account | tavu_matchedaccount | Lookup → Account | AI fills when it finds a match |
| Matched Contact | tavu_matchedcontact | Lookup → Contact | AI fills when it finds a match |
| AI Confidence Score | tavu_aiconfidencescore | Decimal (0-1) | AI fills after processing |
| AI Recommendation | tavu_airecommendation | Multiple Lines | AI fills: promote / discard / review |
| Promoted Contact | tavu_promotedcontact | Lookup → Contact | Filled when promoted |
| Days in Buffer | tavu_daysinbuffer | Whole Number | Calculated daily by scheduled flow |
| Last AI Processing Date | tavu_lastaiprocessingdate | DateTime | AI fills after each processing |

### 3.3 Lead processing flow (Path B)

**Step 1 — Auto-creation on inbound signal:**
Power Automate detects new email to `info@` or web form submission → creates `tavu_lead` with status `New`.

**Step 2 — Module 3 AI processes (target < 2 minutes):**
- Does it match an existing Contact? (email lookup) → if YES, link and notify
- Does it match an existing Account? (company name lookup) → if YES, link
- Is it spam/noise? → if YES, status = `Inactive / Discarded as Noise`
- Is it a duplicate? → if YES, status = `Inactive / Duplicate`
- Fill AI Confidence Score, AI Recommendation
- If confidence ≥ 0.85 → auto-promote to Contact + Account, status = `Inactive / Promoted to Contact`
- If 0.50 ≤ confidence < 0.85 → status = `Awaiting Human Review`, notify sales
- If confidence < 0.50 → status = `Inactive / Discarded as Noise`

**Step 3 — Human review (if needed):**
Sales rep reads AI Recommendation and decides:
- Promote → creates Contact + Account, lead = `Inactive / Promoted to Contact`
- Discard → lead = `Inactive / Discarded as Noise`

**Step 4 — Auto-cleanup of stale leads:**
Scheduled Power Automate flow (runs daily):
- Query: leads where `statecode = Active` AND `statuscode = Awaiting Human Review` AND `tavu_daysinbuffer > 14`
- Action: change to `Inactive / Stale`
- Optional notification to owner

This prevents abandoned leads from polluting views indefinitely.

### 3.4 Why this design

- **Respects how consultants work:** Path A is the common case, Path B is the exception
- **Preserves the buffer's function:** low-quality anonymous inbound does NOT pollute the master Contact database
- **Gives Module 3 a clear role:** orchestrates automatic promotion with confidence threshold
- **Captures audit trail:** each processed lead leaves a record of what AI decided and why

---

## 4. The `account` table — Customer Tier for SLA and prioritization

OpenTavu uses Dataverse's standard `account` table (mixed architecture decision — see VISION.md Section 6).

### 4.1 Custom columns added to the standard table

| Display Name | Schema Name | Type | Required | Default |
|---|---|---|---|---|
| Customer Tier | tavu_customertier | Lookup → tavu_customertierdefinition | Optional | Standard |
| Is Customer | tavu_iscustomer | Yes/No | Optional | No |
| Customer Since | tavu_customersince | Date Only | Optional | (empty) |
| Last Engagement Date | tavu_lastengagementdate | DateTime | Optional | (empty) |

**When populated:** when a new account is created. If not specified, defaults to Standard.

**Automatic logic for Is Customer (Plugin/Flow on opportunity Won close):**

```
IF tavu_opportunity changes to state = Won
  AND opp_customer points to Account:
    → account.tavu_iscustomer = Yes (if it was No)
    → account.tavu_customersince = today (if null, NOT overwritten)

IF opp_customer points to Contact:
    → logic applies to Contact, not Account (see Section 5)
```

`tavu_iscustomer` is NEVER automatically changed to No. That is a human decision when the relationship formally ends. `tavu_lastengagementdate` is updated by Module 3 (Activity Capture) when engagement is detected.

**Importance for sales:** the tier influences:
- Opportunity priority (Strategic accounts typically receive more attention)
- SLA for cases associated with the client (documented in the service model guide)
- Pipeline reports by tier
- Forecast accuracy (Strategic accounts have more historical data)

---

## 5. The `contact` table — People in the system: client or interlocutor?

`contact` is the master table for people. In OpenTavu, a person can have two possible roles:

- **Interlocutor of an Account** (typical B2B case): Carlos Méndez is CTO of RetailCorp — he is not a client himself, but he is the person we talk to
- **Direct client** (B2C / individual case): Carolina López contracts directly with the law firm for her divorce; SHE is the client, there is no associated Account

This duality matters because the model must NOT force "converting" Carolina into a fictitious Account, nor assume that Carlos as a Contact is "a client" when the actual client is RetailCorp.

### 5.1 Custom columns added to the standard `contact` table

| Display Name | Schema Name | Type | Required | Default |
|---|---|---|---|---|
| Is Customer | tavu_iscustomer | Yes/No | Optional | No |
| Customer Since | tavu_customersince | Date Only | Optional | (empty) |
| Engagement Status | tavu_engagementstatus | Choice | Optional | Cold |
| Last Engagement Date | tavu_lastengagementdate | DateTime | Optional | (empty) |
| Customer Tier | tavu_customertier | Lookup → tavu_customertierdefinition | Optional | Standard |

**Choice values for `tavu_engagementstatus`:**

| Value | Meaning |
|---|---|
| **Cold** (default) | No recent engagement |
| **Engaged** | Recent activity (emails, meetings) |
| **Inactive** | Was Engaged but no activity in 90+ days (configurable) |

### 5.2 `tavu_iscustomer` logic on Contact

```
IF tavu_opportunity changes to state = Won
  AND opp_customer points to Contact (B2C case):
    → contact.tavu_iscustomer = Yes (if it was No)
    → contact.tavu_customersince = today (if null)

IF opp_customer points to Account (B2B case):
    → contact.tavu_iscustomer is NOT modified
    → the client flag goes to the Account
```

**Important:** a Contact acting as an Account's interlocutor is NOT marked as `tavu_iscustomer = Yes`. The client flag reflects the actual commercial subject, not a communication role.

### 5.3 `tavu_engagementstatus` logic

- **Cold:** initial value. Person in the database with no recent activity.
- **Engaged:** set manually by consultant or automatically by Module 3 (Activity Capture) when it detects recent emails/meetings.
- **Inactive:** scheduled Power Automate flow moves to Inactive if no activity in X days (default 90, configurable in `tavu_systemsettings` via `tavu_activitythresholddays`).

**Useful combinations for reporting:**

| Is Customer | Engagement Status | Meaning |
|---|---|---|
| No | Cold | Cold prospect in database |
| No | Engaged | Active prospect (in discovery, no opp yet) |
| Yes | Engaged | Active client with ongoing communication |
| Yes | Inactive | Historical client with no recent activity — re-engagement candidate |
| Yes | Cold | Client with whom communication has gone cold |

### 5.4 Customer Tier on Contact (B2C case)

For cases where the Contact is a direct client (not an interlocutor), Customer Tier can be assigned directly to the Contact:
- **Law firm:** Carolina López can be Tier "Strategic" as a high-value individual client
- **Wealth management:** a High Net Worth Individual with $5M in assets can be Tier "Strategic"

When the Contact is an Account's interlocutor, the tier is inherited from the Account; the Tier field on Contact is left empty or ignored in reports.

### 5.5 The 360 view is built from relationships, not from a Stage field

When a consultant opens a Contact form (whether interlocutor or direct client), the N:1 relationships from other tables provide the complete picture:
- **Opportunities** where they are Primary Contact (Won/Lost/Open)
- **Proposals** where they are primary contact
- **Cases** where they are Primary Contact (Active/Resolved)
- **Time Entries** accumulated (total effort)
- **Activity timeline** (emails, meetings, calls)
- **Notes** about the person

The same applies when opening an Account form: you see all associated opportunities, proposals, cases, and contacts.

**This is the real 360 view**, without needing a Stage field that tries to summarize everything into a single label.

### 5.6 Why Lifecycle Stage was removed from the model

Early versions of OpenTavu proposed `tavu_lifecyclestage` on Contact with values Cold → Engaged → Qualified → In Active Opportunity → Customer → Lost. This approach was discarded for three reasons:

1. **Conceptual inconsistency:** in B2B the client is the Account, not the Contact. Stage on Contact mixes "Contact state" with "commercial relationship state."
2. **Does not support clean upsell:** what Stage does an existing Customer with an open upsell opportunity have? "Customer" or "In Active Opportunity"? A single label cannot capture the reality.
3. **The commercial subject varies:** in B2C the client IS the Contact. A generic Lifecycle Stage on Contact applies differently for B2B (Contact is interlocutor) vs. B2C (Contact is client).

The separation of `tavu_iscustomer` (contractual relationship) and `tavu_engagementstatus` (communication relationship) better reflects operational reality and supports all three models (B2B, B2C, hybrid) without ambiguity.

---

## 6. The `tavu_opportunity` table — The commercial pipeline

This is the central table of the sales model. Each opportunity represents a specific deal with a client.

### 6.1 Base configuration

| Property | Value |
|---|---|
| Display name | `Opportunity` |
| Plural | `Opportunities` |
| Schema name | `tavu_opportunity` |
| Primary column | `Topic` (`tavu_topic`) |
| Ownership | User or team |
| Activities ✅ | Notes ✅ |
| Enable for queues | ✅ |

### 6.2 State + Status Reason

OpenTavu adopts the Dynamics OOTB pattern but with discovery-driven stages (NOT Quote-driven):

| State (statecode) | Status Reasons (statuscode) | Meaning |
|---|---|---|
| **Open** (default) | Discovery | Investigating need and fit |
| | Proposal Drafted | Working on the internal proposal |
| | Proposal Sent | Proposal sent to client |
| | Negotiation | Negotiating final terms |
| **Won** | Won | Closed successfully |
| **Lost** | Lost | Closed without success (reason in tavu_lostreason) |

The discovery-driven stages (Discovery, Proposal Drafted, Proposal Sent, Negotiation) are **Status Reasons of the Open state**, NOT custom Choice values. This respects the native Dataverse pattern.

**Granular Lost Reason:** when state changes to Lost, the specific reason (Price, Competitor, Timing, etc.) is captured in the custom field `tavu_lostreason`, which is tied to a Global Choice. This is done this way because `statuscode` cannot be linked to Global Choices (Dataverse technical limitation).

### 6.3 Custom columns

**Hybrid Customer architecture (key architectural decision):**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Topic | tavu_topic | Single Line (Primary) | Required | |
| **Customer** | **tavu_customer** | **Customer (polymorphic Account+Contact)** | **Required** | **Single source of truth** |
| **Account (auto)** | **tavu_account** | **Lookup → Account** | **Optional** | **Auto-populated when Customer=Account** |
| **Contact (auto)** | **tavu_contact** | **Lookup → Contact** | **Optional** | **Auto-populated when Customer=Contact** |
| Primary Contact | tavu_primarycontact | Lookup → Contact | Optional | Human interlocutor |
| Estimated Revenue | tavu_estimatedvalue | Currency | Optional | |
| Estimated Close Date | tavu_estimatedclosedate | Date Only | Optional | |
| Probability | tavu_probability | Whole Number (0-100) | Optional | |
| Engagement Type | tavu_engagementtype | Choice | Optional | One-time Project, Retainer, Ongoing, T&M |
| Discovery Notes | tavu_discoverynotes | Multiple Lines | Optional | |
| Source Lead | tavu_sourcelead | Lookup → tavu_lead | Optional | If it came from Path B |
| Lost Reason | tavu_lostreason | Lookup → Global Choice tavu_global_lostreason | Optional | Mirror from close activity |
| Actual Revenue | tavu_actualrevenue | Currency | Optional | Mirror |
| Actual Close Date | tavu_actualclosedate | Date Only | Optional | Mirror |
| Close Notes | tavu_closenotes | Multiple Lines | Optional | Mirror |
| Customer Tier (denorm) | tavu_customertier | Lookup → tavu_customertierdefinition | Optional | Auto from Account OR Contact |

**Plugin/Flow logic for auto-population (on create or modify of tavu_customer):**

```
IF tavu_customer points to Account:
  → tavu_account = that Account
  → tavu_contact = (empty)
  → tavu_primarycontact NOT auto-populated (consultant fills manually)
  → tavu_customertier = Customer Tier of Account

IF tavu_customer points to Contact:
  → tavu_account = (empty)
  → tavu_contact = that Contact
  → tavu_primarycontact = that Contact (auto, EDITABLE — consultant can change)
  → tavu_customertier = Customer Tier of Contact
```

**Why this hybrid architecture:**
- **Simple UX:** user only interacts with the Customer field. `setEntityTypes` (via systemsettings) filters by firm preference.
- **Simple reporting:** Power BI uses `tavu_account` and `tavu_contact` directly, without handling polymorphism.
- **Simple integrations:** point to typed specific fields.
- **Microsoft standard pattern:** Dynamics 365 Quote, Order, and Invoice use exactly this pattern.

### 6.4 Customer Mode configuration

OpenTavu allows each firm to configure the Customer lookup behavior according to their business model. This configuration lives in `tavu_systemsettings`:

```
tavu_systemsettings (single record, Organization-owned)
  tavu_customermode (Choice):
    B2B_Only  — firm sells only to companies
    B2C_Only  — firm sells only to individuals
    Mixed     — firm sells to both (default)
```

**JavaScript implementation (in form OnLoad):**

```javascript
const settings = await getSystemSettings();
const customerControl = formContext.getControl("tavu_customer");

if (settings.customermode === "B2B_Only") {
    customerControl.setEntityTypes(["account"]);
} else if (settings.customermode === "B2C_Only") {
    customerControl.setEntityTypes(["contact"]);
}
// If "Mixed", no filtering → both available
```

This pattern is applied consistently in `tavu_opportunity`, `tavu_case`, and any future table with a Customer field.

**Why the "mirror fields" (tavu_lostreason, actualrevenue, actualclosedate, closenotes)?**

These fields live in `tavu_opportunity` even though the "primary" information is captured in the `tavu_opportunityclose` activity. The duplication is deliberate for two reasons:
1. **Power BI reports:** filtering by `tavu_opportunity.tavu_lostreason` is trivial. Filtering by related activities requires complex joins.
2. **Operational views:** a consultant who opens the opportunity sees the loss reason without having to open the activity. Saves clicks.

A Plugin/Power Automate automatically syncs both places when `tavu_opportunityclose` is created/modified.

---

## 7. The `tavu_opportunityclose` activity — Historical close log

Instead of simply changing a Status field on the opportunity, OpenTavu creates an **activity record** every time a close is attempted.

### 7.1 Why Activity Type?

- **Appears automatically in the opportunity timeline**
- **Allows multiple close/reopen attempts** (each leaves a trace)
- **Captures rich context** (notes, actual date, reason) without polluting the main opportunity form
- **Inherent audit trail** (who closed, when, what they said)

### 7.2 Base configuration

| Property | Value |
|---|---|
| Display name | `Opportunity Close` |
| Plural | `Opportunity Closures` |
| Schema name | `tavu_opportunityclose` |
| **Type** | **Activity** |
| Primary column | `Subject` (standard for activities) |
| Ownership | User or team |
| Notes ✅ | Audit ✅ |

### 7.3 Custom columns

| Display Name | Schema | Type | Required |
|---|---|---|---|
| Subject | subject (OOTB) | Single Line (Primary) | Required |
| Activity Date | actualstart (OOTB) | Date Only | Required |
| Description (Close Notes) | description (OOTB) | Multiple Lines | Required |
| Close Type | tavu_closetype | Choice (Won, Lost) | Required |
| **Lost Reason** | **tavu_lostreason** | **Lookup → Global Choice tavu_global_lostreason** | Required when Close Type = Lost |
| **Actual Revenue** | **tavu_actualrevenue** | **Currency** | Required when Close Type = Won |
| **Actual Close Date** | **tavu_actualclosedate** | **Date Only** | Required |
| Regarding | regardingobjectid (OOTB) | Lookup polymorphic → tavu_opportunity | Required |
| Resource | tavu_resource | Lookup → SystemUser | Required |

### 7.4 Close mechanics — Ribbon buttons

OpenTavu adds two custom buttons to the `tavu_opportunity` Ribbon:

**"Close Won" button:**
1. Opens Main Form Dialog (guided pop-up)
2. Pre-fills Close Type = Won
3. Hides Lost Reason field (not applicable)
4. Shows: Actual Revenue (required), Actual Close Date (default = today), Close Notes
5. On Confirm:
   - Creates `tavu_opportunityclose` record
   - Changes opportunity statecode to Won, statuscode to Won
   - Plugin/Flow syncs mirrors: actualrevenue, actualclosedate, closenotes
   - Auto-changes Primary Contact's Engagement Status to Engaged (or flags for manual review)
   - Auto-marks Account or Contact `tavu_iscustomer = Yes`

**"Close Lost" button:**
1. Opens Main Form Dialog
2. Pre-fills Close Type = Lost
3. Hides Actual Revenue (not applicable)
4. Shows: Lost Reason (required, Global Choice dropdown), Actual Close Date, Close Notes
5. On Confirm:
   - Creates `tavu_opportunityclose` record
   - Changes opportunity statecode to Lost, statuscode to Lost
   - Plugin/Flow syncs mirrors: lostreason, actualclosedate, closenotes
   - If NO other open opportunities for Primary Contact → suggests updating engagement status

### 7.5 Reopening opportunities

**Option 1 — Reopen same opportunity:**
- Change statecode from Lost to Open manually
- Create new `tavu_opportunityclose` with description "Reopened: client returned with new requirements"
- Opportunity timeline shows both events (close + reopen)

**Option 2 — Create new opportunity (recommended):**
- Keep Lost opportunity closed (historical data preserved)
- Create new opportunity linked to same Account/Contact
- Reference previous opportunity in Discovery Notes

The choice between Option 1 and Option 2 depends on whether context/scope changed significantly. If it's the same deal resumed: Option 1. If it's a new deal under the same client: Option 2.

---

## 8. The `tavu_proposal` table — SOWs and proposals

This table captures the formal proposals and SOWs (Statements of Work) generated during the sales process. It is the piece that connects the opportunity to the document the client signs.

### 8.1 Base configuration

| Property | Value |
|---|---|
| Display name | `Proposal` |
| Plural | `Proposals` |
| Schema name | `tavu_proposal` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | User or team |
| Activities ✅ | Notes ✅ |

### 8.2 State + Status Reason

| State (statecode) | Status Reasons (statuscode) |
|---|---|
| **Active** (default) | Draft, AI Generated — Awaiting Review, Under Internal Review, Sent to Client, Awaiting Decision |
| **Inactive** | Approved by Client, Rejected by Client, Superseded, Withdrawn |

### 8.3 Custom columns

**Group A — Identification and link to opportunity:**

| Display Name | Schema | Type | Required | When populated |
|---|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | Required | On creation |
| Opportunity | tavu_opportunityid | Lookup → tavu_opportunity | Required | On creation (critical link) |
| Customer | tavu_customerid | Customer (polymorphic Account+Contact) | Read-only | Auto-inherited from opportunity |
| Account | tavu_accountid | Lookup → Account | Read-only | Auto-inherited from opportunity (B2B case) |
| Contact | tavu_contactid | Lookup → Contact | Read-only | Auto-inherited from opportunity (B2C case) |
| Discovery Notes | tavu_discoverynotes | Multiple Lines (50,000+ chars) | Optional | Auto-inherited from opportunity |
| Effective From | tavu_effectivefrom | Date Only | Optional | |
| Effective To | tavu_effectiveto | Date Only | Optional | |

**Inheritance logic on proposal creation:** when a `tavu_proposal` record is created linked to a `tavu_opportunity`, a Plugin/Flow automatically copies `tavu_opportunity.tavu_customer` → `tavu_proposal.tavu_customerid`, account, and contact fields. These fields are read-only in the seller's form. If the customer changes, it is changed on the opportunity, not on the proposal directly.

**Group B — Document data:**

| Display Name | Schema | Type | Required | When populated |
|---|---|---|---|---|
| Document Type | tavu_documenttype | Choice (SOW, Proposal, Contract, Estimate, Change Order) | Optional | On creation |
| Version | tavu_version | Single Line | Optional | On creation (e.g. v1.0, v1.1) |
| Sent Date | tavu_sentdate | Date Only | Optional | When sent to client |
| Expected Decision Date | tavu_expecteddecisiondate | Date Only | Optional | When sending |
| Proposal Content | tavu_content | Multiple Lines (50,000+ chars) | Optional | Narrative body (input/output of future AI Proposal Generator) |

**Group C — Quotation (fields added in v1.2):**

| Display Name | Schema | Type | Required | When populated |
|---|---|---|---|---|
| Price List | tavu_pricelistid | Lookup → tavu_pricelist | Optional | On creation — determines currency and base prices for lines |
| Subtotal | tavu_subtotal | Currency (calculated) | — | Auto: SUM of tavu_proposalline.tavu_extendedamount |
| Total Tax | tavu_totaltax | Currency (calculated) | — | Auto: SUM subtotal × taxrate (Rollup) |
| Total | tavu_total | Currency (calculated) | — | Auto: tavu_subtotal + tavu_totaltax (Plugin) |
| Total Cost | tavu_totalcost | Currency (calculated) | — | Auto: SUM of tavu_proposalline.tavu_linecost — visible only to Ops/Manager roles |
| Gross Margin (%) | tavu_grossmargin | Decimal (calculated) | — | Auto: ((subtotal − totalcost) / subtotal) × 100 — visible only to Ops/Manager |
| Show Kit Breakdown | tavu_showkitbreakdown | Yes/No | Optional | Default: No. If Yes, PDF expands kit components |

**Note on tavu_taxrate:** OpenTavu does not implement a tax engine. The seller manually enters the percentage based on the client's jurisdiction. For firms that require automatic calculation by US state, Avalara or TaxJar integration is added as an external connector in future phases.

**Note on tavu_totalcost and tavu_grossmargin:** visible only to Operations Manager and Sales Manager via Field Security Profile. The seller's form does not include them, so as not to condition negotiation with internal cost data.

### 8.4 Relationship with opportunity

The relationship is **N:1** — one opportunity can have multiple proposals (versions, change orders, related contracts).

**Example:**
```
Opportunity #501 "MegaCorp ERP Implementation"
    ↓
    ├─ Proposal v1.0 (Draft → Sent to Client → Rejected by Client)
    ├─ Proposal v1.1 (Draft → AI Generated → Sent to Client → Approved by Client) ✓
    ├─ Change Order #1 (after project start)
    └─ Change Order #2 (additional scope)
```

Each proposal carries its own independent statecode. The opportunity is closed Won when ONE of the proposals is Approved by Client (and signed).

### 8.5 Connection with the future AI Proposal Generator module

The `Proposal Content` field is dimensioned generously (50,000+ chars) because it is designed as the input/output of the AI Proposal Generator (roadmap module):
- **Input:** AI reads `Discovery Notes` from the opportunity, client context, previously won opportunities with similar clients
- **Output:** AI generates a draft of Proposal Content that the consultant reviews, edits, and sends
- **State machine:** Draft → AI Generated — Awaiting Review → Under Internal Review → Sent to Client

This module is NOT in the MVP, but the schema structure is already prepared.

---

## 8bis. Quotation model — Proposal lines, catalog, and kits

This section extends `tavu_proposal` with the complete quotation system: the line grid the seller sees, the product/service catalog, kit (bundle) management, price lists, and delivery roles. The guiding principle is **zero friction for the seller**: a single grid, no visible BOM complexity.

### 8bis.1 Quotation model overview

The seller interacts exclusively with two surfaces:
1. **`tavu_proposal`** — the quotation header, linked to the opportunity.
2. **`tavu_proposalline`** — the line grid where they add services, licenses, or kits. One line per item, regardless of whether it's simple or a composite bundle.

```
[Seller creates tavu_proposal linked to opportunity]
            ↓
[Adds tavu_proposallines — one grid, one item per line]
            ↓
[Selects tavu_product → can be simple service or kit]
            ↓
[System auto-fills price from tavu_pricelist]
            ↓
[Seller adjusts quantity, discount, role if applicable]
            ↓
[tavu_proposal calculates subtotal, tax, total, margin]
            ↓
[When generating PDF: if line is a kit, it explodes in memory]
[Client sees breakdown; Dataverse stores a single line]
```

### 8bis.2 Front-end entities (what the seller touches)

#### 8bis.2.1 The `tavu_proposalline` table — The seller's single grid

This is the most important table for the seller's experience. Each row represents an item in the proposal, whether a simple service, a license, or a complete kit.

**Base configuration:**

| Property | Value |
|---|---|
| Display name | `Proposal Line` |
| Plural | `Proposal Lines` |
| Schema name | `tavu_proposalline` |
| Primary column | `Name` (`tavu_name`, auto-generated) |
| Ownership | User or team |
| Audit ✅ | |

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Proposal | tavu_proposalid | Lookup → tavu_proposal | Required | Link to the header proposal |
| Product | tavu_productid | Lookup → tavu_product | Required | Service, license, or kit. JS detects if kit and shows visual badge |
| Unit of Measure | tavu_uomid | Lookup → tavu_uom | Required | Auto-filled from tavu_product.tavu_defaultuomid, editable |
| Quantity | tavu_quantity | Decimal | Required | E.g.: 40 (hours), 3 (months), 1 (complete kit) |
| Price Per Unit | tavu_priceperunit | Currency | Required | Auto from tavu_pricelist; manually editable |
| Unit Cost | tavu_unitcost | Currency | Optional | Auto from tavu_product.tavu_cost |
| Tax Rate (%) | tavu_taxrate | Decimal | Optional | Copied automatically from tavu_pricelistitem |
| Tax Amount | tavu_taxamount | Currency (Calculated) | Optional | tavu_subtotal * (tavu_taxrate/100) |
| Discount | tavu_discount | Currency | Optional | Discount amount in currency (not percentage) |
| Subtotal | tavu_subtotal | Currency (Calculated) | — | tavu_quantity * tavu_priceperunit |
| Total | tavu_total | Currency (calculated) | — | tavu_subtotal + tavu_taxamount − tavu_discount |
| Line Cost | tavu_linecost | Currency (calculated) | — | tavu_quantity × tavu_unitcost; visible to operations roles |
| Override Price | tavu_overrideprice | Yes/No | Optional | Allows overriding tavu_priceperunit for the product |

**JavaScript logic on tavu_productid OnChange:**

```javascript
// When a product is selected on the line:
if (product.tavu_iskit === true) {
    // Show visual "KIT" badge next to product name
    // Auto-fill UOM and Price Per Unit from active Price List
    // Show tooltip: "This item includes components — see breakdown in PDF"
} else {
    // Standard behavior: auto-fill UOM and price
    // If tavu_roleid is filled, use tavu_servicerole.tavu_defaultrate
    // as price suggestion (editable)
}
```

**Kit behavior in the proposal — architectural decision:**

The kit appears as **a single line** in `tavu_proposalline`. The explosion (component breakdown) happens **only when generating the PDF/Word**, ephemerally in memory. Component lines are never written back to Dataverse. Reasons:
- Changing the kit quantity requires editing one line, not N.
- Kit discount applies to the kit total, not distributed across lines.
- If the client rejects the kit, one row is deleted, not several.
- Audit trail is clean: the sales intent was the kit as a unit.

**When to use tavu_roleid vs tavu_productid with role name:**

For MVP, two strategies are supported based on firm maturity:

| Strategy | When to use | How |
|---|---|---|
| **Product per role** | Small firm (≤15 people), few profiles | Create separate products: "Senior Architect Hour", "Junior Consultant Hour". Without using tavu_roleid. |
| **Role on the line** | Mid-size firm with clear role structure | Single product "Consulting Hour" + tavu_roleid field on each line. Price comes from tavu_servicerole.tavu_defaultrate. |

The "Product per role" strategy is simpler to implement in MVP; "Role on the line" is more flexible and enables utilization reports by profile.

### 8bis.3 Back-end entities (catalog administered by Ops)

#### 8bis.3.1 The `tavu_uom` table — Units of measure

| Property | Value |
|---|---|
| Display name | `Unit of Measure` |
| Plural | `Units of Measure` |
| Schema name | `tavu_uom` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | Organization |
| Audit ✅ | |

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | Required | E.g.: "Hour", "Day", "Month", "License", "Unit" |
| Schedule | tavu_schedule | Choice | Optional | Grouping of interconvertible UOMs. E.g.: "Time" (Hour, Day); "Software" (License, Unit) |
| Conversion Factor | tavu_conversionfactor | Decimal | Optional | E.g.: 1 Day = 8 Hours. Base = 1 (for the root UOM of the schedule) |

**Initial seed data (pre-loaded in managed solution):**

| Name | Schedule | Conversion Factor |
|---|---|---|
| Hour | Time | 1 |
| Day | Time | 8 |
| Month | Time | 160 |
| License | Software | 1 |
| Unit | General | 1 |

#### 8bis.3.2 The `tavu_product` table — Master catalog

Everything the firm sells must exist here, whether an individual service, a license, or a kit (composite bundle).

| Property | Value |
|---|---|
| Display name | `Product` |
| Plural | `Products` |
| Schema name | `tavu_product` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | Organization |
| Audit ✅ | Quick create ❌ |

**State + Status Reason:**

| State | Status Reasons |
|---|---|
| **Active** (default) | Available |
| **Inactive** | Discontinued, Replaced |

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | Required | E.g.: "IT Consulting", "Power Apps License", "Cloud Migration Kit" |
| Default Unit Group | tavu_defaultunitgroup | Lookup → tavu_unitofmeasureschedule | Required | Logical category the product belongs to |
| Default Unit | tavu_defaultunit | Lookup → tavu_uom | Required | Unit used to sell the product |
| Cost | tavu_cost | Currency | Optional | Internal unit cost. Base for margin calculation in proposal |
| Is Kit | tavu_iskit | Yes/No | Required | Default: No. If Yes, product is composed of items in tavu_kitcomponent |
| Description | tavu_description | Multiple Lines | Optional | Commercial description visible in proposals |
| AI Categorization Hint | tavu_aihint | Multiple Lines | Optional | Text to help the AI Proposal Generator understand when to include this product |

**Critical business restriction:** a product with `tavu_iskit = Yes` CANNOT be a `tavu_childproductid` in any `tavu_kitcomponent` record. MVP limits kits to one level of depth (kit contains individual products; nested kits are not supported). A plugin or Business Rule must block this with a clear error message if attempted.

**Initial seed data:**

| Name | Default UOM | Is Kit | Standard Cost |
|---|---|---|---|
| Consulting Hour | Hour | No | $60 USD |
| Senior Architect Hour | Hour | No | $90 USD |
| Junior Consultant Hour | Hour | No | $45 USD |
| Power Apps Premium License | License | No | $20 USD |
| Cloud Migration Kit | Unit | **Yes** | (calculated from components) |

#### 8bis.3.3 The `tavu_kitcomponent` table — The kit recipe

This table defines the internal composition of each kit. It is the BOM (Bill of Materials) of the system. The seller never sees it directly; the administrator configures it once and the system consults it when generating documents.

**Why an intermediate table (not a Parent field on tavu_product):** a `tavu_parentproductid` field on `tavu_product` would create a single-level flat tree that collapses as soon as the same child product appears in multiple kits with different quantities. The intermediate table supports many-to-many relationships with their own attributes (quantity, UOM per component) — the real case in consulting (40 consulting hours in one kit, 80 in another).

| Property | Value |
|---|---|
| Display name | `Kit Component` |
| Plural | `Kit Components` |
| Schema name | `tavu_kitcomponent` |
| Primary column | `Name` (`tavu_name`, auto-generated) |
| Ownership | Organization |
| Audit ✅ | |

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Parent Product | tavu_parentproductid | Lookup → tavu_product | Required | The kit. Must have tavu_iskit = Yes |
| Child Product | tavu_childproductid | Lookup → tavu_product | Required | The component. Must have tavu_iskit = No (business restriction) |
| Quantity | tavu_quantity | Decimal | Required | How many units of the component are in the kit |
| Unit of Measure | tavu_uomid | Lookup → tavu_uom | Required | UOM of the component within the kit |

**Example configuration of "Cloud Migration Kit":**

| Parent Product | Child Product | Quantity | UOM |
|---|---|---|---|
| Cloud Migration Kit | Consulting Hour | 40 | Hour |
| Cloud Migration Kit | Power Apps Premium License | 2 | License |

When the PDF is generated with `tavu_showkitbreakdown = Yes`, the flow explodes this table in memory and renders the breakdown in the document. When `No`, the PDF shows only "Cloud Migration Kit — 1 Unit — $X".

#### 8bis.3.4 The `tavu_pricelist` and `tavu_pricelistitem` tables — Price lists

Allow having different rates by market, client type, or currency: "Standard USD Rate", "Partner Rate", "Colombia COP Rate", etc.

**`tavu_pricelist` custom columns:**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | Required | E.g.: "Standard USD Rate 2026" |
| Currency | tavu_currency | Choice or Single Line | Required | Currency for this list (USD, COP, EUR…) |
| Effective Date | tavu_effectivedate | Date Only | Optional | When it becomes effective |
| Expiration Date | tavu_expirationdate | Date Only | Optional | When it expires |
| Is Default | tavu_isdefault | Yes/No | Optional | The list pre-selected when creating a proposal |

**`tavu_pricelistitem` custom columns:**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Price List | tavu_pricelistid | Lookup → tavu_pricelist | Required | |
| Product | tavu_productid | Lookup → tavu_product | Required | |
| Price Per Unit | tavu_priceperunit | Currency | Required | Sale price for this product in this list |
| Quantity | tavu_quantity | Decimal | Optional | For volume-tiered pricing |

**Auto-fill logic for price in tavu_proposalline:**

```
When tavu_productid is selected on a proposal line:
1. Read tavu_pricelistid from the header proposal
2. Look up in tavu_pricelistitem: tavu_pricelistid = [current list] AND tavu_productid = [selected product]
3. If found → fill tavu_priceperunit with tavu_pricelistitem.tavu_amount
4. If not found → leave tavu_priceperunit empty for seller to enter manually
5. If tavu_roleid is filled → suggest tavu_servicerole.tavu_defaultrate as optional override
```

#### 8bis.3.5 The `tavu_servicerole` table — Delivery roles

Allows differentiating the price and internal cost of a service based on the profile of the person delivering it, without needing a separate product for each work type × profile combination.

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | Required | E.g.: "Senior Architect", "Junior Consultant", "PM" |
| Default Rate | tavu_defaultrate | Currency | Required | Standard sale price per hour for this role |
| Cost Rate | tavu_costrate | Currency | Required | Internal cost per hour for this role (for real margin) |
| Description | tavu_description | Multiple Lines | Optional | Profile description and responsibilities |

**Initial seed data:**

| Name | Default Rate | Cost Rate |
|---|---|---|
| Senior Architect | $200 USD/hr | $90 USD/hr |
| Senior Consultant | $150 USD/hr | $70 USD/hr |
| Junior Consultant | $100 USD/hr | $45 USD/hr |
| Project Manager | $130 USD/hr | $60 USD/hr |

### 8bis.4 Seller workflow when quoting

**Step 1 — Seller opens the opportunity and creates a new proposal:**

```
tavu_proposal:
- Name: "RetailCorp Migration v1.0"
- Opportunity: [link to tavu_opportunity]
- Price List: "Standard USD Rate 2026" (auto-selected by tavu_isdefault)
- Show Kit Breakdown: No (default)
- statuscode: Draft
```

**Step 2 — Seller adds lines to the grid:**

| Product | Role | Qty | UOM | Price/Unit | Discount | Extended |
|---|---|---|---|---|---|---|
| Cloud Migration Kit | — | 1 | Unit | $8,500 | $0 | $8,500 |
| Consulting Hour | Senior Architect | 10 | Hour | $200 | $0 | $2,000 |
| User Training | — | 2 | Day | $1,200 | $200 | $2,200 |

The "KIT" field appears with a visual badge in the grid to distinguish it from simple services. The seller only interacts with this grid — they never see the tavu_kitcomponent table.

**Step 3 — Proposal calculates automatically:**

```
Subtotal:     $12,700
Tax Rate:     8.5%
Tax Amount:   $1,079.50
Total:        $13,779.50

[Visible only to Ops/Manager:]
Total Cost:   $5,850
Gross Margin: 53.9%
```

**Step 4 — Seller generates PDF:**
- If `tavu_showkitbreakdown = No` → PDF shows the 3 lines as they appear in the grid.
- If `tavu_showkitbreakdown = Yes` → PDF expands the Cloud Migration Kit into its components (40 consulting hours + 2 licenses) for greater transparency with the client. The proposal in Dataverse still has a single line.

**The kit explosion happens in Power Automate (or in the document generation plugin), never in Dataverse.** The flow reads `tavu_kitcomponent` for the kit line, calculates quantities (line.tavu_quantity × component.tavu_quantity), and writes the result to the Word template. It does not create additional proposal records.

### 8bis.5 Pre-loaded seed data (managed solution)

To reduce setup time for a 15-person firm from hours to minutes, the managed solution includes the following pre-loaded data that the implementation consultant only needs to adjust:

| Table | Seed records included |
|---|---|
| tavu_uom | 5 UOMs: Hour, Day, Month, License, Unit |
| tavu_product | 4 individual services + 1 fully configured example kit |
| tavu_kitcomponent | Components of the example kit (editable list) |
| tavu_pricelist | 1 "Standard USD Rate" list marked as default |
| tavu_pricelistitem | Prices of the 4 services in the standard list |
| tavu_servicerole | 4 roles: Senior Architect, Senior Consultant, Junior Consultant, PM |

### 8bis.6 MVP restrictions and design decisions

| Decision | Detail |
|---|---|
| Single-level kits | A kit can contain individual products. A kit CANNOT contain another kit. Restriction enforced by Business Rule/Plugin. If nested kits are needed in the future, the schema already supports it, but the explosion code requires deliberate refactoring. |
| Manual tax | No tax engine implemented. tavu_taxrate is a Decimal field the seller fills manually. For firms needing automatic tax by US state, Avalara/TaxJar integration is added as an external connector in future phases. |
| Discount by amount, not percentage | tavu_discount is Currency (fixed amount) for simplicity. If percentage discounts are needed, the field is adapted or tavu_discountpct is added with conversion logic. |
| Margin hidden from sellers | tavu_totalcost and tavu_grossmargin are visible only to Operations and Management roles via Field Security Profile. Seller form does not include them. |
| Audit depth | Every change to tavu_proposalline and tavu_product is recorded in Dataverse's Audit log. Proposal versions (v1.0, v1.1) are managed by creating new tavu_proposal records, not overwriting the previous one. |

---

## 9. End-to-end examples

### Example 1 — Path A: Outbound from networking

**Monday — Microsoft Power Platform Conference:**
María (consultant) meets Carlos Méndez (CTO of RetailCorp) at a conference. They briefly discuss Carlos's problem: they need to migrate from Salesforce to Dynamics 365.

**Monday 11pm — María updates the CRM:**

```
Account: RetailCorp
- Customer Tier: Premium
- Industry: Retail
- Annual Revenue: $50M

Contact: Carlos Méndez
- Email: carlos@retailcorp.com
- Job Title: CTO
- Account: RetailCorp
- Engagement Status: Engaged
```

**Notable:** NO `tavu_lead` was created. María went straight to Contact + Account.

**Wednesday — Discovery call:** Carlos confirms 8,000 records to migrate, budget $30K-$50K, Salesforce license expiring in 3 months.

```
tavu_opportunity:
- Topic: "RetailCorp Salesforce → Dynamics 365 Migration"
- Customer: RetailCorp (Account)
- Primary Contact: Carlos Méndez
- Estimated Value: $40,000
- Estimated Close Date: Aug 8, 2026
- Engagement Type: One-time Project
- statuscode: Discovery
```

**Week 5 — Proposal created, sent, negotiated (v1.0 → v1.1).**

**Week 7 — Close Won:**

Pop-up:
- Actual Revenue: $47,000 (includes training add-on)
- Close Notes: "Client signed after adding training module. Project start July 7."

System:
- Creates `tavu_opportunityclose` (Won)
- Mirrors: actualrevenue, actualclosedate, closenotes → tavu_opportunity
- `account.tavu_iscustomer = Yes`
- Proposal v1.1 → Approved by Client

**Result:** deal won in 6 weeks, full audit trail, $47K vs $40K estimated (+17%).

---

### Example 2 — Path B: Anonymous inbound processed by AI

Email arrives at `info@company.com`:
> "Hi, I'm Juan López from GreenTech Solutions, 25 people, evaluating CRM options..."

**System creates tavu_lead** → Module 3 AI processes (Confidence: 0.72 — below threshold) → status: Awaiting Human Review → María promotes → Contact + Account created → opportunity follows Path A.

**Key:** the resulting opportunity has `tavu_sourcelead = [link to original lead]` for full traceability of the inbound source.

---

### Example 3 — Close Lost with captured reason

RetailCorp chooses HubSpot. María clicks "Close Lost":
- Lost Reason: Competitor
- Close Notes: "Client chose HubSpot for price and native ERP integration. Lesson: validate integrations before proposing Dynamics 365 to accounts with specific ERP stack."

System creates `tavu_opportunityclose` (Lost), mirrors lostreason and closenotes to opportunity. Proposal → Rejected by Client.

**Power BI reports:** Lost Reasons by Quarter, Lost vs Won by Engagement Type — all derived from clean structured data.

---

### Example 4 — B2C case: Law firm serving an individual

`tavu_customermode = Mixed`

Carolina López contacts the firm for her divorce. María creates her directly as a Contact (no Account — she's an individual).

```
tavu_opportunity:
- Topic: "Carolina López — Divorce Proceedings"
- Customer: Carolina López (Contact)
- Account (auto): (empty)
- Contact (auto): Carolina López
- Primary Contact: Carolina López (auto — same person)
- Engagement Type: One-time Project
```

When closed Won: `contact.tavu_iscustomer = Yes` on Carolina (no Account to mark).

---

## 10. Queues — Native Dataverse routing

OpenTavu uses native queues rather than building custom routing logic.

A queue is a "shared inbox" where cases wait for assignment. Any consultant with permission can:
- View cases in the queue
- "Pick" a case → assign themselves as Worked By
- "Release" a case they took but cannot continue
- Re-assign to another queue or consultant

**Typical queue examples:**
- Tier 1 Support Queue (all Support Requests)
- Sales Queue (all RFP/Proposal Inquiries)
- Finance Queue (all Billing Inquiries)
- Strategic Customer Queue (all cases from Strategic clients)
- On-Call Queue (all Critical priority cases)

---

## 11. Recommended configuration by firm type

### Small IT consultancy (12 people)

- Path A only (Path B very rare — almost everything through networking/referrals)
- 1–2 Customer Tiers (Standard + Strategic, no Premium in between)
- Main Engagement Type: One-time Project + Retainer
- Max 2–3 proposal versions per typical opportunity

**Result:** very simple, agile model with no administrative overhead.

### Mid-size B2B agency (25 people)

- Both Path A and Path B active (web form receives inbound regularly)
- 3 Customer Tiers
- Varied Engagement Type (Project, Retainer, T&M)
- Pipeline of 8–12 concurrent active opportunities

**Result:** balanced model between structure and flexibility.

### Software QA boutique (40 people)

- Both paths very active (high inbound volume from forms and referrals)
- 3–4 Customer Tiers
- Engagement Type includes Sprint-based (T&M with fixed duration)
- Pipeline with 20+ active opportunities
- Multiple Change Orders per opportunity

**Result:** robust model with high automation.

### Boutique law firm (8 people, B2B + B2C hybrid)

- `tavu_customermode = Mixed`
- Customer Tiers: Standard, Premium (for corporates with recurring services)
- Main Engagement Type: One-time Project + Retainer
- Some clients are Accounts (corporate legal advisory); many are direct Contacts (divorces, estates, criminal defense)

**Result:** model works for both client types without artificial distinction. Reporting separates naturally by `tavu_account != null` vs `tavu_contact != null`.

---

## 12. Frequently asked questions

**Why isn't `tavu_lead` where the commercial lifecycle lives?**

Because B2B relationships in Professional Services typically do NOT start as anonymous leads. They start as direct contacts (networking, referrals, events). Forcing the "Lead → Qualify → Convert" model introduces friction that causes CRM abandonment (Pain #1). The Lead only exists as a technical buffer for anonymous inbound.

**Can I disable Path B (tavu_lead) if I never have anonymous inbound?**

Yes. The `tavu_lead` table can exist empty without affecting anything. If your firm works 100% through networking, simply don't configure the web form or generic email processing. The system will work perfectly with only Path A active.

**Why is "Discovery Notes" a Multiple Lines field and not something more structured?**

Because discovery conversations in Professional Services are inherently narrative, not structured. Trying to impose premature structure (fixed fields for Budget, Authority, Need, Timeline) reduces the quality of captured information. Multiple Lines lets the consultant capture full context, and the future AI Proposal Generator will extract the structured elements it needs from the free text.

**What happens if a Won opportunity is later cancelled (client changes their mind)?**

Reopen the opportunity manually (statecode: Open). Create a new `tavu_opportunityclose` documenting the scenario. The opportunity stays Open until a decision is made to close Lost or re-close Won with different conditions. The audit trail shows both events: original close and reopen.

**How is an upsell on an existing client captured?**

Create a new `tavu_opportunity` with the same Account/Contact. The old (Won) opportunity stays closed. The new one represents the upsell. The "Customer Lifetime Value" report sums all Won opportunities per client.

**Why is `tavu_proposal` separate from `tavu_opportunity`?**

Because one opportunity can have multiple proposals (versions, change orders, related contracts). Mixing document data with deal data in one table would create massive duplication (10 proposal versions = 10 opportunity records). Separation allows ONE opportunity with a COMPLETE history of proposals.

**Can I migrate opportunities from existing Salesforce/Dynamics to the OpenTavu model?**

Yes. `tavu_opportunity` maps conceptually well with Salesforce/Dynamics Opportunity. Discovery-driven stages map to Sales Process Stages. Lost Reasons map to the Global Choice. Existing proposals can be migrated as `tavu_proposal` records linked to the corresponding opportunity.

**What about a Contact who is an interlocutor but NOT the legal client?**

Carlos Méndez (CTO of RetailCorp) is an interlocutor but NOT the client. The legal client is RetailCorp (Account):
- `account.tavu_iscustomer = Yes` (RetailCorp is the client)
- `contact.tavu_iscustomer = No` (Carlos is NOT individually marked)
- `contact.tavu_engagementstatus` can be Engaged (active communication with him)

**How does Customer Mode work if my firm changes from B2B-only to Mixed?**

Change `tavu_systemsettings.tavu_customermode = Mixed`. The form's JavaScript detects the change on the next OnLoad and allows selecting Contacts in the lookup. Existing opportunities and cases (pointing to Accounts) are NOT affected. New records can point to Contacts if the firm decides to serve individuals. Transparent migration with no downtime.

**Why not explode the kit into multiple tavu_proposallines when selecting it?**

Because it destroys the seller's experience. If the kit explodes into N lines in Dataverse: (a) changing the kit quantity requires editing N lines instead of one, (b) kit discount becomes a math problem distributed across lines, (c) removing the kit from scope requires identifying and deleting N rows. Ephemeral explosion at PDF generation is the correct pattern: one line in the CRM, breakdown only in the document.

**Does the model support proposals in multiple currencies?**

Yes, with one restriction: a proposal uses the currency of the assigned price list (`tavu_pricelist.tavu_currency`). If the firm has clients in COP and USD, create two price lists with their respective amounts. One proposal is one currency. Multi-currency mixing within the same proposal is not supported in MVP.

---

## Document control

| Version | Date | Author | Notes |
|---|---|---|---|
| 1.0 | May 6, 2026 | Gustavo González Villani | Initial operational guide. Covers tavu_lead, account, contact, tavu_opportunity, tavu_opportunityclose, tavu_proposal. |
| 1.1 | May 8, 2026 | Gustavo González Villani | Architectural correction: removed tavu_lifecyclestage from Contact; added tavu_iscustomer, tavu_customersince, tavu_lastengagementdate to Account; added tavu_engagementstatus, tavu_customertier to Contact; adopted hybrid Customer field architecture; added tavu_systemsettings; expanded to B2B + B2C hybrid; added B2C law firm example. |
| 1.2 | May 8, 2026 | Gustavo González Villani | Added complete quotation model (Section 8bis): tavu_proposalline, tavu_product, tavu_uom, tavu_kitcomponent, tavu_pricelist + tavu_pricelistitem, tavu_servicerole. Documented MVP design decisions: single-level kits, ephemeral PDF explosion, manual tax, hidden margin via Field Security Profile, pre-loaded seed data. |

*This document is the operational reference for OpenTavu's sales model.*
