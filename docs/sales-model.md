# Sales Model Operational Guide — OpenTavu

## How the Single Lifecycle, Dual Entry model works for Professional Services SMBs

**Audience:** Power Platform consultants, MVPs, integrators adopting OpenTavu, future contributors, and any AI system that needs context about the model.

**Purpose:** explain what each sales model table does, what each field does, and when each field is populated. The guide is organized around real operational flows, not tables in isolation — because no field exists in a vacuum; every field is part of a business process.

**Last updated:** July 10, 2026

---

## 1\. Commercial model overview

Unlike traditional B2C CRMs — where everything enters as a "Lead" and must be qualified before becoming a "Contact" — OpenTavu is designed for **Professional Services SMBs**, explicitly supporting three client models:

- **B2B only** — the firm sells exclusively to companies (IT consultancies, B2B agencies, software/QA boutiques)  
- **B2C / individuals** — the firm sells to natural persons (some law firms, independent coaches)  
- **Hybrid** — the firm serves both simultaneously (boutique law firms with corporate and individual clients, accountants with business and personal clients, wealth managers)

OpenTavu adopts a hybrid architecture that covers all three models without forcing artificial adaptations in any case.

In B2B consulting, relationships typically begin with a direct contact, a referral, or a meeting at an event. Forcing the consultant to create a "Lead" and then "convert" it introduces administrative friction and CRM abandonment (Pain \#1 — the most documented pain point in the evidence triangulation).

OpenTavu resolves this with the **Single Lifecycle, Dual Entry** model, using twelve interrelated tables.

**Terminology note:** "Single Lifecycle" refers to the lifecycle of **opportunities** (where the commercial cycle truly lives), NOT to a Stage field on Contact. Early versions of the model proposed a Lifecycle Stage field on Contact; this was corrected when it became clear that in Professional Services the commercial subject is the Account (B2B) or the Contact (B2C), and that its state is better derived from relationships with opportunities and cases than from a label field. See Section 5 for detail.

### Sales model tables

| Table | Role | Who edits |
| :---- | :---- | :---- |
| `account` (Dataverse standard) | Corporate client accounts; each has an assigned Customer Tier | Sales / Ops during client onboarding |
| `contact` (Dataverse standard) | People; carries engagement status and customer flags | Sales daily |
| `tavu_lead` | Ingestion buffer for low-quality anonymous inbound | System (AI) first, then sales |
| `tavu_salesstage` | Configurable pipeline stages with default probability and forecast category | Admin / Operations Manager during setup |
| `tavu_opportunity` | Discovery-driven commercial pipeline | Sales / Sales Manager |
| `tavu_opportunityclose` (Activity) | Historical log of every close attempt (Won/Lost/Reopen) | Sales via guided pop-up |
| `tavu_proposal` | SOWs and proposals linked to opportunities | Sales \+ future AI Proposal Generator |
| `tavu_proposalline` | Quotation lines — the single grid the seller sees | Sales when building a proposal |
| `tavu_product` | Master catalog of services, licenses, and kits | Admin / Operations Manager |
| `tavu_uom` | Units of measure (Hour, Month, License…) | Admin during setup |
| `tavu_kitcomponent` | BOM: internal composition of kits (hidden recipe) | Admin / Operations Manager |
| `tavu_pricelist` \+ `tavu_pricelistitem` | Price lists by currency and rate | Admin / Operations Manager |
| `tavu_servicerole` | Delivery roles with rate and cost per profile | Admin / Operations Manager |

### Single Lifecycle, Dual Entry flow

\[Path A: Outbound / Networking\] ──→ Contact created directly (Engagement Status: Engaged)

                                        ↓

                                tavu\\\_opportunity created

                                        ↓

\[Path B: Anonymous Inbound\] ──────→ tavu\_lead created (Buffer)

                                        ↓

                                AI Hygiene evaluates and promotes to Contact

                                        ↓

                                tavu\\\_opportunity created

                                        ↓

\[Opportunity Management\] ─────────→ Advances through configurable tavu\_salesstage records (default seed: Discovery → Proposal Drafted → Proposal Sent → Negotiation)

                                        ↓

\[Close\] ───────────────────────────→ "Close" button opens guided pop-up

                                        ↓

\[Transactional Orchestration\] ────→ Creates tavu\_opportunityclose Activity (historical log)

                                   \\+ Updates mirror fields in tavu\\\_opportunity

                                        ↓

\[If signed\] ───────────────────────→ tavu\_proposal created (SOW linked to opportunity)

---

## 2\. Path A — Outbound / Networking (the common case in Professional Services)

This is the natural flow: the consultant identifies a real opportunity through their network, an event, a referral, or an existing relationship.

### 2.1 Direct Contact creation

**When it happens:** consultant meets someone at an event, receives a referral from an existing client, identifies a prospect on LinkedIn who knows their work.

**Action:** consultant creates the `contact` directly, linked to the `account` (existing or new).

**Fields populated on contact:**

| Field | Schema | Who fills it | Example |
| :---- | :---- | :---- | :---- |
| First Name \+ Last Name | OOTB standard | Consultant | "Carlos Méndez" |
| Email | OOTB standard | Consultant | "[carlos@megacorp.com](mailto:carlos@megacorp.com)" |
| Phone | OOTB standard | Consultant | "+1 555-0142" |
| Job Title | OOTB standard | Consultant | "CTO" |
| Account | parentcustomerid (OOTB) | Consultant | Lookup → MegaCorp |
| **Engagement Status** | **tavu\_engagementstatus** | **Consultant** | **Engaged** |

**State at this point:** contact exists in the system. NO opportunity yet.

### 2.2 When the opportunity is created

**When it happens:** the conversation with Carlos progresses. There is concrete interest in a project. There is a tentative budget. There is a defined timeline.

**Action:** consultant creates `tavu_opportunity` from the contact.

**Key point about Path A:** **`tavu_lead` is NEVER used in this flow.** The consultant works with a full Account \+ Contact from the first minute.

---

## 3\. Path B — Anonymous Inbound (when `tavu_lead` IS used)

This flow applies when an external signal arrives that cannot be cleanly attributed to an existing Account/Contact.

### 3.1 Typical inbound cases that generate a tavu\_lead

- Website form submission ("I'd like more information")  
- Generic email received at `info@company.com` or `sales@`  
- LinkedIn message from someone NOT in the CRM  
- Lead sent by a partner with incomplete data

### 3.2 The `tavu_lead` table — Configuration

**Base configuration:**

| Property | Value |
| :---- | :---- |
| Display name | `Lead` |
| Plural | `Leads` |
| Schema name | `tavu_lead` |
| Primary column | `Subject` (`tavu_subject`) |
| Ownership | User or team |
| Activities ✅ | Notes ✅ |

**State \+ Status Reason:**

| State (statecode) | Status Reasons (statuscode) |
| :---- | :---- |
| **Active** (default) | New, AI Processing, Awaiting Human Review, Manual Review Required |
| **Inactive** | Promoted to Contact, Discarded as Noise, Duplicate, Not Qualified, Stale |

**Custom columns:**

| Display Name | Schema | Type | When populated |
| :---- | :---- | :---- | :---- |
| Subject | tavu\_subject | Single Line (Primary) | On creation (auto-extracted from email subject) |
| Source | tavu\_source | Choice | On creation (Web Form, Email, LinkedIn, Partner Referral, Other) |
| Source Details | tavu\_sourcedetails | Multiple Lines | On creation (raw text of received message) |
| Email | tavu\_email | Single Line (Email) | On creation (extracted from sender) |
| Phone | tavu\_phone | Single Line (Phone) | On creation (if in message) |
| First Name | tavu\_firstname | Single Line | On creation (extracted or manual) |
| Last Name | tavu\_lastname | Single Line | On creation (extracted or manual) |
| Company Name (raw) | tavu\_companyname | Single Line | On creation (extracted, unvalidated) |
| Matched Account | tavu\_matchedaccount | Lookup → Account | AI fills when it finds a match |
| Matched Contact | tavu\_matchedcontact | Lookup → Contact | AI fills when it finds a match |
| AI Confidence Score | tavu\_aiconfidencescore | Decimal (0-1) | AI fills after processing |
| AI Recommendation | tavu\_airecommendation | Multiple Lines | AI fills: promote / discard / review |
| Promoted Contact | tavu\_promotedcontact | Lookup → Contact | Filled when promoted |
| Days in Buffer | tavu\_daysinbuffer | Whole Number | Calculated daily by scheduled flow |
| Buffer Alert | tavu\_bufferalert | Choice | Auto-populated by scheduled flow |
| Last AI Processing Date | tavu\_lastaiprocessingdate | DateTime | AI fills after each processing |

**Choice values for `tavu_bufferalert`:**

| Value | Label | Color |
| :---- | :---- | :---- |
| 1 | Fresh (0-7 days) | \#107C10 (green) |
| 2 | Aging (8-14 days) | \#CA5010 (amber) |
| 3 | Stale (15+ days) | \#D13438 (red) |

Auto-populated by the same daily scheduled Power Automate flow that calculates `tavu_daysinbuffer`. The flow sets the alert level based on the current buffer days. Choice colors render as colored pills in the MDA view grid natively — no custom code required.

### 3.3 Lead processing flow (Path B)

**Step 1 — Auto-creation on inbound signal:** Power Automate detects new email to `info@` or web form submission → creates `tavu_lead` with status `New`.

**Step 2 — Module 3 AI processes (target \< 2 minutes):**

- Does it match an existing Contact? (email lookup) → if YES, link and notify  
- Does it match an existing Account? (company name lookup) → if YES, link  
- Is it spam/noise? → if YES, status \= `Inactive / Discarded as Noise`  
- Is it a duplicate? → if YES, status \= `Inactive / Duplicate`  
- Fill AI Confidence Score, AI Recommendation  
- If confidence ≥ 0.85 → auto-promote to Contact \+ Account, status \= `Inactive / Promoted to Contact`  
- If 0.50 ≤ confidence \< 0.85 → status \= `Awaiting Human Review`, notify sales  
- If confidence \< 0.50 → status \= `Inactive / Discarded as Noise`

**Step 3 — Human review (if needed):** Sales rep reads AI Recommendation and decides:

- Promote → creates Contact \+ Account, lead \= `Inactive / Promoted to Contact`  
- Discard → lead \= `Inactive / Discarded as Noise`

**Step 4 — Auto-cleanup of stale leads:** Scheduled Power Automate flow (runs daily):

- Query: leads where `statecode = Active` AND `statuscode = Awaiting Human Review` AND `tavu_daysinbuffer > 14`  
- Action: change to `Inactive / Stale`  
- Optional notification to owner

The same scheduled flow also updates `tavu_bufferalert` on all Active leads:

- tavu\_daysinbuffer ≤ 7 → Fresh  
- tavu\_daysinbuffer 8–14 → Aging  
- tavu\_daysinbuffer ≥ 15 → Stale

This prevents abandoned leads from polluting views indefinitely.

### 3.4 Why this design

- **Respects how consultants work:** Path A is the common case, Path B is the exception  
- **Preserves the buffer's function:** low-quality anonymous inbound does NOT pollute the master Contact database  
- **Gives Module 3 a clear role:** orchestrates automatic promotion with confidence threshold  
- **Captures audit trail:** each processed lead leaves a record of what AI decided and why

---

## 4\. The `account` table — Customer Tier for SLA and prioritization

OpenTavu uses Dataverse's standard `account` table (mixed architecture decision — see VISION.md Section 6).

### 4.1 Custom columns added to the standard table

| Display Name | Schema Name | Type | Required | Default |
| :---- | :---- | :---- | :---- | :---- |
| Customer Tier | tavu\_customertier | Lookup → tavu\_customertierdefinition | Optional | Standard |
| Is Customer | tavu\_iscustomer | Yes/No | Optional | No |
| Customer Since | tavu\_customersince | Date Only | Optional | (empty) |
| Last Engagement Date | tavu\_lastengagementdate | DateTime | Optional | (empty) |
| Country | tavu_country | Lookup → tavu_country | Required in MVP | |
| State/Province | tavu_stateprovince | Lookup → tavu_stateprovince | Optional | Filtered by Country |
| City | tavu_city | Lookup → tavu_city | Optional | Filtered by State/Province |

**When populated:** when a new account is created. If not specified, defaults to Standard.

**Automatic logic for Is Customer (Plugin/Flow on opportunity Won close):**

IF tavu\_opportunity changes to state \= Won

AND opp\_customer points to Account:

→ account.tavu\\\_iscustomer \\= Yes (if it was No)

→ account.tavu\\\_customersince \\= today (if null, NOT overwritten)

IF opp\_customer points to Contact:

→ logic applies to Contact, not Account (see Section 5\\)

`tavu_iscustomer` is NEVER automatically changed to No. That is a human decision when the relationship formally ends. `tavu_lastengagementdate` is updated by Module 3 (Activity Capture) when engagement is detected.

**Importance for sales:** the tier influences:

- Opportunity priority (Strategic accounts typically receive more attention)  
- SLA for cases associated with the client (documented in the service model guide)  
- Pipeline reports by tier  
- Forecast accuracy (Strategic accounts have more historical data)

---

## 5\. The `contact` table — People in the system: client or interlocutor?

`contact` is the master table for people. In OpenTavu, a person can have two possible roles:

- **Interlocutor of an Account** (typical B2B case): Carlos Méndez is CTO of RetailCorp — he is not a client himself, but he is the person we talk to  
- **Direct client** (B2C / individual case): Carolina López contracts directly with the law firm for her divorce; SHE is the client, there is no associated Account

This duality matters because the model must NOT force "converting" Carolina into a fictitious Account, nor assume that Carlos as a Contact is "a client" when the actual client is RetailCorp.

### 5.1 Custom columns added to the standard `contact` table

| Display Name | Schema Name | Type | Required | Default |
| :---- | :---- | :---- | :---- | :---- |
| Is Customer | tavu\_iscustomer | Yes/No | Optional | No |
| Customer Since | tavu\_customersince | Date Only | Optional | (empty) |
| Engagement Status | tavu\_engagementstatus | Choice | Optional | Cold |
| Last Engagement Date | tavu\_lastengagementdate | DateTime | Optional | (empty) |
| Customer Tier | tavu\_customertier | Lookup → tavu\_customertierdefinition | Optional | Standard |
| Country | tavu_country | Lookup → tavu_country | Required in MVP | |
| State/Province | tavu_stateprovince | Lookup → tavu_stateprovince | Optional | Filtered by Country |
| City | tavu_city | Lookup → tavu_city | Optional | Filtered by State/Province |

**Choice values for `tavu_engagementstatus`:**

| Value | Meaning |
| :---- | :---- |
| **Cold** (default) | No recent engagement |
| **Engaged** | Recent activity (emails, meetings) |
| **Inactive** | Was Engaged but no activity in 90+ days (configurable) |

### 5.2 `tavu_iscustomer` logic on Contact

IF tavu\_opportunity changes to state \= Won

AND opp\_customer points to Contact (B2C case):

→ contact.tavu\\\_iscustomer \\= Yes (if it was No)

→ contact.tavu\\\_customersince \\= today (if null)

IF opp\_customer points to Account (B2B case):

→ contact.tavu\\\_iscustomer is NOT modified

→ the client flag goes to the Account

**Important:** a Contact acting as an Account's interlocutor is NOT marked as `tavu_iscustomer = Yes`. The client flag reflects the actual commercial subject, not a communication role.

### 5.3 `tavu_engagementstatus` logic

- **Cold:** initial value. Person in the database with no recent activity.  
- **Engaged:** set manually by consultant or automatically by Module 3 (Activity Capture) when it detects recent emails/meetings.  
- **Inactive:** scheduled Power Automate flow moves to Inactive if no activity in X days (default 90, configurable in `tavu_systemsettings` via `tavu_activitythresholddays`).

**Useful combinations for reporting:**

| Is Customer | Engagement Status | Meaning |
| :---- | :---- | :---- |
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

### 5.7 Geographic catalog tables — structured address data

OpenTavu replaces Dataverse's free-text address fields with a three-level lookup hierarchy: Country → State/Province → City. This eliminates duplicate city names, inconsistent abbreviations, and data quality issues caused by manual text entry.

#### 5.7.1 The `tavu_country` table

| Property | Value |
|---|---|
| Display name | `Country` |
| Plural | `Countries` |
| Schema name | `tavu_country` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | Organization |
| Audit ✅ | |

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | Required | "United States", "Colombia" |
| ISO Alpha-2 | tavu_isocode2 | Single Line (2) | Required | US, CO |
| ISO Alpha-3 | tavu_isocode3 | Single Line (3) | Optional | USA, COL |
| Numeric Code | tavu_numericcode | Whole Number | Optional | 840, 170 |
| Currency | tavu_currency | Lookup → Currency | Optional | Lookup to Dataverse transactioncurrency table |
| Phone Prefix | tavu_phoneprefix | Single Line (5) | Optional | +1, +57 |
| Sort Order | tavu_sortorder | Whole Number | Optional | For displaying the firm's primary countries first |

**Initial seed data:** United States (US) and Colombia (CO). Additional countries added by the implementation consultant as needed.

#### 5.7.2 The `tavu_stateprovince` table

| Property | Value |
|---|---|
| Display name | `State/Province` |
| Plural | `States/Provinces` |
| Schema name | `tavu_stateprovince` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | Organization |
| Audit ✅ | |

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | Required | "California", "Valle del Cauca" |
| Country | tavu_country | Lookup → tavu_country | Required | Parent reference |
| ISO Code | tavu_isocode | Single Line (6) | Optional | ISO 3166-2: US-CA, CO-VAC |
| Short Code | tavu_shortcode | Single Line (5) | Optional | CA, VAC — for compact views |
| Local Code | tavu_localcode | Single Line (5) | Optional | DANE code (Colombia), FIPS (USA) |

**Initial seed data:** 52 US states/territories + 33 Colombian departments = 85 records.

#### 5.7.3 The `tavu_city` table

| Property | Value |
|---|---|
| Display name | `City` |
| Plural | `Cities` |
| Schema name | `tavu_city` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | Organization |
| Audit ✅ | |

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | Required | "Cali", "Miami" |
| State/Province | tavu_stateprovince | Lookup → tavu_stateprovince | Required | Cascading from country |
| Country | tavu_country | Lookup → tavu_country | Optional | Auto-filled from State/Province (denormalized for query speed) |
| Population | tavu_population | Whole Number | Optional | Useful for sort/relevance |
| Is Capital | tavu_iscapital | Yes/No | Optional | True for Bogotá, Washington D.C., state/department capitals |
| Local Code | tavu_localcode | Single Line (10) | Optional | DANE municipality code (Colombia) |

**Auto-fill logic:** when `tavu_stateprovince` is selected, a Plugin/Flow copies the parent country into `tavu_country` automatically. This denormalization avoids joins in Power BI reports and views.

**Initial seed data:** ~60 Colombian cities (32 department capitals + top municipalities) + ~200 US cities (top metros by population) = ~260 records. Pre-loaded in the managed solution via CSV import.

#### 5.7.4 Custom address columns added to `account` and `contact`

Both `account` and `contact` receive three lookup fields that replace the free-text OOTB address fields for structured location data:

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Country | tavu_country | Lookup → tavu_country | Required in MVP | |
| State/Province | tavu_stateprovince | Lookup → tavu_stateprovince | Optional | Filtered by Country |
| City | tavu_city | Lookup → tavu_city | Optional | Filtered by State/Province |

**Cascading filter logic (JavaScript on form OnLoad + OnChange):**

```javascript
// When tavu_country changes:
// → filter tavu_stateprovince to show only states where tavu_country = selected country
// → clear tavu_stateprovince and tavu_city

// When tavu_stateprovince changes:
// → filter tavu_city to show only cities where tavu_stateprovince = selected state
// → clear tavu_city
```

The OOTB free-text address fields (`address1_line1`, `address1_city`, etc.) remain available for street-level detail but are not included in the main form tabs or views. Structured location data lives in the lookup fields; free-text fields are relegated to a secondary "Address Details" section for optional use.

**Why this design over free-text addresses:**
- Eliminates "Cali" vs "Santiago de Cali" vs "cali" data quality issues
- Enables clean Power BI grouping by city, state, and country without normalization
- Cascading lookups guide the user through a valid hierarchy — impossible to select a city in a non-existent state
- Admin controls the catalog — no orphan locations created by typos
---

## 6\. The `tavu_opportunity` table — The commercial pipeline

This is the central table of the sales model. Each opportunity represents a specific deal with a client.

### 6.1 Base configuration

| Property | Value |
| :---- | :---- |
| Display name | `Opportunity` |
| Plural | `Opportunities` |
| Schema name | `tavu_opportunity` |
| Primary column | `Topic` (`tavu_topic`) |
| Ownership | User or team |
| Activities ✅ | Notes ✅ |
| Enable for queues | ✅ |

### 6.2 State \+ Status Reason

OpenTavu deliberately keeps `statecode` and `statuscode` minimal and uses a dedicated lookup field (`tavu_salesstage` → `tavu_salesstage` configuration table, see Section 6.3bis) to represent the pipeline stage. This decision is grounded in a real-world observation: every firm has its own vocabulary for sales stages (one firm uses "RFI / Evaluation / RFP / Offer / Negotiation", another uses "Discovery / Proposal Drafted / Proposal Sent / Negotiation", another uses "Qualification / Demo / Quote / Commit"). Hardcoding these into `statuscode` makes the product un-deployable to firms with their own pipeline taxonomy — and locks the AI Forecasting roadmap module to a fixed vocabulary.

By moving the granular pipeline stage to a configurable lookup, OpenTavu becomes truly multi-tenant deployable and prepares the data layer for the future "AI-Assisted Forecasting & Capacity Planning" module (see VISION.md Section 8 roadmap).

| State (statecode) | Status Reasons (statuscode) | Meaning |
| :---- | :---- | :---- |
| **Open** (default) | Open | Active opportunity — granular stage tracked in `tavu_salesstage` |
| **Won** | Won | Closed successfully |
| **Lost** | Lost | Closed without success (reason in tavu\_lostreason) |

**Granular Lost Reason:** when state changes to Lost, the specific reason (Price, Competitor, Timing, etc.) is captured in the custom field `tavu_lostreason`, which is tied to a Global Choice. This is done this way because `statuscode` cannot be linked to Global Choices (Dataverse technical limitation).

**Where the stage lives:** the active stage of an open opportunity lives in `tavu_salesstage` (lookup to the configuration table). When the opportunity closes (Won or Lost), `tavu_salesstage` is preserved as historical reference (the last stage the opportunity was in before closing) but the operational driver becomes `statecode`. Power BI and views can filter by `tavu_salesstage` for granular pipeline analytics and by `statecode` for operational scope (Open vs Closed).

### 6.3 Custom columns

**Hybrid Customer architecture (key architectural decision):**

| Display Name | Schema | Type | Required | Notes |
| :---- | :---- | :---- | :---- | :---- |
| Topic | tavu\_topic | Single Line (Primary) | Required |  |
| **Customer** | **tavu\_customer** | **Customer (polymorphic Account+Contact)** | **Required** | **Single source of truth** |
| **Account (auto)** | **tavu\_account** | **Lookup → Account** | **Optional** | **Auto-populated when Customer=Account** |
| **Contact (auto)** | **tavu\_contact** | **Lookup → Contact** | **Optional** | **Auto-populated when Customer=Contact** |
| Primary Contact | tavu\_primarycontact | Lookup → Contact | Optional | Human interlocutor |
| Estimated Revenue | tavu\_estimatedvalue | Currency | Optional |  |
| Estimated Close Date | tavu\_estimatedclosedate | Date Only | Optional |  |
| **Sales Stage** | **tavu\_salesstage** | **Lookup → tavu\_salesstage** | **Required** | **Configurable pipeline stage. See Section 6.3bis.** |
| Probability | tavu\_probability | Whole Number (0-100) | Optional | Auto-populated from Sales Stage default. Editable. |
| Probability Is Manual | tavu\_probabilityismanual | Yes/No | Optional | Set to Yes when consultant overrides default. Plugin reads this flag to decide whether to re-apply stage default on stage change. |
| Discovery Notes | tavu\_discoverynotes | Multiple Lines | Optional |  |
| Source Lead | tavu\_sourcelead | Lookup → tavu\_lead | Optional | If it came from Path B |
| Lost Reason | tavu\_lostreason | Lookup → Global Choice tavu\_global\_lostreason | Optional | Mirror from close activity |
| Actual Revenue | tavu\_actualrevenue | Currency | Optional | Mirror |
| Actual Close Date | tavu\_actualclosedate | Date Only | Optional | Mirror |
| Close Notes | tavu\_closenotes | Multiple Lines | Optional | Mirror |
| Customer Tier (denorm) | tavu\_customertier | Lookup → tavu\_customertierdefinition | Optional | Auto from Account OR Contact |

**Plugin/Flow logic for auto-population (on create or modify of tavu\_customer):**

IF tavu\_customer points to Account:

→ tavu\_account \= that Account

→ tavu\_contact \= (empty)

→ tavu\_primarycontact NOT auto-populated (consultant fills manually)

→ tavu\_customertier \= Customer Tier of Account

IF tavu\_customer points to Contact:

→ tavu\_account \= (empty)

→ tavu\_contact \= that Contact

→ tavu\_primarycontact \= that Contact (auto, EDITABLE — consultant can change)

→ tavu\_customertier \= Customer Tier of Contact

**Why this hybrid architecture:**

- **Simple UX:** user only interacts with the Customer field. `setEntityTypes` (via systemsettings) filters by firm preference.  
- **Simple reporting:** Power BI uses `tavu_account` and `tavu_contact` directly, without handling polymorphism.  
- **Simple integrations:** point to typed specific fields.  
- **Microsoft standard pattern:** Dynamics 365 Quote, Order, and Invoice use exactly this pattern.

### 6.3bis The `tavu_salesstage` configuration table

This master table captures the pipeline stages used by `tavu_opportunity`. It exists as a configuration table — not hardcoded `statuscode` values — for a critical reason: **every Professional Services firm has its own pipeline vocabulary**. A consultancy might use "Discovery / Proposal Drafted / Proposal Sent / Negotiation". A QA boutique might use "RFI / Evaluation / RFP / Offer / Presentation / Negotiation". A digital agency might use "Qualification / Discovery Call / Pitch / Negotiation". Hardcoding these into `statuscode` would force every firm to either adopt OpenTavu's vocabulary or fork the product. Neither is acceptable.

**Base configuration:**

| Property | Value |
| :---- | :---- |
| Display name | `Sales Stage` |
| Plural | `Sales Stages` |
| Schema name | `tavu_salesstage` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | Organization |
| Audit ✅ | |

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
| :---- | :---- | :---- | :---- | :---- |
| Name | tavu\_name | Single Line (Primary) | Required | E.g.: "Discovery", "RFI", "Negotiation" |
| Display Order | tavu\_displayorder | Whole Number | Required | Order in the pipeline (1, 2, 3, …). Used to sort views and funnels. |
| Default Probability | tavu\_defaultprobability | Whole Number (0-100) | Required | Default probability applied to opportunities entering this stage |
| Forecast Category | tavu\_forecastcategory | Choice | Required | Pipeline / Best Case / Committed / Closed |
| Is Active | tavu\_isactive | Yes/No | Required | Default: Yes. Inactive stages disappear from new opportunity forms but remain readable for historical records |
| Color | tavu\_color | Single Line | Optional | Hex code for Power BI funnel visualizations |
| Notes | tavu\_notes | Multiple Lines | Optional | Internal documentation: "When does an opportunity enter this stage? What signals justify advancing?" |

**Forecast Category — semantic meaning:**

This Choice field is standard forecasting terminology in mature commercial organizations (Salesforce, Clari, Gong all use the same vocabulary). It enables Sales Manager conversations like *"How much do I have in Committed this quarter?"* and *"What percentage of my Best Case typically converts?"* — without requiring custom Power BI logic per firm.

| Value | Meaning | Typical probability range |
| :---- | :---- | :---- |
| **Pipeline** | Early-stage exploration. Possible but uncommitted. | 10–35% |
| **Best Case** | Active engagement. Reasonable chance to close. | 40–65% |
| **Committed** | Late-stage, strong signals of close. Forecast-grade. | 70–95% |
| **Closed** | Terminal stage (Won or Lost). Probability = 100% or 0%. | — |

**Default seed data shipped with the managed solution:**

OpenTavu ships with a generic Professional Services pipeline as seed data. Firms with their own vocabulary edit the records (rename, reorder, adjust probabilities) or deactivate them and create their own.

| Name | Display Order | Default Probability | Forecast Category | Color |
| :---- | :---- | :---- | :---- | :---- |
| Discovery | 1 | 20 | Pipeline | `#605E5C` |
| Proposal Drafted | 2 | 40 | Best Case | `#0078D4` |
| Proposal Sent | 3 | 60 | Best Case | `#0078D4` |
| Negotiation | 4 | 80 | Committed | `#CA5010` |

**Example reconfiguration for a firm with bid-driven pipeline:**

| Name | Display Order | Default Probability | Forecast Category |
| :---- | :---- | :---- | :---- |
| RFI | 1 | 10 | Pipeline |
| Evaluation | 2 | 20 | Pipeline |
| RFP | 3 | 35 | Pipeline |
| Offer | 4 | 60 | Best Case |
| Presentation | 5 | 75 | Committed |
| Negotiation | 6 | 85 | Committed |

The admin reconfigures stages without touching code, without forking the product, without breaking existing opportunities.

**Plugin logic for probability defaulting (Pre-Operation on `tavu_opportunity` Create/Update):**

```
on tavu_opportunity Pre-Operation Create:
    if tavu_salesstage is set AND tavu_probability is null:
        tavu_probability = tavu_salesstage.tavu_defaultprobability
        tavu_probabilityismanual = false

on tavu_opportunity Pre-Operation Update:
    if tavu_salesstage changed AND tavu_probabilityismanual == false:
        tavu_probability = NEW tavu_salesstage.tavu_defaultprobability
    
    if tavu_probability changed by user (not by plugin):
        tavu_probabilityismanual = true
```

**Distinguishing "user-edited" from "plugin-edited":** the Plugin sets a thread-local flag before writing `tavu_probability`, so the post-write trigger that flips `tavu_probabilityismanual` does not fire on plugin writes. Alternatively, the Plugin can write both fields in the same Update operation (`tavu_probability` from stage default + `tavu_probabilityismanual = false`), and a separate JavaScript on form `OnChange` of `tavu_probability` sets `tavu_probabilityismanual = true` when the change originates from the UI.

**Reset to stage default — ribbon button:**

A "Reset Probability to Stage Default" button is available on the opportunity form. When clicked, it sets `tavu_probabilityismanual = false` and re-applies the current stage's default probability. Useful when the consultant overrode the value temporarily and wants to return to the system suggestion.

**Why this matters for AI-First:**

The configurable stage table is not just a UX nicety — it is the **data foundation for the AI-Assisted Forecasting & Capacity Planning module** on the roadmap (VISION.md Section 8). When that module ships (target: Month 9–12, once each tenant has accumulated 30+ closed opportunities), it will:

- Analyze historical Won/Lost ratios per stage **per tenant** (using each firm's own vocabulary)
- Propose adjusted `tavu_defaultprobability` values to the admin (*"Your Negotiation stage actually closes at 65%, not the 80% currently configured — accept the update?"*)
- Generate forecast confidence intervals using the firm's actual conversion curves
- Detect stuck opportunities relative to typical stage duration

None of this is possible if pipeline stages live hardcoded in `statuscode`. The architectural decision in this section is the enabling condition for the AI forecasting roadmap module.

### 6.4 Customer Mode and system-level configuration

OpenTavu stores tenant-level settings in a single Organization-owned record: `tavu_systemsettings`.

| Property | Value |
|---|---|
| Display name | `System Settings` |
| Plural | `System Settings` |
| Schema name | `tavu_systemsettings` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | Organization |
| Audit ✅ | |
| Records | Single record per tenant |

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
|---|---|---|---|---|
| Name | tavu_name | Single Line (Primary) | Required | E.g.: "OpenTavu Settings" |
| Customer Mode | tavu_customermode | Choice | Required | B2B_Only / B2C_Only / Mixed (default: Mixed) |
| Default Country | tavu_defaultcountry | Lookup → tavu_country | Optional | Pre-fills Country on new Account and Contact records |
| Activity Threshold Days | tavu_activitythresholddays | Whole Number | Optional | Number of days without customer activity before Engagement Status is automatically marked as Inactive (default: 90) |

**Customer Mode behavior:**

The `tavu_customermode` flag controls the Customer lookup behavior across the system. JavaScript on form OnLoad reads the setting and filters the `tavu_customer` polymorphic lookup accordingly:

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

**Default Country behavior:**

When a new `account` or `contact` record is created, a Plugin/Flow reads `tavu_defaultcountry` from `tavu_systemsettings` and pre-fills `tavu_country` if it is empty. The user can override the pre-filled value. If `tavu_defaultcountry` is not configured, no pre-fill occurs.

**Activity Threshold Days behavior:**

A scheduled Power Automate flow (runs daily) queries all Contacts where `tavu_engagementstatus = Engaged` and `tavu_lastengagementdate` is older than `tavu_activitythresholddays` days. Matching contacts are automatically moved to `tavu_engagementstatus = Inactive`. The default threshold is 90 days if the field is left empty.

**Why the "mirror fields" (tavu\_lostreason, actualrevenue, actualclosedate, closenotes)?**

These fields live in `tavu_opportunity` even though the "primary" information is captured in the `tavu_opportunityclose` activity. The duplication is deliberate for two reasons:

1. **Power BI reports:** filtering by `tavu_opportunity.tavu_lostreason` is trivial. Filtering by related activities requires complex joins.  
2. **Operational views:** a consultant who opens the opportunity sees the loss reason without having to open the activity. Saves clicks.

A Plugin/Power Automate automatically syncs both places when `tavu_opportunityclose` is created/modified.

---

## 7\. The `tavu_opportunityclose` activity — Historical close log

Instead of simply changing a Status field on the opportunity, OpenTavu creates an **activity record** every time a close is attempted.

### 7.1 Why Activity Type?

- **Appears automatically in the opportunity timeline**  
- **Allows multiple close/reopen attempts** (each leaves a trace)  
- **Captures rich context** (notes, actual date, reason) without polluting the main opportunity form  
- **Inherent audit trail** (who closed, when, what they said)

### 7.2 Base configuration

| Property | Value |
| :---- | :---- |
| Display name | `Opportunity Close` |
| Plural | `Opportunity Closures` |
| Schema name | `tavu_opportunityclose` |
| **Type** | **Activity** |
| Primary column | `Subject` (standard for activities) |
| Ownership | User or team |
| Notes ✅ | Audit ✅ |

### 7.3 Custom columns

| Display Name | Schema | Type | Required |
| :---- | :---- | :---- | :---- |
| Subject | subject (OOTB) | Single Line (Primary) | Required |
| Activity Date | actualstart (OOTB) | Date Only | Required |
| Description (Close Notes) | description (OOTB) | Multiple Lines | Required |
| **Lost Reason** | **tavu\_lostreason** | **Choice → Global Choice `tavu_lostreason`** | Required on Lost |
| **Actual Revenue** | **tavu\_actualrevenue** | **Currency** | Required on Won |
| **Actual Close Date** | **tavu\_actualclosedate** | **DateTime** | Required |
| Regarding | regardingobjectid (OOTB) | Lookup polymorphic → tavu\_opportunity | Required |
| Resource | tavu\_resource | Lookup → SystemUser | Required |

> **Won vs Lost is the activity's own `statuscode`, not a separate field.** Won \= `576600001`, Lost \= `576600002` (both under the Completed state). There is **no `tavu_closetype` field** — an earlier draft of this guide proposed one; it was dropped as redundant with `statuscode`, and the implementation reads the outcome from `statuscode`.

### 7.4 Close mechanics — ribbon buttons + guided dialog

OpenTavu adds three commands to the `tavu_opportunity` main-form ribbon: **Closed as
Won** and **Closed as Lost** (shown while the opportunity is Open) and **Reopen
Opportunity** (shown while it is closed). Full build and registration detail lives
in `docs/opportunity-close-dialog.md`.

**Design: the opportunity is the source of truth.** The Won/Lost buttons open a
guided **custom-page dialog** (`tavu_opportunityclosedialog_31702`) that captures
the close inputs and writes them onto the opportunity in a single save; the server
plugins then perform the derived work. The buttons do **not** create the close
activity from the client — the plugin does.

**Closed as Won / Closed as Lost:**

1. Opens the custom-page dialog. The outcome (won/lost) is passed via the
   navigateTo `entityName` parameter, because dialogs drop other custom params.  
2. Won shows Actual Revenue (prefilled from Estimated Revenue); Lost shows Lost
   Reason. Both show Actual Close Date (default today) and Close Notes.  
3. On confirm, the dialog `Patch`es the opportunity: `statecode = Inactive` **and**
   `statuscode = Won/Lost` (both must be set together, or Dataverse rejects the
   transition), plus the close fields (`tavu_actualrevenue` or `tavu_lostreason`,
   `tavu_actualclosedate`, `tavu_closenotes`). `tavu_salesstage` is preserved as the
   last stage before close.  
4. `Pl.Opportunity.LifecycleTracker` (Pre-Op) validates the inputs (Won requires
   Actual Revenue > 0; Lost requires a Lost Reason), forces `tavu_probability` to
   100 (Won) / 0 (Lost), and defaults the close date if empty.  
5. `Pl.Opportunity.CloseOrchestrator` (Post-Op) creates the `tavu_opportunityclose`
   history log (created Open, then transitioned to Completed + Won/Lost, since
   activities cannot be created in a completed state), and on Won marks the
   commercial subject as a customer (`tavu_iscustomer = Yes`, `tavu_customersince`
   stamped if empty) on the Account (B2B) or Contact (B2C).

**Engagement status (`tavu_engagementstatus`) is deliberately NOT changed on
close.** That communication state is owned by Module 3 (AI Activity Capture), which
derives it from real activity rather than as a hardcoded close side effect.

### 7.5 Reopening opportunities

**Option 1 — Reopen the same opportunity (Reopen Opportunity button):**

- The button flips `statecode` back to Active and `statuscode` to Open.  
- `Pl.Opportunity.LifecycleTracker` detects the reopen and re-applies the current
  Sales Stage's default probability (clearing the manual flag). The close fields
  (revenue / lost reason / close date) are preserved as history and stay read-only
  on the form.  
- v1 does not log a separate reopen activity.

**Option 2 — Create new opportunity (recommended when scope changed):**

- Keep Lost opportunity closed (historical data preserved)  
- Create new opportunity linked to same Account/Contact  
- Reference previous opportunity in Discovery Notes

The choice between Option 1 and Option 2 depends on whether context/scope changed significantly. If it's the same deal resumed: Option 1\. If it's a new deal under the same client: Option 2\.

---

## 8\. The `tavu_proposal` table — SOWs and proposals

This table captures the formal proposals and SOWs (Statements of Work) generated during the sales process. It is the piece that connects the opportunity to the document the client signs.

### 8.1 Base configuration

| Property | Value |
| :---- | :---- |
| Display name | `Proposal` |
| Plural | `Proposals` |
| Schema name | `tavu_proposal` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | User or team |
| Activities ✅ | Notes ✅ |

### 8.2 State \+ Status Reason

| State (statecode) | Status Reasons (statuscode) |
| :---- | :---- |
| **Active** (default) | Draft, AI Generated — Awaiting Review, Sent to Client |
| **Inactive** | Approved by Client, Rejected by Client, Superseded, Withdrawn |

> **Lifecycle is button-driven; Status Reason is read-only.** Transitions happen only via the ribbon buttons (Send to Client / Mark as Approved / Mark as Lost / Create New Version), never by editing the picklist. *Under Internal Review* and *Awaiting Decision* were **hidden** (reversible) — they had no button and no clear pain point; a firm that needs an internal-review gate can un-hide *Under Internal Review* and add its button. The full lifecycle + the Send-to-Client email draft (AI body + branded PDF from `tavu_companyprofile`) are documented in `proposal-lifecycle.md`.

### 8.3 Custom columns

**Group A — Identification and link to opportunity:**

| Display Name | Schema | Type | Required | When populated |
| :---- | :---- | :---- | :---- | :---- |
| Name | tavu\_name | Single Line (Primary) | Required | On creation |
| Opportunity | tavu\_opportunity | Lookup → tavu\_opportunity | Required | On creation (critical link) |
| Customer | tavu\_customer | Customer (polymorphic Account+Contact) | Read-only | Auto-inherited from opportunity |
| Account | tavu\_account | Lookup → Account | Read-only | Auto-inherited from opportunity (B2B case) |
| Contact | tavu\_contact | Lookup → Contact | Read-only | Auto-inherited from opportunity (B2C case) |
| Discovery Notes | tavu\_discoverynotes | Multiple Lines (50,000+ chars) | Optional | Auto-inherited from opportunity |
| Effective From | tavu\_effectivefrom | Date Only | Optional |  |
| Effective To | tavu\_effectiveto | Date Only | Optional |  |

**Inheritance logic on proposal creation:** when a `tavu_proposal` record is created linked to a `tavu_opportunity`, a Plugin/Flow automatically copies `tavu_opportunity.tavu_customer` → `tavu_proposal.tavu_customer`, account, and contact fields. These fields are read-only in the seller's form. If the customer changes, it is changed on the opportunity, not on the proposal directly.

**Group B — Document data:**

| Display Name | Schema | Type | Required | When populated |
| :---- | :---- | :---- | :---- | :---- |
| Version | tavu\_version | Single Line | Optional | Defaulted to `v1` by the LifecycleTracker plugin on create; incremented by Create New Version (v1 → v2 …). |
| Sent Date | tavu\_sentdate | Date Only | Optional | Stamped by the "Send to Client" button. |
| Expected Decision Date | tavu\_expecteddecisiondate | Date Only | Optional | When sending |
| Proposal Content | tavu\_proposalcontent | Multiple Lines (50,000+ chars) | Optional | Narrative body (input/output of future AI Proposal Generator) |

> **Note:** there is **no `tavu_documenttype`** field (an earlier draft listed one; it was never implemented). The full proposal lifecycle — statuses, lock, versioning, the Send/Approve/Lost/New Version buttons, Approved→Won, and the header totals auto-refresh — is documented in **`docs/proposal-lifecycle.md`**.

**Group C — Quotation (fields added in v1.2):**

| Display Name | Schema | Type | Required | When populated |
| :---- | :---- | :---- | :---- | :---- |
| Price List | tavu\_pricelist | Lookup → tavu\_pricelist | Optional | On creation — determines currency and base prices for lines |
| Subtotal | tavu\_subtotal | Currency (calculated) | — | Auto: SUM of tavu\_proposalline.tavu\_extendedamount |
| Total Tax | tavu\_totaltax | Currency (calculated) | — | Auto: SUM subtotal × taxrate (Rollup) |
| Total | tavu\_total | Currency (calculated) | — | Auto: tavu\_subtotal \+ tavu\_totaltax (Plugin) |
| Total Cost | tavu\_totalcost | Currency (calculated) | — | Auto: SUM of tavu\_proposalline.tavu\_linecost — visible only to Ops/Manager roles |
| Gross Margin (%) | tavu\_grossmargin | Decimal (calculated) | — | Auto: ((subtotal − totalcost) / subtotal) × 100 — visible only to Ops/Manager |
| Show Kit Breakdown | tavu\_showkitbreakdown | Yes/No | Optional | Default: No. If Yes, PDF expands kit components |

**Note on tavu\_taxrate:** OpenTavu does not implement a tax engine. The seller manually enters the percentage based on the client's jurisdiction. For firms that require automatic calculation by US state, Avalara or TaxJar integration is added as an external connector in future phases.

**Note on tavu\_totalcost and tavu\_grossmargin:** visible only to Operations Manager and Sales Manager via Field Security Profile. The seller's form does not include them, so as not to condition negotiation with internal cost data.

### 8.4 Relationship with opportunity

The relationship is **N:1** — one opportunity can have multiple proposals (versions, change orders, related contracts).

**Example:**

Opportunity \#501 "MegaCorp ERP Implementation"

↓

├─ Proposal v1.0 (Draft → Sent to Client → Rejected by Client)

├─ Proposal v1.1 (Draft → AI Generated → Sent to Client → Approved by Client) ✓

├─ Change Order \\\#1 (after project start)

└─ Change Order \\\#2 (additional scope)

Each proposal carries its own independent statecode. The opportunity is closed Won when ONE of the proposals is Approved by Client (and signed).

### 8.5 Connection with the future AI Proposal Generator module

The `Proposal Content` field is dimensioned generously (50,000+ chars) because it is designed as the input/output of the AI Proposal Generator (roadmap module):

- **Input:** AI reads `Discovery Notes` from the opportunity, client context, previously won opportunities with similar clients  
- **Output:** AI generates a draft of Proposal Content that the consultant reviews, edits, and sends  
- **State machine:** Draft → AI Generated — Awaiting Review → Under Internal Review → Sent to Client

**Strategic priority.** Preparing proposals and SOWs is consistently the single most time-consuming step of the sales process for senior consultants — the costliest bottleneck in this area. That is why the AI Proposal Generator (a.k.a. AI RFP & Proposal Architect) is the **top roadmap priority for the proposals area**, even though it is sequenced after the three foundational modules that establish a clean data layer (see VISION §8). The design intent is **AI-first**: the AI *generates the draft* (scope, lines, pricing from opportunity context + prior won proposals) and the consultant reviews and edits — replacing the manual authoring, not bolting an "ask AI" button onto a blank field.

This module is NOT in the MVP, but the schema structure is already prepared.

---

## 8bis. Quotation model — Proposal lines, catalog, and kits

This section extends `tavu_proposal` with the complete quotation system: the line grid the seller sees, the product/service catalog, kit (bundle) management, price lists, and delivery roles. The guiding principle is **zero friction for the seller**: a single grid, no visible BOM complexity.

### 8bis.1 Quotation model overview

The seller interacts exclusively with two surfaces:

1. **`tavu_proposal`** — the quotation header, linked to the opportunity.  
2. **`tavu_proposalline`** — the line grid where they add services, licenses, or kits. One line per item, regardless of whether it's simple or a composite bundle.

\[Seller creates tavu\_proposal linked to opportunity\]

        ↓

\[Adds tavu\_proposallines — one grid, one item per line\]

        ↓

\[Selects tavu\_product → can be simple service or kit\]

        ↓

\[System auto-fills price from tavu\_pricelist\]

        ↓

\[Seller adjusts quantity, discount, role if applicable\]

        ↓

\[tavu\_proposal calculates subtotal, tax, total, margin\]

        ↓

\[When generating PDF: if line is a kit, it explodes in memory\]

\[Client sees breakdown; Dataverse stores a single line\]

### 8bis.2 Front-end entities (what the seller touches)

#### 8bis.2.1 The `tavu_proposalline` table — The seller's single grid

This is the most important table for the seller's experience. Each row represents an item in the proposal, whether a simple service, a license, or a complete kit.

**Base configuration:**

| Property | Value |
| :---- | :---- |
| Display name | `Proposal Line` |
| Plural | `Proposal Lines` |
| Schema name | `tavu_proposalline` |
| Primary column | `Name` (`tavu_name`, auto-generated) |
| Ownership | User or team |
| Audit ✅ |  |

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
| :---- | :---- | :---- | :---- | :---- |
| Proposal | tavu\_proposal | Lookup → tavu\_proposal | Required | Link to the header proposal |
| Product | tavu\_product | Lookup → tavu\_product | Required | Service, license, or kit. JS detects if kit and shows visual badge |
| Unit of Measure | tavu\_unitofmeasure | Lookup → tavu\_uom | Required | Auto-filled from tavu\_product.tavu\_defaultunit, editable |
| Quantity | tavu\_quantity | Decimal | Required | E.g.: 40 (hours), 3 (months), 1 (complete kit) |
| Price Per Unit | tavu\_priceperunit | Currency | Required | Auto from tavu\_pricelist; manually editable |
| Unit Cost | tavu\_unitcost | Currency | Optional | Auto from tavu\_product.tavu\_cost |
| Tax Rate (%) | tavu\_taxrate | Decimal | Optional | Copied automatically from tavu\_pricelistitem |
| Tax Amount | tavu\_taxamount | Currency (Calculated) | Optional | tavu\_subtotal \* (tavu\_taxrate/100) |
| Discount | tavu\_discount | Currency | Optional | Discount amount in currency (not percentage) |
| Subtotal | tavu\_subtotal | Currency (Calculated) | — | tavu\_quantity \* tavu\_priceperunit |
| Total | tavu\_total | Currency (calculated) | — | tavu\_subtotal \+ tavu\_taxamount − tavu\_discount |
| Line Cost | tavu\_linecost | Currency (calculated) | — | tavu\_quantity × tavu\_unitcost; visible to operations roles |
| Override Price | tavu\_overrideprice | Yes/No | Optional | Allows overriding tavu\_priceperunit for the product |

**JavaScript logic on tavu\_product OnChange:**

// When a product is selected on the line:

if (product.tavu\_iskit \=== true) {

// Show visual "KIT" badge next to product name

// Auto-fill UOM and Price Per Unit from active Price List

// Show tooltip: "This item includes components — see breakdown in PDF"

} else {

// Standard behavior: auto-fill UOM and price

// If tavu\\\_roleid is filled, use tavu\\\_servicerole.tavu\\\_defaultrate

// as price suggestion (editable)

}

**Kit behavior in the proposal — architectural decision:**

The kit appears as **a single line** in `tavu_proposalline`. The explosion (component breakdown) happens **only when generating the PDF/Word**, ephemerally in memory. Component lines are never written back to Dataverse. Reasons:

- Changing the kit quantity requires editing one line, not N.  
- Kit discount applies to the kit total, not distributed across lines.  
- If the client rejects the kit, one row is deleted, not several.  
- Audit trail is clean: the sales intent was the kit as a unit.

**When to use tavu\_roleid vs tavu\_product with role name:**

For MVP, two strategies are supported based on firm maturity:

| Strategy | When to use | How |
| :---- | :---- | :---- |
| **Product per role** | Small firm (≤15 people), few profiles | Create separate products: "Senior Architect Hour", "Junior Consultant Hour". Without using tavu\_roleid. |
| **Role on the line** | Mid-size firm with clear role structure | Single product "Consulting Hour" \+ tavu\_roleid field on each line. Price comes from tavu\_servicerole.tavu\_defaultrate. |

The "Product per role" strategy is simpler to implement in MVP; "Role on the line" is more flexible and enables utilization reports by profile.

### 8bis.3 Back-end entities (catalog administered by Ops)

#### 8bis.3.0 The `tavu_unitofmeasureschedule` table — Unit groups

Groups interconvertible units of measure (the parent of `tavu_uom`). Each group defines a family of units that convert among themselves via `tavu_uom.tavu_conversionfactor`, anchored on the unit flagged `tavu_isbaseunit = Yes`. Referenced by `tavu_uom.tavu_unitgroup` and by `tavu_product.tavu_defaultunitgroup`.

| Property | Value |
| :---- | :---- |
| Display name | `Unit of Measure Schedule` |
| Plural | `Unit of Measure Schedules` |
| Schema name | `tavu_unitofmeasureschedule` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | Organization |
| Audit ✅ |  |

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
| :---- | :---- | :---- | :---- | :---- |
| Name | tavu\_name | Single Line (Primary) | Required | E.g.: "Time", "Software", "General" |

**Relationships:**

| Relationship | Type | Notes |
| :---- | :---- | :---- |
| `tavu_uom_UnitGroup_tavu_unitofmeasureschedule` | 1:N → `tavu_uom` | One group contains many units; the group's base unit has `tavu_isbaseunit = Yes` |

**Initial seed data (pre-loaded in managed solution):**

| Name |
| :---- |
| Time |
| Software |
| General |

#### 8bis.3.1 The `tavu_uom` table — Units of measure

| Property | Value |
| :---- | :---- |
| Display name | `Unit of Measure` |
| Plural | `Units of Measure` |
| Schema name | `tavu_uom` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | Organization |
| Audit ✅ |  |

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
| :---- | :---- | :---- | :---- | :---- |
| Name | tavu\_name | Single Line (Primary) | Required | E.g.: "Hour", "Day", "Month", "License", "Unit" |
| Unit Group | tavu\_unitgroup | Lookup → tavu\_unitofmeasureschedule | Required (Business required) | Schedule/group of interconvertible UOMs the unit belongs to. E.g.: "Time" (Hour, Day); "Software" (License, Unit). Relationship: `tavu_uom_UnitGroup_tavu_unitofmeasureschedule` |
| Conversion Factor | tavu\_conversionfactor | Decimal | Required | E.g.: 1 Day \= 8 Hours, relative to the group's base unit |
| Is Base Unit | tavu\_isbaseunit | Yes/No | Required | Default: No. Marks the root UOM of the group (Conversion Factor \= 1) |

**Initial seed data (pre-loaded in managed solution):**

| Name | Unit Group | Conversion Factor | Is Base Unit |
| :---- | :---- | :---- | :---- |
| Hour | Time | 1 | Yes |
| Day | Time | 8 | No |
| Month | Time | 160 | No |
| License | Software | 1 | Yes |
| Unit | General | 1 | Yes |

#### 8bis.3.2 The `tavu_product` table — Master catalog

Everything the firm sells must exist here, whether an individual service, a license, or a kit (composite bundle).

| Property | Value |
| :---- | :---- |
| Display name | `Product` |
| Plural | `Products` |
| Schema name | `tavu_product` |
| Primary column | `Name` (`tavu_name`) |
| Ownership | Organization |
| Audit ✅ | Quick create ❌ |

**State \+ Status Reason:**

| State | Status Reasons |
| :---- | :---- |
| **Active** (default) | Available |
| **Inactive** | Discontinued, Replaced |

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
| :---- | :---- | :---- | :---- | :---- |
| Name | tavu\_name | Single Line (Primary) | Required | E.g.: "IT Consulting", "Power Apps License", "Cloud Migration Kit" |
| Default Unit Group | tavu\_defaultunitgroup | Lookup → tavu\_unitofmeasureschedule | Required | Logical category the product belongs to |
| Default Unit | tavu\_defaultunit | Lookup → tavu\_uom | Required | Unit used to sell the product |
| Cost | tavu\_cost | Currency | Optional | Internal unit cost. Base for margin calculation in proposal |
| Is Kit | tavu\_iskit | Yes/No | Required | Default: No. If Yes, product is composed of items in tavu\_kitcomponent |
| Description | tavu\_description | Multiple Lines | Optional | Commercial description visible in proposals |
| AI Categorization Hint | tavu\_aihint | Multiple Lines | Optional | Text to help the AI Proposal Generator understand when to include this product |

**Critical business restriction:** a product with `tavu_iskit = Yes` CANNOT be a `tavu_childproduct` in any `tavu_kitcomponent` record. MVP limits kits to one level of depth (kit contains individual products; nested kits are not supported). A plugin or Business Rule must block this with a clear error message if attempted.

**Initial seed data:**

| Name | Default UOM | Is Kit | Standard Cost |
| :---- | :---- | :---- | :---- |
| Consulting Hour | Hour | No | $60 USD |
| Senior Architect Hour | Hour | No | $90 USD |
| Junior Consultant Hour | Hour | No | $45 USD |
| Power Apps Premium License | License | No | $20 USD |
| Cloud Migration Kit | Unit | **Yes** | (calculated from components) |

#### 8bis.3.3 The `tavu_kitcomponent` table — The kit recipe

This table defines the internal composition of each kit. It is the BOM (Bill of Materials) of the system. The seller never sees it directly; the administrator configures it once and the system consults it when generating documents.

**Why an intermediate table (not a Parent field on tavu\_product):** a `tavu_parentproduct` field on `tavu_product` would create a single-level flat tree that collapses as soon as the same child product appears in multiple kits with different quantities. The intermediate table supports many-to-many relationships with their own attributes (quantity, UOM per component) — the real case in consulting (40 consulting hours in one kit, 80 in another).

| Property | Value |
| :---- | :---- |
| Display name | `Kit Component` |
| Plural | `Kit Components` |
| Schema name | `tavu_kitcomponent` |
| Primary column | `Name` (`tavu_name`, auto-generated) |
| Ownership | Organization |
| Audit ✅ |  |

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
| :---- | :---- | :---- | :---- | :---- |
| Parent Product | tavu\_parentproduct | Lookup → tavu\_product | Required | The kit. Must have tavu\_iskit \= Yes |
| Child Product | tavu\_childproduct | Lookup → tavu\_product | Required | The component. Must have tavu\_iskit \= No (business restriction) |
| Quantity | tavu\_quantity | Decimal | Required | How many units of the component are in the kit |
| Unit of Measure | tavu\_unitofmeasure | Lookup → tavu\_uom | Required | UOM of the component within the kit |

**Example configuration of "Cloud Migration Kit":**

| Parent Product | Child Product | Quantity | UOM |
| :---- | :---- | :---- | :---- |
| Cloud Migration Kit | Consulting Hour | 40 | Hour |
| Cloud Migration Kit | Power Apps Premium License | 2 | License |

When the PDF is generated with `tavu_showkitbreakdown = Yes`, the flow explodes this table in memory and renders the breakdown in the document. When `No`, the PDF shows only "Cloud Migration Kit — 1 Unit — $X".

#### 8bis.3.4 The `tavu_pricelist` and `tavu_pricelistitem` tables — Price lists

Allow having different rates by market, client type, or currency: "Standard USD Rate", "Partner Rate", "Colombia COP Rate", etc.

**`tavu_pricelist` custom columns:**

| Display Name | Schema | Type | Required | Notes |
| :---- | :---- | :---- | :---- | :---- |
| Name | tavu\_name | Single Line (Primary) | Required | E.g.: "Standard USD Rate 2026" |
| Effective Date | tavu\_effectivedate | Date Only | Optional | When it becomes effective |
| Expiration Date | tavu\_expirationdate | Date Only | Optional | When it expires |
| Is Default | tavu\_isdefault | Yes/No | Optional | The list pre-selected when creating a proposal |

**Currency:** the price list's currency is the native Dataverse `transactioncurrencyid` (lookup → `transactioncurrency`), chosen once at the header. The former custom `tavu_currency` field was removed — a money-typed or duplicate Choice field is the wrong tool; the native lookup handles selection and exchange rates. `transactioncurrencyid` persists on `tavu_pricelist` even with no money column on the table, functioning as a pure currency selector.

**`tavu_pricelistitem` custom columns:**

| Display Name | Schema | Type | Required | Notes |
| :---- | :---- | :---- | :---- | :---- |
| Price List | tavu\_pricelist | Lookup → tavu\_pricelist | Required |  |
| Product | tavu\_product | Lookup → tavu\_product | Required |  |
| Price Per Unit | tavu\_priceperunit | Currency | Required | Sale price for this product in this list |
| Quantity | tavu\_quantity | Decimal | Optional | For volume-tiered pricing |
| Tax Rate (%) | tavu_taxrate | Decimal | Optional | Copied to the proposal line on product selection |

**Currency inheritance (lines follow the header):** each `tavu_pricelistitem` carries its own native `transactioncurrencyid` (because `tavu_priceperunit` is a money field), so Dataverse does NOT inherit it from the parent automatically. A **Pre-Operation Create/Update plugin on `tavu_pricelistitem`** reads the parent `tavu_pricelist.transactioncurrencyid` and stamps it onto the Target — covering UI, import, and integration paths (a Business Rule or JavaScript would miss imports). The currency field is **not exposed on the line form**: currency is decided once at the header and inherited, never edited per line. This enforces "one price list = one currency", which the proposal model depends on (a proposal adopts the currency of its assigned price list).

**Auto-fill logic for price in tavu\_proposalline:**

When tavu\_product is selected on a proposal line:

1\. Read tavu\_pricelist from the header proposal

2\. Look up in tavu\_pricelistitem: tavu\_pricelist \= \[current list\] AND tavu\_product \= \[selected product\]

3\. If found → fill tavu\_priceperunit with tavu\_pricelistitem.tavu\_priceperunit

4\. If not found → leave tavu\_priceperunit empty for seller to enter manually

5\. If tavu\_roleid is filled → suggest tavu\_servicerole.tavu\_defaultrate as optional override

#### 8bis.3.5 The `tavu_servicerole` table — Delivery roles

Allows differentiating the price and internal cost of a service based on the profile of the person delivering it, without needing a separate product for each work type × profile combination.

**Custom columns:**

| Display Name | Schema | Type | Required | Notes |
| :---- | :---- | :---- | :---- | :---- |
| Name | tavu\_name | Single Line (Primary) | Required | E.g.: "Senior Architect", "Junior Consultant", "PM" |
| Default Rate | tavu\_defaultrate | Currency | Required | Standard sale price per hour for this role |
| Cost Rate | tavu\_costrate | Currency | Required | Internal cost per hour for this role (for real margin) |
| Description | tavu\_description | Multiple Lines | Optional | Profile description and responsibilities |

**Initial seed data:**

| Name | Default Rate | Cost Rate |
| :---- | :---- | :---- |
| Senior Architect | $200 USD/hr | $90 USD/hr |
| Senior Consultant | $150 USD/hr | $70 USD/hr |
| Junior Consultant | $100 USD/hr | $45 USD/hr |
| Project Manager | $130 USD/hr | $60 USD/hr |

### 8bis.4 Seller workflow when quoting

**Step 1 — Seller opens the opportunity and creates a new proposal:**

tavu\_proposal:

\- Name: "RetailCorp Migration v1.0"

\- Opportunity: \[link to tavu\_opportunity\]

\- Price List: "Standard USD Rate 2026" (auto-selected by tavu\_isdefault)

\- Show Kit Breakdown: No (default)

\- statuscode: Draft

**Step 2 — Seller adds lines to the grid:**

| Product | Role | Qty | UOM | Price/Unit | Discount | Extended |
| :---- | :---- | :---- | :---- | :---- | :---- | :---- |
| Cloud Migration Kit | — | 1 | Unit | $8,500 | $0 | $8,500 |
| Consulting Hour | Senior Architect | 10 | Hour | $200 | $0 | $2,000 |
| User Training | — | 2 | Day | $1,200 | $200 | $2,200 |

The "KIT" field appears with a visual badge in the grid to distinguish it from simple services. The seller only interacts with this grid — they never see the tavu\_kitcomponent table.

**Step 3 — Proposal calculates automatically:**

Subtotal:     $12,700

Tax Rate:     8.5%

Tax Amount:   $1,079.50

Total:        $13,779.50

\[Visible only to Ops/Manager:\]

Total Cost:   $5,850

Gross Margin: 53.9%

**Step 4 — Seller generates PDF:**

- If `tavu_showkitbreakdown = No` → PDF shows the 3 lines as they appear in the grid.  
- If `tavu_showkitbreakdown = Yes` → PDF expands the Cloud Migration Kit into its components (40 consulting hours \+ 2 licenses) for greater transparency with the client. The proposal in Dataverse still has a single line.

**The kit explosion happens in Power Automate (or in the document generation plugin), never in Dataverse.** The flow reads `tavu_kitcomponent` for the kit line, calculates quantities (line.tavu\_quantity × component.tavu\_quantity), and writes the result to the Word template. It does not create additional proposal records.

### 8bis.5 Pre-loaded seed data (managed solution)

To reduce setup time for a 15-person firm from hours to minutes, the managed solution includes the following pre-loaded data that the implementation consultant only needs to adjust:

| Table | Seed records included |
| :---- | :---- |
| tavu\_uom | 5 UOMs: Hour, Day, Month, License, Unit |
| tavu\_product | 4 individual services \+ 1 fully configured example kit |
| tavu\_kitcomponent | Components of the example kit (editable list) |
| tavu\_pricelist | 1 "Standard USD Rate" list marked as default |
| tavu\_pricelistitem | Prices of the 4 services in the standard list |
| tavu\_servicerole | 4 roles: Senior Architect, Senior Consultant, Junior Consultant, PM |

### 8bis.6 MVP restrictions and design decisions

| Decision | Detail |
| :---- | :---- |
| Single-level kits | A kit can contain individual products. A kit CANNOT contain another kit. Restriction enforced by Business Rule/Plugin. If nested kits are needed in the future, the schema already supports it, but the explosion code requires deliberate refactoring. |
| Manual tax | No tax engine implemented. tavu\_taxrate is a Decimal field the seller fills manually. For firms needing automatic tax by US state, Avalara/TaxJar integration is added as an external connector in future phases. |
| Discount by amount, not percentage | tavu\_discount is Currency (fixed amount) for simplicity. If percentage discounts are needed, the field is adapted or tavu\_discountpct is added with conversion logic. |
| Margin hidden from sellers | tavu\_totalcost and tavu\_grossmargin are visible only to Operations and Management roles via Field Security Profile. Seller form does not include them. |
| Audit depth | Every change to tavu\_proposalline and tavu\_product is recorded in Dataverse's Audit log. Proposal versions (v1.0, v1.1) are managed by creating new tavu\_proposal records, not overwriting the previous one. |

---

## 9\. End-to-end examples

### Example 1 — Path A: Outbound from networking

**Monday — Microsoft Power Platform Conference:** María (consultant) meets Carlos Méndez (CTO of RetailCorp) at a conference. They briefly discuss Carlos's problem: they need to migrate from Salesforce to Dynamics 365\.

**Monday 11pm — María updates the CRM:**

Account: RetailCorp

\- Customer Tier: Premium

\- Industry: Retail

\- Annual Revenue: $50M

Contact: Carlos Méndez

\- Email: [carlos@retailcorp.com](mailto:carlos@retailcorp.com)

\- Job Title: CTO

\- Account: RetailCorp

\- Engagement Status: Engaged

**Notable:** NO `tavu_lead` was created. María went straight to Contact \+ Account.

**Wednesday — Discovery call:** Carlos confirms 8,000 records to migrate, budget $30K-$50K, Salesforce license expiring in 3 months.

tavu\_opportunity:

\- Topic: "RetailCorp Salesforce → Dynamics 365 Migration"

\- Customer: RetailCorp (Account)

\- Primary Contact: Carlos Méndez

\- Estimated Value: $40,000

\- Estimated Close Date: Aug 8, 2026

\- Sales Stage: Discovery (default seed)

\- Probability: 20% (auto-populated from stage default)

\- statecode: Open

**Week 5 — Proposal created, sent, negotiated (v1.0 → v1.1).**

**Week 7 — Close Won:**

Pop-up:

- Actual Revenue: $47,000 (includes training add-on)  
- Close Notes: "Client signed after adding training module. Project start July 7."

System:

- Creates `tavu_opportunityclose` (Won)  
- Mirrors: actualrevenue, actualclosedate, closenotes → tavu\_opportunity  
- `account.tavu_iscustomer = Yes`  
- Proposal v1.1 → Approved by Client

**Result:** deal won in 6 weeks, full audit trail, $47K vs $40K estimated (+17%).

---

### Example 2 — Path B: Anonymous inbound processed by AI

Email arrives at `info@company.com`:

"Hi, I'm Juan López from GreenTech Solutions, 25 people, evaluating CRM options..."

**System creates tavu\_lead** → Module 3 AI processes (Confidence: 0.72 — below threshold) → status: Awaiting Human Review → María promotes → Contact \+ Account created → opportunity follows Path A.

**Key:** the resulting opportunity has `tavu_sourcelead = [link to original lead]` for full traceability of the inbound source.

---

### Example 3 — Close Lost with captured reason

RetailCorp chooses HubSpot. María clicks "Close Lost":

- Lost Reason: Competitor  
- Close Notes: "Client chose HubSpot for price and native ERP integration. Lesson: validate integrations before proposing Dynamics 365 to accounts with specific ERP stack."

System creates `tavu_opportunityclose` (Lost), mirrors lostreason and closenotes to opportunity. Proposal → Rejected by Client.

**Power BI reports:** Lost Reasons by Quarter, Lost vs Won by Sales Stage, Pipeline by Forecast Category — all derived from clean structured data.

---

### Example 4 — B2C case: Law firm serving an individual

`tavu_customermode = Mixed`

Carolina López contacts the firm for her divorce. María creates her directly as a Contact (no Account — she's an individual).

tavu\_opportunity:

\- Topic: "Carolina López — Divorce Proceedings"

\- Customer: Carolina López (Contact)

\- Account (auto): (empty)

\- Contact (auto): Carolina López

\- Primary Contact: Carolina López (auto — same person)

When closed Won: `contact.tavu_iscustomer = Yes` on Carolina (no Account to mark).

---

## 10\. Queues — Native Dataverse routing

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

## 11\. Recommended configuration by firm type

### Small IT consultancy (12 people)

- Path A only (Path B very rare — almost everything through networking/referrals)  
- 1–2 Customer Tiers (Standard \+ Strategic, no Premium in between)  
- Default seed `tavu_salesstage` (Discovery → Proposal Drafted → Proposal Sent → Negotiation) typically works without modification  
- Max 2–3 proposal versions per typical opportunity

**Result:** very simple, agile model with no administrative overhead.

### Mid-size B2B agency (25 people)

- Both Path A and Path B active (web form receives inbound regularly)  
- 3 Customer Tiers  
- `tavu_salesstage` typically reconfigured with agency-specific vocabulary (e.g., adds "Pitch" between Proposal Drafted and Proposal Sent)  
- Pipeline of 8–12 concurrent active opportunities

**Result:** balanced model between structure and flexibility.

### Software QA boutique (40 people)

- Both paths very active (high inbound volume from forms and referrals)  
- 3–4 Customer Tiers  
- `tavu_salesstage` reconfigured with bid-driven vocabulary (RFI, Evaluation, RFP, Offer, Presentation, Negotiation) — see example in Section 6.3bis  
- Pipeline with 20+ active opportunities  
- Multiple Change Orders per opportunity

**Result:** robust model with high automation.

### Boutique law firm (8 people, B2B \+ B2C hybrid)

- `tavu_customermode = Mixed`  
- Customer Tiers: Standard, Premium (for corporates with recurring services)  
- `tavu_salesstage` typically simplified (e.g., Initial Consultation → Engagement Letter → Negotiation) to match how legal services are sold  
- Some clients are Accounts (corporate legal advisory); many are direct Contacts (divorces, estates, criminal defense)

**Result:** model works for both client types without artificial distinction. Reporting separates naturally by `tavu_account != null` vs `tavu_contact != null`.

---
## 12. Choice colors — Visual design system for OpenTavu

**Purpose:** define a consistent color palette for all Choice fields across OpenTavu's data model. When a consultant opens any OpenTavu form, the same color always means the same thing — they don't need to learn a separate vocabulary per Choice.

This palette is based on Microsoft Fluent UI semantic colors, which guarantees:

- Visual coherence with the broader Power Platform UI
- WCAG AA contrast ratios out of the box
- Consistency across Dataverse views, forms, subgrids, and high-density headers

### 12.1 The canonical palette

| Semantic meaning | Hex code | Fluent reference | Typical use |
|---|---|---|---|
| **Success / Good** | `#107C10` | Communication Green | Engaged, On Track, Won, Met SLA, Active, Solved, Resolved |
| **Neutral / Initial** | `#605E5C` | Neutral Gray 130 | Cold, New, Not Started, Default, Standard, Calm |
| **Caution / Watch** | `#CA5010` | Orange 20 | At Risk, Aging, Awaiting Customer, Pending Review, Concerned, Expedited |
| **Danger / Bad** | `#A4262C` | Red 100 | Inactive, Breached, Lost, Stale, Critical, Cancelled, Frustrated |
| **Info / Process** | `#0078D4` | Communication Blue | In Progress, AI Processing, Categorized, Awaiting Assignment, Promoted |
| **Premium / Strategic** | `#5C2D91` | Purple 20 | Strategic tier, Premium tier (reserved for customer tier signaling) |

### 12.2 Application by Choice field

The following table maps the palette to specific Choice fields already defined elsewhere in this document and in the service-model.md.

| Table | Choice field | Choice value | Color (hex) | Semantic |
|---|---|---|---|---|
| `contact` | `tavu_engagementstatus` | Cold | `#605E5C` | Neutral |
| `contact` | `tavu_engagementstatus` | Engaged | `#107C10` | Success |
| `contact` | `tavu_engagementstatus` | Inactive | `#A4262C` | Danger |
| `account` | `tavu_engagementstatus` | Cold | `#605E5C` | Neutral |
| `account` | `tavu_engagementstatus` | Engaged | `#107C10` | Success |
| `account` | `tavu_engagementstatus` | Inactive | `#A4262C` | Danger |
| `tavu_lead` | `tavu_bufferalert` | Fresh (0-7 days) | `#107C10` | Success |
| `tavu_lead` | `tavu_bufferalert` | Aging (8-14 days) | `#CA5010` | Caution |
| `tavu_lead` | `tavu_bufferalert` | Stale (15+ days) | `#A4262C` | Danger |
| `tavu_case` | `tavu_slastatus` | On Track | `#107C10` | Success |
| `tavu_case` | `tavu_slastatus` | At Risk | `#CA5010` | Caution |
| `tavu_case` | `tavu_slastatus` | Breached | `#A4262C` | Danger |
| `tavu_case` | `tavu_slastatus` | Met | `#107C10` | Success |
| `tavu_case` | `tavu_priority` | Standard | `#605E5C` | Neutral |
| `tavu_case` | `tavu_priority` | Expedited | `#CA5010` | Caution |
| `tavu_case` | `tavu_priority` | Critical | `#A4262C` | Danger |
| `tavu_case` | `tavu_aisentiment` | Calm | `#605E5C` | Neutral |
| `tavu_case` | `tavu_aisentiment` | Concerned | `#CA5010` | Caution |
| `tavu_case` | `tavu_aisentiment` | Frustrated | `#A4262C` | Danger |
| `tavu_case` | `tavu_aisentiment` | Critical | `#A4262C` | Danger |
| `tavu_case` | `tavu_aisentiment` | Unknown | `#605E5C` | Neutral |
| `tavu_salesstage` | `tavu_forecastcategory` | Pipeline | `#605E5C` | Neutral (early, uncommitted) |
| `tavu_salesstage` | `tavu_forecastcategory` | Best Case | `#0078D4` | Info (active engagement) |
| `tavu_salesstage` | `tavu_forecastcategory` | Committed | `#107C10` | Success (late-stage, high confidence) |
| `tavu_salesstage` | `tavu_forecastcategory` | Closed | `#5C2D91` | Premium (terminal, historical) |

### 12.3 Customer tier — special palette

The `tavu_customertier` lookup field references the `tavu_customertierdefinition` table. While not a Choice field per se, the visual treatment of tiers follows a dedicated palette that signals progression rather than status:

| Tier (in `tavu_customertierdefinition.tavu_name`) | Color (hex) | Rationale |
|---|---|---|
| Standard | `#605E5C` | Neutral — default tier, no special treatment |
| Premium | `#0078D4` | Info — elevated relationship, deserves visibility |
| Strategic | `#5C2D91` | Premium — top tier, distinct from operational status colors |

These colors are applied to the Customer Tier pill rendered in the form header and in views via JavaScript on form `OnLoad` (or via a future PCF control). The Customer Tier color does **not** overlap with operational status colors (Success/Caution/Danger), so a tier pill and a status pill can coexist visually without confusion.

### 12.4 Implementation notes

- **Choice colors are configured in the Dataverse modern column designer**, in the color picker for each Choice item.
- The same hex value should be used in light and dark mode — Power Apps automatically adjusts contrast.
- For values that share semantic meaning across tables (e.g., "Engaged" in both Account and Contact), the **same hex must be used** to preserve cross-table visual consistency.
- The palette should be treated as a versioned design system asset. Any change to a hex value should be reflected here first, then propagated to all affected Choice fields.

### 12.5 Out-of-scope variations

These cases are deliberately excluded from this palette:

- **Custom illustrations, marketing assets, or external documentation:** OpenTavu's brand identity (logo, GitHub README, marketing site) uses a separate brand palette, not this functional palette.
- **Power BI dashboards:** color mapping must be done independently in each Power BI report. The Dataverse connector does not propagate Choice colors to Power BI.
- **Charts in model-driven apps:** the chart engine uses its own color sequencing (Microsoft's chart palette). Choice colors do not apply to chart bars/slices automatically.
---

## 13\. Frequently asked questions

**Why isn't `tavu_lead` where the commercial lifecycle lives?**

Because B2B relationships in Professional Services typically do NOT start as anonymous leads. They start as direct contacts (networking, referrals, events). Forcing the "Lead → Qualify → Convert" model introduces friction that causes CRM abandonment (Pain \#1). The Lead only exists as a technical buffer for anonymous inbound.

**Can I disable Path B (tavu\_lead) if I never have anonymous inbound?**

Yes. The `tavu_lead` table can exist empty without affecting anything. If your firm works 100% through networking, simply don't configure the web form or generic email processing. The system will work perfectly with only Path A active.

**Why is "Discovery Notes" a Multiple Lines field and not something more structured?**

Because discovery conversations in Professional Services are inherently narrative, not structured. Trying to impose premature structure (fixed fields for Budget, Authority, Need, Timeline) reduces the quality of captured information. Multiple Lines lets the consultant capture full context, and the future AI Proposal Generator will extract the structured elements it needs from the free text.

**What happens if a Won opportunity is later cancelled (client changes their mind)?**

Reopen the opportunity manually (statecode: Open). Create a new `tavu_opportunityclose` documenting the scenario. The opportunity stays Open until a decision is made to close Lost or re-close Won with different conditions. The audit trail shows both events: original close and reopen.

**How is an upsell on an existing client captured?**

Create a new `tavu_opportunity` with the same Account/Contact. The old (Won) opportunity stays closed. The new one represents the upsell. The "Customer Lifetime Value" report sums all Won opportunities per client.

**Why is `tavu_proposal` separate from `tavu_opportunity`?**

Because one opportunity can have multiple proposals (versions, change orders, related contracts). Mixing document data with deal data in one table would create massive duplication (10 proposal versions \= 10 opportunity records). Separation allows ONE opportunity with a COMPLETE history of proposals.

**Can I migrate opportunities from existing Salesforce/Dynamics to the OpenTavu model?**

Yes. `tavu_opportunity` maps conceptually well with Salesforce/Dynamics Opportunity. Salesforce Sales Process Stages and Dynamics Business Process Flow stages map cleanly to `tavu_salesstage` records (which is precisely why we made the stage configurable — to absorb whatever vocabulary the source CRM used). Lost Reasons map to the Global Choice. Existing proposals can be migrated as `tavu_proposal` records linked to the corresponding opportunity.

**What about a Contact who is an interlocutor but NOT the legal client?**

Carlos Méndez (CTO of RetailCorp) is an interlocutor but NOT the client. The legal client is RetailCorp (Account):

- `account.tavu_iscustomer = Yes` (RetailCorp is the client)  
- `contact.tavu_iscustomer = No` (Carlos is NOT individually marked)  
- `contact.tavu_engagementstatus` can be Engaged (active communication with him)

**How does Customer Mode work if my firm changes from B2B-only to Mixed?**

Change `tavu_systemsettings.tavu_customermode = Mixed`. The form's JavaScript detects the change on the next OnLoad and allows selecting Contacts in the lookup. Existing opportunities and cases (pointing to Accounts) are NOT affected. New records can point to Contacts if the firm decides to serve individuals. Transparent migration with no downtime.

**Why not explode the kit into multiple tavu\_proposallines when selecting it?**

Because it destroys the seller's experience. If the kit explodes into N lines in Dataverse: (a) changing the kit quantity requires editing N lines instead of one, (b) kit discount becomes a math problem distributed across lines, (c) removing the kit from scope requires identifying and deleting N rows. Ephemeral explosion at PDF generation is the correct pattern: one line in the CRM, breakdown only in the document.

**Does the model support proposals in multiple currencies?**

Yes, with one restriction: a proposal uses the currency of the assigned price list (`tavu_pricelist.tavu_currency`). If the firm has clients in COP and USD, create two price lists with their respective amounts. One proposal is one currency. Multi-currency mixing within the same proposal is not supported in MVP.

---

## Document control

| Version | Date | Author | Notes |
| :---- | :---- | :---- | :---- |
| 1.0 | May 6, 2026 | Gustavo González Villani | Initial operational guide. Covers tavu\_lead, account, contact, tavu\_opportunity, tavu\_opportunityclose, tavu\_proposal. |
| 1.1 | May 8, 2026 | Gustavo González Villani | Architectural correction: removed tavu\_lifecyclestage from Contact; added tavu\_iscustomer, tavu\_customersince, tavu\_lastengagementdate to Account; added tavu\_engagementstatus, tavu\_customertier to Contact; adopted hybrid Customer field architecture; added tavu\_systemsettings; expanded to B2B \+ B2C hybrid; added B2C law firm example. |
| 1.2 | May 8, 2026 | Gustavo González Villani | Added complete quotation model (Section 8bis): tavu\_proposalline, tavu\_product, tavu\_uom, tavu\_kitcomponent, tavu\_pricelist \+ tavu\_pricelistitem, tavu\_servicerole. Documented MVP design decisions: single-level kits, ephemeral PDF explosion, manual tax, hidden margin via Field Security Profile, pre-loaded seed data. |
| 1.3 | May 14, 2026 | Gustavo González Villani | Added geographic catalog tables (Section 5.3): tavu_country, tavu_stateprovince, tavu_city. Added tavu_country, tavu_stateprovince, tavu_city lookup fields to account (Section 4.1) and contact (Section 5.1). Added tavu_defaultcountry to tavu_systemsettings (Section 6.4). |
| 1.4 | May 25, 2026 | Gustavo González Villani | Sales pipeline architectural refinement to maximize multi-tenant deployability and prepare data foundation for the AI-Assisted Forecasting roadmap module: (1) **Removed `tavu_engagementtype`** from `tavu_opportunity` schema, form, views, and Section 12.2 color palette — taxonomy that does not address a documented pain point, with most firms defaulting to a single dominant value. (2) **Introduced `tavu_salesstage` configuration table** (Section 6.3bis) with columns `tavu_name`, `tavu_displayorder`, `tavu_defaultprobability`, `tavu_forecastcategory` (Pipeline/Best Case/Committed/Closed), `tavu_isactive`, `tavu_color`, `tavu_notes`. Each firm reconfigures pipeline vocabulary without forking the product. (3) **Simplified `statuscode` on `tavu_opportunity`** to Open/Won/Lost only (Section 6.2). Granular pipeline stage now lives in `tavu_salesstage` lookup. (4) **Added `tavu_salesstage` (Required) and `tavu_probabilityismanual` (Yes/No flag) to `tavu_opportunity`** custom columns (Section 6.3). (5) **Defined Plugin logic for probability defaulting** based on stage default, respecting consultant manual overrides via the flag, with a "Reset to Stage Default" ribbon button. (6) **Updated all examples** (Section 9) and **firm-type configuration recommendations** (Section 11) to reflect the new architecture. (7) **Added forecast category colors** to Section 12.2 palette. (8) Updated Single Lifecycle flow diagram (Section 1) and migration FAQ (Section 13) to reference configurable stages. |
| 1.5 | June 14, 2026 | Gustavo González Villani | Corrected `tavu_uom` schema (Section 8bis.3.1) to match the implemented environment: (1) **Replaced the `tavu_schedule` Choice with the `tavu_unitgroup` lookup** (Display name "Unit Group") → `tavu_unitofmeasureschedule`, Business required — the schedule/group is a configuration table, not an optionset, consistent with `tavu_product.tavu_defaultunitgroup`. (2) **Conversion Factor** changed Optional → Required. (3) **Added the `tavu_isbaseunit` (Yes/No) column** that explicitly flags the root UOM of each group, replacing the implicit "factor = 1" convention. (4) Updated seed data table accordingly. (5) **Added Section 8bis.3.0** documenting the `tavu_unitofmeasureschedule` (Unit groups) table, parent of `tavu_uom`. (6) **Resynced the repo copy** (`C:\Code\OpenTavu\core\docs\sales-model.md`) to this version (previously stale at v1.2). |
| 1.6 | June 14, 2026 | Gustavo González Villani | Currency model correction for the price list (Section 8bis.3.4): (1) **Removed the custom `tavu_currency` field** from `tavu_pricelist` — replaced by the native Dataverse `transactioncurrencyid` (lookup → `transactioncurrency`), selected once at the header. (2) Documented that `transactioncurrencyid` persists on `tavu_pricelist` as a pure currency selector even with no money column on the table. (3) Specified **currency inheritance**: `tavu_pricelistitem` does not auto-inherit currency; a Pre-Operation Create/Update plugin stamps `transactioncurrencyid` from the parent list, and the currency field is not exposed on the line form. Enforces one-currency-per-list. |
| 1.7 | July 3, 2026 | Gustavo González Villani (revision with Claude) | §8.5: framed the **AI Proposal Generator as the top roadmap priority for the proposals area** (proposal/SOW authoring is the costliest, most time-consuming step for senior consultants), with cross-reference to VISION §8, and clarified the AI-first intent (AI drafts, human reviews — not an "ask AI" button). Direction note only; no schema change. Derived from the HubSpot GTM 2026 analysis (evidence in the private triangulation §3.7). |
| 1.8 | July 10, 2026 | Gustavo González Villani (revision with Claude) | Aligned the opportunity close mechanics with the implemented Win/Loss engine. (1) **Removed the ghost `tavu_closetype` field** from `tavu_opportunityclose` (§7.3) — Won/Lost is the activity's own `statuscode` (Won `576600001` / Lost `576600002`); corrected `tavu_lostreason` to a Choice (not lookup) and `tavu_actualclosedate` to DateTime. (2) **Rewrote §7.4** to the "opportunity is source of truth" (Arch B) flow: ribbon commands (Closed as Won / Lost / Reopen) open a custom-page dialog (`tavu_opportunityclosedialog_31702`) that Patches the opportunity's statecode+statuscode; `Pl.Opportunity.LifecycleTracker` (Pre-Op) validates and forces probability; `Pl.Opportunity.CloseOrchestrator` (Post-Op) writes the history log (created Open, then completed) and marks `tavu_iscustomer` on Won. (3) **Deferred engagement-status changes to Module 3** (removed the close-time engagement mutation). (4) **Rewrote §7.5 reopen** to the Reopen button + plugin (re-applies stage default probability; close fields preserved). Full build detail in `docs/opportunity-close-dialog.md`. |
| 1.9 | July 10, 2026 | Gustavo González Villani (revision with Claude) | Implemented the deterministic **proposal lifecycle** (§8). (1) **Corrected §8.3**: `tavu_version` is real (defaulted to `v1`, incremented by Create New Version); removed the ghost `tavu_documenttype`; fixed `tavu_content` → `tavu_proposalcontent`. (2) New **`Pl.Proposal.LifecycleTracker`** (Pre-Op): version default, transition guard, lock (business `tavu_*` fields once Sent/closed), one Approved per opportunity. (3) **`Pl.ProposalLine.Calculator`**: added line-lock when the parent is locked. (4) New **`tavu_CloneProposalVersion` Custom API** (`Pl.Proposal.CloneVersion`): clones header+lines into a new Draft, increments version, supersedes source. (5) New **`tavu_proposal_form.js`**: Send to Client / Mark as Approved (rolls total to the opportunity + offers Close as Won) / Mark as Lost / Create New Version buttons; visual lock; header totals auto-refresh on add/edit (grid OnSave) and add/delete (subgrid addOnLoad row-count) using the modern Power Apps grid (editable). (6) **Standardized autonumber IDs** `OTC/OTO/OTP-{DATETIMEUTC:yyyy}-{SEQNUM:5}` across Case/Opportunity/Proposal. (7) Fixed the opportunity form JS `tavu_customerid` → `tavu_customer`. Full build detail in `docs/proposal-lifecycle.md`. |
| 2.0 | July 29, 2026 | Gustavo González Villani (revision with Claude) | Proposal → client email draft + status-model cleanup. (1) **§8.2 statuses reduced**: `Under Internal Review` and `Awaiting Decision` hidden (reversible); active flow Draft / AI Generated → Sent, button-driven, **Status Reason read-only**. (2) New **`tavu_companyprofile`** (Organization-owned single record) for seller branding (logo/color/profile/terms), with `Pl.CompanyProfile.SingleRecordGuard` + `tavu_companyprofile_open` web resource. (3) New **`tavu_BuildProposalEmailDraft` Custom API** (`Pl.Proposal.BuildEmailDraft`): on Send to Client, builds a client email draft (AI body + branded PDF) and opens it in a modal OOB email dialog; toggle `tavu_systemsettings.tavu_proposalemaildraftenabled` (default on). (4) **Gateway** endpoint `/api/proposal/email-draft` (PdfSharpCore/MigraDocCore, MIT) renders the PDF data-driven from Company Profile — in the gateway, not the net462 plugin sandbox. Full detail in `proposal-lifecycle.md`; strategic record in the product master Decisión 43. |

*This document is the operational reference for OpenTavu's sales model.*