# OpenTavu — View Definitions

## Active and Inactive views for all tables — v1.3

**Purpose:** define the columns, sort order, and filter for every view in the OpenTavu Model-Driven App. Designed for the primary user: a consultant or sales rep at a Professional Services SMB (10–50 people) who values speed, zero-friction data entry, and context at a glance — not administrative overhead.

**Design principles applied:**

- Maximum 7 columns per view (cognitive load limit for list scanning)  
- Leftmost column always the primary identifier the user searches by  
- Status/state column always visible — user needs to know "what state is this in" without opening the record  
- Dates sorted descending by default (most recent first) unless the entity is catalog/config  
- Avoid internal fields (schema names, IDs, system fields) — users never care about those  
- AI fields visible only where they drive a user action (review, assignment) or save the user a click (executive summary on Case views)  
- Mirror fields (`tavu_actualrevenue`, `tavu_lostreason` on opportunity) ARE included — they exist precisely for views  
- Cost / margin fields NEVER appear on seller-facing views (Field Security applies regardless)

---

## USER-FACING TABLES (Pipeline \+ Clients \+ Service)

---

### 1\. Account

**User context:** Sales rep or Ops scans accounts to prioritize outreach, review client portfolio, or onboard a new firm. The key questions: "Is this a client?", "What tier?", "When did we last engage?", "Who owns it?"

#### Active Accounts view

*Filter: `statecode = Active`* *Sort: `tavu_customertier` ascending (Strategic first via sort order), then `name` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Account Name** | `name` | Primary identifier — what the user searches |
| 2 | **Customer Tier** | `tavu_customertier` | Strategic/Premium/Standard — drives priority at a glance |
| 3 | **Primary Contact** | `primarycontactid` |  |
| 4 | **Main Phone** | `telephone1` |  |
| 5 | **City** | `tavu_city` |  |
| 6 | **Email (Primary Contact)** | `tavu_primarycontact.email` |  |
| 7 | **Is Customer** | `tavu_iscustomer` | Yes/No — quick filter: prospect vs active client |
| 8 | **Industry** | `industrycode` | OOTB field — vertical context for targeting |
| 9 | **Customer Since** | `tavu_customersince` | How long they've been a client |
| 10 | **Last Engagement** | `tavu_lastengagementdate` | When was the last touchpoint — staleness signal |
| 11 | **Owner** | `ownerid` | Who is responsible for this account |

**Notes:** Sort Strategic → Premium → Standard using `tavu_customertierdefinition.tavu_sortorder`. This surfaces the most important accounts at the top without manual filtering.

#### Inactive Accounts view

*Filter: `statecode = Inactive`* *Sort: `modifiedon` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Account Name** | `name` |  |
| 2 | **Customer Tier** | `tavu_customertier` | Was this a Strategic client? Re-engagement priority |
| 3 | **Is Customer** | `tavu_iscustomer` | If Yes \+ Inactive \= churned client, high value for win-back |
| 4 | **Customer Since** | `tavu_customersince` | Tenure before going inactive |
| 5 | **Last Engagement** | `tavu_lastengagementdate` | How long has it been silent |
| 6 | **Modified On** | `modifiedon` | When the account was deactivated |
| 7 | **Owner** | `ownerid` |  |

---

### 2\. Contact

**User context:** Consultant opens this daily to find who to call, check if a prospect is still engaged, or identify stale relationships. Key questions: "Who is this person?", "Are they a client?", "Are they engaged?", "Who do they work for?", "How do I reach them?"

#### Active Contacts view

*Filter: `statecode = Active`* *Sort: `tavu_engagementstatus` ascending (Engaged first), then `lastname` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Full Name** | `fullname` | Primary identifier |
| 2 | **Account** | `parentcustomerid` | Company they belong to — B2B context (empty for B2C clients) |
| 3 | **Job Title** | `jobtitle` | Role/seniority — who are we talking to |
| 4 | **Email** | `emailaddress1` | Most-used contact channel — saves opening the record |
| 5 | **Engagement Status** | `tavu_engagementstatus` | Engaged/Cold/Inactive — actionability signal |
| 6 | **Is Customer** | `tavu_iscustomer` | Client vs prospect (relevant for B2C) |
| 7 | **Last Engagement** | `tavu_lastengagementdate` | Recency of last interaction |

**Notes:** Sort Engaged → Cold → Inactive so active relationships surface first. This view is the consultant's daily "who should I follow up with" list. Email is included because in Professional Services the most common action from this list is "send an email to this person" — having it visible avoids opening the record.

#### Inactive Contacts view

*Filter: `statecode = Inactive`* *Sort: `modifiedon` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Full Name** | `fullname` |  |
| 2 | **Account** | `parentcustomerid` |  |
| 3 | **Job Title** | `jobtitle` |  |
| 4 | **Email** | `emailaddress1` | For potential re-engagement campaigns |
| 5 | **Is Customer** | `tavu_iscustomer` | Was this a paying client? |
| 6 | **Customer Since** | `tavu_customersince` |  |
| 7 | **Last Engagement** | `tavu_lastengagementdate` |  |

---

### 3\. Lead (`tavu_lead`)

**User context:** Sales rep reviews inbound leads that the AI flagged for human review. The primary action is "promote or discard." Key questions: "Who is this?", "Where did they come from?", "What did the AI recommend?", "How long have they been waiting?"

#### Active Leads view

*Filter: `statecode = Active`* *Sort: `statuscode` ascending (Manual Review Required first), then `createdon` ascending (oldest first)*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Subject** | `tavu_subject` | What was the inquiry about |
| 2 | **Status Reason** | `statuscode` | New / AI Processing / Awaiting Human Review / Manual Review Required — what needs to happen |
| 3 | **Full Name** | `tavu_firstname` \+ `tavu_lastname` | Who sent it (use a calculated column or include both) |
| 4 | **Company** | `tavu_companyname` | Raw company name extracted |
| 5 | **Source** | `tavu_source` | Web Form / Email / LinkedIn / Partner Referral — channel context |
| 6 | **AI Confidence** | `tavu_aiconfidencescore` | 0–1 score — drives urgency of human review |
| 7 | **Days in Buffer** | `tavu_daysinbuffer` | How long it has been waiting — staleness alert |
| 8 | **Created On** | `createdon` |  |

**Notes:** This view is the AI review queue. Sorting by `statuscode` first surfaces Manual Review Required and Awaiting Human Review records before AI Processing. Oldest-first secondary sort ensures nothing is forgotten. Conditional formatting on `tavu_daysinbuffer` (\>7 days \= amber, \>14 days \= red) makes staleness immediately visible.

**Recommended secondary view — Needs Human Decision:** *Filter: `statecode = Active` AND `statuscode IN (Awaiting Human Review, Manual Review Required)`* *Sort: `tavu_daysinbuffer` descending*

Same columns as Active Leads. This is the daily triage queue — the sales rep's "inbox" for AI escalations.

#### Inactive Leads view

*Filter: `statecode = Inactive`* *Sort: `modifiedon` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Subject** | `tavu_subject` |  |
| 2 | **Status Reason** | `statuscode` | Promoted to Contact / Discarded as Noise / Duplicate / Not Qualified / Stale — why it went inactive |
| 3 | **First/Last Name** | `tavu_firstname` \+ `tavu_lastname` |  |
| 4 | **Company** | `tavu_companyname` |  |
| 5 | **Source** | `tavu_source` |  |
| 6 | **Promoted Contact** | `tavu_promotedcontact` | If promoted, who did it become — traceability |
| 7 | **Modified On** | `modifiedon` | When the decision was made |

---

### 4\. Opportunity (`tavu_opportunity`)

**User context:** Sales rep and Sales Manager — this is the pipeline view. The most critical view in the system. Key questions: "What's in the pipe?", "What stage is each deal?", "What's the value?", "When does it close?", "Who is the client?", "What tier?"

#### Active Opportunities view (Open)

*Filter: `statecode = Active`* *Sort: `tavu_customertier` ascending (Strategic first), then `tavu_estimatedclosedate` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Topic** | `tavu_topic` | Deal name — what is this opportunity |
| 2 | **Customer** | `tavu_customer` | Account or Contact — who is the client (polymorphic) |
| 3 | **Customer Tier** | `tavu_customertier` | Denormalized from Account/Contact — Strategic deals surface first |
| 4 | **Sales Stage** | `tavu_salesstage` | Per-firm pipeline stage (Discovery / Proposal Drafted / Proposal Sent / Negotiation …), from the `tavu_salesstage` config table |
| 5 | **Est. Revenue** | `tavu_estimatedrevenue` | Deal size — drives prioritization |
| 6 | **Est. Close Date** | `tavu_estimatedclosedate` | Urgency signal |
| 7 | **Owner** | `ownerid` | Who is working this deal |

**Notes:** This view answers the Sales Manager's "what is our pipeline today?" in one glance. `statecode = Active` means Open (statuscode is only Open/Won/Lost; the granular pipeline stage lives in `tavu_salesstage`). The dual sort (tier first, then close date) surfaces Strategic near-closing deals first. Probability is omitted from the default view (unreliable in early-stage SMBs); it lives on the form.

**Recommended secondary view — My Open Opportunities:** *Filter: `statecode = Active` AND `ownerid = current user`* *Sort: `tavu_estimatedclosedate` ascending*

Same columns minus `ownerid`. The seller's personal pipeline.

**Recommended secondary view — Pipeline by Sales Stage:** *Filter: `statecode = Active`* *Sort: `tavu_salesstage` ascending (by display order), then `tavu_estimatedrevenue` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Topic** | `tavu_topic` |  |
| 2 | **Customer** | `tavu_customer` |  |
| 3 | **Sales Stage** | `tavu_salesstage` | Group/sort by stage for a funnel-style read |
| 4 | **Est. Revenue** | `tavu_estimatedrevenue` |  |
| 5 | **Probability** | `tavu_probability` | For weighted forecasting (defaulted per stage) |
| 6 | **Est. Close Date** | `tavu_estimatedclosedate` |  |
| 7 | **Owner** | `ownerid` |  |

Supports forecast reporting by stage and its Forecast Category (Pipeline / Best Case / Committed) configured on `tavu_salesstage`.

#### Won Opportunities view

*Filter: `statecode = Inactive` AND `statuscode = Won`* *Sort: `tavu_actualclosedate` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Topic** | `tavu_topic` |  |
| 2 | **Customer** | `tavu_customer` |  |
| 3 | **Actual Revenue** | `tavu_actualrevenue` | What we actually closed — not estimate (mirror field) |
| 4 | **Customer Tier** | `tavu_customertier` | Which tier are wins coming from |
| 5 | **Sales Stage** | `tavu_salesstage` | Last stage before close (historical reference) |
| 6 | **Actual Close Date** | `tavu_actualclosedate` | When it closed |
| 7 | **Owner** | `ownerid` |  |

#### Lost Opportunities view

*Filter: `statecode = Inactive` AND `statuscode = Lost`* *Sort: `tavu_actualclosedate` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Topic** | `tavu_topic` |  |
| 2 | **Customer** | `tavu_customer` |  |
| 3 | **Lost Reason** | `tavu_lostreason` | Price / Competitor / Timing / No Decision — why we lost (mirror field) |
| 4 | **Est. Revenue** | `tavu_estimatedrevenue` | How much value was lost |
| 5 | **Sales Stage** | `tavu_salesstage` | Stage reached before losing |
| 6 | **Actual Close Date** | `tavu_actualclosedate` |  |
| 7 | **Owner** | `ownerid` |  |

---

### 5\. Opportunity Close (`tavu_opportunityclose`) — Activity

**User context:** This is an Activity Type, not a primary entity. Users rarely browse it as a list — they see closure records in the timeline of the parent opportunity. However, an admin/manager view is useful for audit and reporting.

#### Recent Closures view

*Filter: no state filter (activities use `statecode = Completed`)* *Sort: `actualstart` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Subject** | `subject` (OOTB) | "Closed Won — RetailCorp Migration" etc. |
| 2 | **Close Type** | `tavu_closetype` | Won / Lost — dual-state indicator |
| 3 | **Regarding (Opportunity)** | `regardingobjectid` | Link back to the parent deal |
| 4 | **Actual Revenue** | `tavu_actualrevenue` | Filled when Won |
| 5 | **Lost Reason** | `tavu_lostreason` | Filled when Lost |
| 6 | **Activity Date** | `actualstart` | When close happened |
| 7 | **Resource** | `tavu_resource` | Who closed it |

**Notes:** This view is for Sales Manager / Ops audit. Regular sellers never need it — they see closures in the opportunity timeline. Active vs Inactive distinction doesn't apply meaningfully here; consider a single "Recent Closures" view filtered to last 90 days for performance.

---

### 6\. Proposal (`tavu_proposal`)

**User context:** Sales rep tracks proposals in flight. Key questions: "Which proposals are waiting on a client decision?", "Which are still in draft?", "Which version is current?", "What's the total value?"

#### Active Proposals view

*Filter: `statecode = Active`* *Sort: `statuscode` descending (Awaiting Decision/Sent first), then `tavu_expecteddecisiondate` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` | Proposal name \+ version |
| 2 | **Opportunity** | `tavu_opportunity` | Which deal this belongs to |
| 3 | **Customer** | `tavu_customer` | Account or Contact (inherited from opportunity) |
| 4 | **Status Reason** | `statuscode` | Draft / AI Generated — Awaiting Review / Under Internal Review / Sent to Client / Awaiting Decision |
| 5 | **Version** | `tavu_version` | Current iteration (v1, v2 …) |
| 6 | **Total** | `tavu_total` | Full value including tax (rollup from lines) |
| 7 | **Expected Decision** | `tavu_expecteddecisiondate` | When client should decide — drives follow-up |

**Notes:** "Awaiting Decision" proposals sorted by expected decision date creates a natural follow-up list. Sales rep sees at a glance which proposals need a nudge today. Total is a calculated field — visible to seller (subtotal \+ tax). `tavu_totalcost` and `tavu_grossmargin` are protected by Field Security and never appear here.

#### Inactive Proposals view

*Filter: `statecode = Inactive`* *Sort: `modifiedon` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` |  |
| 2 | **Opportunity** | `tavu_opportunity` |  |
| 3 | **Customer** | `tavu_customer` |  |
| 4 | **Status Reason** | `statuscode` | Approved by Client / Rejected by Client / Superseded / Withdrawn — outcome |
| 5 | **Version** | `tavu_version` |  |
| 6 | **Total** | `tavu_total` | How much was this worth |
| 7 | **Sent Date** | `tavu_sentdate` | When it was sent — historical context |

---

### 7\. Proposal Line (`tavu_proposalline`)

**User context:** This is a child grid inside the Proposal form, not a standalone list users browse. However, an "All Proposal Lines" view is useful for product-mix and discount analysis by Ops Manager.

#### Active Proposal Lines view (for Ops reporting)

*Filter: no special filter (child records typically follow parent state)* *Sort: `tavu_proposalid` ascending, then `createdon` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Proposal** | `tavu_proposalid` | Which proposal this line belongs to |
| 2 | **Product** | `tavu_productid` | What was sold (KIT badge for kits via JS) |
| 3 | **Quantity** | `tavu_quantity` |  |
| 4 | **Unit of Measure** | `tavu_uomid` | Hour / Day / License / Unit |
| 5 | **Price Per Unit** | `tavu_priceperunit` | Sell price |
| 6 | **Discount** | `tavu_discount` | Discount amount in currency |
| 7 | **Total** | `tavu_total` | Calculated: subtotal \+ tax − discount |

**Notes:** Cost-related columns (`tavu_unitcost`, `tavu_linecost`) are hidden from this view because the audience is typically the seller working on the proposal grid. An Ops-only view in the Configuration area can include them, protected by Field Security Profile.

---

### 8\. Case (`tavu_case`)

**User context:** This is the most operationally critical view. Consultants and managers open this multiple times per day. Key questions: "What needs my attention right now?", "Is the SLA at risk?", "What did the AI say?", "Who is the client and what tier are they?"

#### Active Cases view

*Filter: `statecode = Active`* *Sort: `tavu_priority` ascending (Critical first), then `tavu_responsetargetdate` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Title** | `tavu_title` | What the case is about |
| 2 | **Customer** | `tavu_customer` | Who reported it (Account or Contact) |
| 3 | **Priority** | `tavu_priority` | Critical / Expedited / Standard — most important sort key |
| 4 | **Status Reason** | `statuscode` | New / AI Processing / Categorized — Awaiting Assignment / In Progress / Manual Review Required / Waiting on Customer |
| 5 | **Type** | `tavu_type` | Support / RFP / Complaint / Billing — what kind of work |
| 6 | **SLA Status** | `tavu_slastatus` | On Track / At Risk / Breached / Met — the alert column |
| 7 | **Response Target** | `tavu_responsetargetdate` | When first response is due — urgency |
| 8 | **Resolution Target** | `tavu_resolutiontargetdate` | When resolution is due — the second SLA deadline |
| 9 | **Owner** | `ownerid` | Who is handling it — assignment visibility for managers |

**Notes:** This view is designed for triage. Critical \+ Breached cases rise to the top through the dual sort. The SLA Status column is the most important operational signal — conditional color formatting strongly recommended: Breached \= red, At Risk \= amber, On Track \= green, Met \= grey/blue. Owner and Resolution Target were added (v1.2): Owner gives managers assignment visibility, Resolution Target shows the second deadline. This brings the view to 9 columns — a deliberate exception to the 7-column guideline, justified because Active Cases is the team's operational hub.

**Note on `tavu_aisummary`:** the one-line AI executive summary is extremely valuable, but at 500 chars it doesn't fit cleanly as a column in the default list view (would either truncate badly or push other columns off). Two options:

1. Keep `tavu_aisummary` off the default view; rely on hover/quick-view or the form for full context.  
2. Add a supplementary "Active Cases — Detailed" view that swaps `tavu_responsetargetdate` for `tavu_aisummary`, for users who prefer AI-first scanning.

Recommend option 2 as an alternative view, not as the default.

**Recommended secondary view — Active Cases (AI Summary):** *Filter: `statecode = Active`* *Sort: `tavu_priority` ascending, then `tavu_responsetargetdate` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Title** | `tavu_title` |  |
| 2 | **Customer** | `tavu_customer` |  |
| 3 | **Priority** | `tavu_priority` |  |
| 4 | **AI Summary** | `tavu_aisummary` | One-line executive summary — saves opening the record |
| 5 | **AI Sentiment** | `tavu_aisentiment` | Calm / Concerned / Frustrated / Critical — emotion signal |
| 6 | **SLA Status** | `tavu_slastatus` |  |
| 7 | **Response Target** | `tavu_responsetargetdate` |  |

**Recommended secondary view — My Open Cases:** *Filter: `statecode = Active` AND `ownerid = current user`* *Sort: `tavu_priority` ascending, then `tavu_responsetargetdate` ascending*

Same columns as Active Cases, minus Owner (implied by the filter) — the individual consultant's personal queue.

**Recommended secondary view — Needs Review — Cases (AI flagged):** *Filter: `statecode = Active` AND `statuscode = Manual Review Required`* *Sort: `createdon` ascending (oldest first — been waiting longest)*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Title** | `tavu_title` | What the case is about |
| 2 | **Customer** | `tavu_customer` | Who reported it |
| 3 | **Subcategory** | `tavu_subcategory` | The AI's most specific classification — this queue exists to validate it (implies its Category and Business Line) |
| 4 | **AI Confidence** | `tavu_aiconfidencescore` | Why it needs review — low confidence |
| 5 | **AI Summary** | `tavu_aisummary` | What the AI understood — saves opening the record |
| 6 | **AI Sentiment** | `tavu_aisentiment` | Critical/Frustrated cases deserve priority even in the review queue |
| 7 | **Multi-Intent Detected** | `tavu_multiintentdetected` | Flags cases that contain several requests and need splitting |
| 8 | **Created On** | `createdon` | How long it has been waiting |

**Notes:** This is the AI-validation queue, not an operational triage list. Cases reach `Manual Review Required` immediately after AI Processing — **before** assignment and SLA calculation — so Owner, SLA Status, and the target dates are typically empty here and were deliberately excluded. The columns instead expose the AI's interpretation (Subcategory, Confidence, Summary, Sentiment, Multi-Intent) so a human can confirm, correct, or split. `Multi-Intent Detected = Yes` is the cue to split one case into several (each may carry a different Type, SLA, and owner).

> **Note (schema reality, v1.3):** `tavu_case` is a custom table, so `statecode` has only **Active / Inactive** — there is no separate Resolved or Cancelled *state*. "Resolved" and "Cancelled" are **status-reason groups inside the Inactive state**. The three closed-case views below therefore filter on `statuscode`, not `statecode`.

#### Inactive Cases view (all closed)

*Filter: `statecode = Inactive`* *Sort: `modifiedon` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Title** | `tavu_title` |  |
| 2 | **Customer** | `tavu_customer` |  |
| 3 | **Type** | `tavu_type` |  |
| 4 | **Status Reason** | `statuscode` | The outcome — distinguishes resolved vs cancelled reasons in one list |
| 5 | **SLA Status** | `tavu_slastatus` | Met vs Breached — compliance |
| 6 | **Actual Hours** | `tavu_actualhours` | Effort for billing / capacity |
| 7 | **Modified On** | `modifiedon` | When it was closed — works for both resolved and cancelled |

**Notes:** Single combined view of every closed case. `Modified On` is the close date because cancelled cases have no `Resolution Date`; the Status Reason column carries the outcome. Use this when one closed list is enough; use the Resolved / Cancelled views below when compliance reporting needs the split.

#### Resolved Cases view

*Filter: `statecode = Inactive` AND `statuscode IN (Solved, Information Provided, Duplicate, Out of Scope)`* *Sort: `tavu_resolutiondate` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Title** | `tavu_title` |  |
| 2 | **Customer** | `tavu_customer` |  |
| 3 | **Type** | `tavu_type` |  |
| 4 | **Status Reason** | `statuscode` | Solved / Information Provided / Duplicate / Out of Scope |
| 5 | **SLA Status** | `tavu_slastatus` | Met vs Breached — compliance reporting |
| 6 | **Actual Hours** | `tavu_actualhours` | Effort for billing and capacity analysis |
| 7 | **Resolution Date** | `tavu_resolutiondate` | When it was resolved |

#### Cancelled Cases view

*Filter: `statecode = Inactive` AND `statuscode IN (Cancelled by Customer, Cannot Reproduce, Closed without Resolution)`* *Sort: `modifiedon` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Title** | `tavu_title` |  |
| 2 | **Customer** | `tavu_customer` |  |
| 3 | **Status Reason** | `statuscode` | Cancelled by Customer / Cannot Reproduce / Closed without Resolution |
| 4 | **Type** | `tavu_type` |  |
| 5 | **Modified On** | `modifiedon` | When it was cancelled |

---

### 9\. Time Entry (`tavu_timeentry`) — Activity

**User context:** Activity Type used to record hours worked on cases and opportunities. Users see time entries inside the timeline of the parent record, but managers benefit from list views for utilization and billing.

#### My Open Time Entries view

*Filter: `statecode = Open` AND `ownerid = current user`* *Sort: `actualstart` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Subject** | `subject` (OOTB) | Description of the work |
| 2 | **Regarding** | `regardingobjectid` | Case or Opportunity this entry belongs to |
| 3 | **Hours** | `tavu_hours` | How much time was logged |
| 4 | **Work Type** | `tavu_worktype` | Billable Work / Travel / Admin / Internal |
| 5 | **Is Billable** | `tavu_isbillable` | Yes/No |
| 6 | **Status Reason** | `statuscode` | Draft / Submitted / Approved |
| 7 | **Activity Date** | `actualstart` | When work was done |

#### Submitted Time Entries view (for approval)

*Filter: `statecode = Open` AND `statuscode = Submitted`* *Sort: `actualstart` ascending*

Same columns. This is the approver's queue.

#### Completed Time Entries view

*Filter: `statecode = Completed` (Approved)* *Sort: `actualstart` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Subject** | `subject` |  |
| 2 | **Regarding** | `regardingobjectid` |  |
| 3 | **Owner** | `ownerid` | Who logged it (for utilization reports) |
| 4 | **Hours** | `tavu_hours` |  |
| 5 | **Is Billable** | `tavu_isbillable` |  |
| 6 | **Activity Date** | `actualstart` |  |

**Notes:** Exact field names for `tavu_timeentry` (`tavu_hours`, `tavu_worktype`, etc.) should be verified against the actual service-model spec; this view assumes standard activity fields plus the documented custom columns from service-model.md Section 8\.

---

### 10\. Knowledge Article (`tavu_knowledge_article`)

**User context:** Consultants reference articles to answer support cases consistently. Key questions: "Is there an article that covers this?", "Is it current?", "Who owns updates?"

#### Active Knowledge Articles view

*Filter: `statecode = Active` (Published)* *Sort: `modifiedon` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Title** | `tavu_title` (or `tavu_name`) | What the article covers |
| 2 | **Category** | `tavu_category` | Topical grouping |
| 3 | **Status Reason** | `statuscode` | Published / Approved |
| 4 | **Owner** | `ownerid` | Who is responsible for keeping it current |
| 5 | **Modified On** | `modifiedon` | Recency — outdated articles drift toward inaccuracy |

#### Inactive Knowledge Articles view

*Filter: `statecode = Inactive` (Draft, Archived, Deprecated)* *Sort: `modifiedon` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Title** | `tavu_title` |  |
| 2 | **Category** | `tavu_category` |  |
| 3 | **Status Reason** | `statuscode` | Draft / Archived / Deprecated |
| 4 | **Owner** | `ownerid` |  |
| 5 | **Modified On** | `modifiedon` |  |

**Notes:** The knowledge article entity is mentioned in VISION\_v0.7.md Section 8 but its detailed field-level spec is not in sales-model.md or service-model.md. View definitions above are conservative; refine once the table specification is finalized. Consider adding `tavu_viewcount` or `tavu_helpfulnesscount` if those are implemented later (relevance signals).

---

## CATALOG / CONFIGURATION TABLES

*These tables are in the Configuration area. Users are Ops Managers or Admins — not daily sales/service users. Views need completeness over speed.*

---

### 11\. Customer Tier Definition (`tavu_customertierdefinition`)

#### Active Customer Tiers view

*Filter: `statecode = Active`* *Sort: `tavu_sortorder` ascending (Strategic \= 10 first)*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` | Standard / Premium / Strategic |
| 2 | **Sort Order** | `tavu_sortorder` | Defines priority order across the system |
| 3 | **Display Color** | `tavu_displaycolor` | Hex code for visual identification in lists/forms |
| 4 | **Description** | `tavu_description` | What qualifies a client for this tier |

**Notes:** 4 columns — this is a simple catalog. Admin needs to see the name, the order, the color, and the definition.

#### Inactive Customer Tiers view

*Filter: `statecode = Inactive`* *Sort: `tavu_name` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` |  |
| 2 | **Status Reason** | `statuscode` | Deprecated / Replaced |
| 3 | **Sort Order** | `tavu_sortorder` |  |
| 4 | **Description** | `tavu_description` |  |

---

### 12\. Case Type (`tavu_casetype`)

#### Active Case Types view

*Filter: `statecode = Active`* *Sort: `tavu_sortorder` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` | General Inquiry / Support Request / RFP / Complaint etc. |
| 2 | **Code** | `tavu_code` | GEN / SUP / RFP / CMP — short reference |
| 3 | **Default Priority** | `tavu_defaultpriority` | Standard / Expedited / Critical |
| 4 | **Is Default** | `tavu_isdefault` | Which type AI uses as fallback |
| 5 | **Default Owner Team** | `tavu_defaultownerteam` | Which queue gets this type by default |
| 6 | **Display Color** | `tavu_displaycolor` | Visual identification |

**Notes:** Admin needs to see at a glance: what types exist, what priority they carry, which queue handles them, and which is the AI fallback. The `tavu_aihint` field is not in the view because it's typically long text — visible on the form, not the list.

#### Inactive Case Types view

*Filter: `statecode = Inactive`* *Sort: `tavu_name` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` |  |
| 2 | **Status Reason** | `statuscode` | Deprecated / Replaced |
| 3 | **Code** | `tavu_code` |  |
| 4 | **Default Priority** | `tavu_defaultpriority` |  |

---

### 13\. SLA Definition (`tavu_sla`)

#### Active SLA Definitions view

*Filter: `statecode = Active`* *Sort: `tavu_customertier` (Strategic first via sort order), then `tavu_evaluationpriority` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` | "Strategic \- Complaint" etc. |
| 2 | **Customer Tier** | `tavu_customertier` | Which tier does this apply to |
| 3 | **Case Type** | `tavu_casetype` | Which type (empty \= default for tier) |
| 4 | **Response Target (hrs)** | `tavu_responsetargethours` | Hours to first response |
| 5 | **Resolution Target (hrs)** | `tavu_resolutiontargethours` | Hours to resolution |
| 6 | **Coverage** | `tavu_coveragehours` | 24x7 / Business Hours 8x5 / Extended Hours 12x5 |
| 7 | **Eval Priority** | `tavu_evaluationpriority` | Match order — 50 (specific) before 100 (default) |

**Notes:** This view is the SLA matrix at a glance. Admin can read the entire service contract policy in one view without opening individual records. This is the most important configuration view in the system because it directly defines the firm's implicit service contract.

#### Inactive SLA Definitions view

*Filter: `statecode = Inactive`* *Sort: `tavu_name` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` |  |
| 2 | **Status Reason** | `statuscode` | Deprecated / Replaced |
| 3 | **Customer Tier** | `tavu_customertier` |  |
| 4 | **Case Type** | `tavu_casetype` |  |
| 5 | **Response Target (hrs)** | `tavu_responsetargethours` |  |
| 6 | **Resolution Target (hrs)** | `tavu_resolutiontargethours` |  |

---

### 14\. Product (`tavu_product`)

#### Active Products view

*Filter: `statecode = Active`* *Sort: `tavu_iskit` descending (kits first), then `tavu_name` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` | Consulting Hour / Cloud Migration Kit etc. |
| 2 | **Is Kit** | `tavu_iskit` | Yes/No — kits need special handling |
| 3 | **Default Unit** | `tavu_defaultunit` | Hour / Day / License / Unit |
| 4 | **Cost** | `tavu_cost` | Internal unit cost — Field Security: visible to Ops/Admin only |
| 5 | **Description** | `tavu_description` | Commercial description |

**Notes:** Cost is visible only to Operations Manager / Sales Manager via Field Security Profile — not to the regular seller. The Configuration area where this view lives has Ops/Admin as audience, so the column is included; on any seller-facing list, the field is hidden by Field Security regardless of whether the view definition includes it.

#### Inactive Products view

*Filter: `statecode = Inactive`* *Sort: `tavu_name` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` |  |
| 2 | **Is Kit** | `tavu_iskit` |  |
| 3 | **Status Reason** | `statuscode` | Discontinued / Replaced |
| 4 | **Default Unit** | `tavu_defaultunit` |  |

---

### 15\. Price List (`tavu_pricelist`)

#### Active Price Lists view

*Filter: `statecode = Active`* *Sort: `tavu_isdefault` descending (default first), then `tavu_name` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` | "Standard USD Rate 2026" etc. |
| 2 | **Currency** | `tavu_currency` | USD / COP / EUR |
| 3 | **Is Default** | `tavu_isdefault` | Which auto-selects in new proposals |
| 4 | **Effective Date** | `tavu_effectivedate` | When it started |
| 5 | **Expiration Date** | `tavu_expirationdate` | When it expires — alert if past due |

#### Inactive Price Lists view

*Filter: `statecode = Inactive`* *Sort: `tavu_expirationdate` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` |  |
| 2 | **Currency** | `tavu_currency` |  |
| 3 | **Effective Date** | `tavu_effectivedate` |  |
| 4 | **Expiration Date** | `tavu_expirationdate` | Why it became inactive |

---

### 16\. Price List Item (`tavu_pricelistitem`)

#### Active Price List Items view

*Filter: `statecode = Active` (if applicable) or no filter* *Sort: `tavu_pricelistid` ascending, then `tavu_productid` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Price List** | `tavu_pricelistid` | Which list this item belongs to |
| 2 | **Product** | `tavu_productid` | What product is priced |
| 3 | **Price Per Unit** | `tavu_priceperunit` | The sell price |
| 4 | **Quantity** | `tavu_quantity` | For volume-tiered pricing |

**Notes:** This is a junction table. The view is mainly used during setup to verify the price matrix is complete. Sorting by Price List \+ Product makes it easy to see gaps (product not yet priced in a specific list).

---

### 17\. Service Role (`tavu_servicerole`)

#### Active Service Roles view

*Filter: `statecode = Active` (if applicable)* *Sort: `tavu_defaultrate` descending (highest rate first)*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` | Senior Architect / Junior Consultant / PM etc. |
| 2 | **Default Rate** | `tavu_defaultrate` | Bill rate per hour |
| 3 | **Cost Rate** | `tavu_costrate` | Internal cost per hour — Field Security: Ops/Admin only |
| 4 | **Description** | `tavu_description` | What this role covers |

**Notes:** Cost Rate protected by Field Security Profile — only Ops Manager and Sales Manager see it. Regular sellers see Default Rate only. Even though this view shows the column, Field Security masks it for unauthorized roles.

---

## BACK-END / JUNCTION TABLES

*Kit Component and UOM are pure configuration. They don't need elaborate views — admins access them during setup only.*

---

### 18\. Kit Component (`tavu_kitcomponent`)

#### All Kit Components view

*No state filter — these records don't have a meaningful Active/Inactive lifecycle* *Sort: `tavu_parentproductid` ascending, then `tavu_quantity` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Parent Product (Kit)** | `tavu_parentproductid` | Which kit this component belongs to |
| 2 | **Child Product** | `tavu_childproductid` | The component inside the kit |
| 3 | **Quantity** | `tavu_quantity` | How many units of the component |
| 4 | **Unit of Measure** | `tavu_uomid` | Hour / License / Unit |

**Notes:** Grouping by Parent Product in the view makes the BOM readable at a glance. Admin can verify "Cloud Migration Kit \= 40 Hours \+ 2 Licenses" without opening each record.

---

### 19\. Unit of Measure (`tavu_uom`)

#### Active UOMs view

*Filter: `statecode = Active` (if applicable)* *Sort: `tavu_schedule` ascending, then `tavu_conversionfactor` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` | Hour / Day / Month / License / Unit |
| 2 | **Schedule** | `tavu_schedule` | Time / Software / General grouping |
| 3 | **Conversion Factor** | `tavu_conversionfactor` | 1 Day \= 8 Hours — setup validation |

---

### 20\. System Settings (`tavu_systemsettings`)

**User context:** Single-record table holding tenant-wide configuration (customer mode, AI thresholds, etc.). Admin opens directly, not via a list. Still, a view is required.

#### Active Settings view

*Filter: `statecode = Active`* *Sort: `createdon` descending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` (or `tavu_recordname`) | "Organization Settings" — typically one record |
| 2 | **Customer Mode** | `tavu_customermode` | B2B\_Only / B2C\_Only / Mixed |
| 3 | **AI Confidence Threshold** | `tavu_aiconfidencethreshold` | Default 0.85 — surface as configuration |
| 4 | **Modified On** | `modifiedon` | When last reconfigured |

**Notes:** This table typically holds a single record (Organization-owned). The view exists to confirm the record exists and to inspect current configuration. Exact column names may differ — refine when the system\_settings spec is finalized.

---

### 21\. Business Line / Category / Subcategory (`tavu_businessline`, `tavu_category`, `tavu_subcategory`)

**User context:** Cascading classification used by cases (Business Line → Category → Subcategory). Admins configure these during setup.

#### Active Business Lines view

*Filter: `statecode = Active`* *Sort: `tavu_sortorder` ascending (if applicable), then `tavu_name` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` | IT Consulting / Marketing / Legal etc. |
| 2 | **Code** | `tavu_code` | Short reference |
| 3 | **Description** | `tavu_description` | What this business line covers |

#### Active Categories view

*Filter: `statecode = Active`* *Sort: `tavu_businessline` ascending, then `tavu_name` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` | Technical Issue / Billing / Strategy etc. |
| 2 | **Business Line** | `tavu_businessline` | Parent grouping |
| 3 | **Description** | `tavu_description` |  |

#### Active Subcategories view

*Filter: `statecode = Active`* *Sort: `tavu_category` ascending, then `tavu_name` ascending*

| \# | Column | Field | Why |
| :---- | :---- | :---- | :---- |
| 1 | **Name** | `tavu_name` | Server Down / Login Issue / Slow Performance etc. |
| 2 | **Category** | `tavu_category` | Parent grouping |
| 3 | **Description** | `tavu_description` |  |

**Notes:** These three tables form a cascading hierarchy. Field-level spec for these tables is not yet detailed in service-model.md beyond their existence — refine once specification is finalized. Inactive views follow the same pattern, sorted by `modifiedon` descending.

---

## VIEW IMPLEMENTATION NOTES

### How to create views in the App Designer

For each table above:

1. In the App Designer, navigate to the table in the Pages panel  
2. Click the table → **Views** tab in the right panel  
3. Select existing views to include OR click **Manage views** to create new ones in the classic editor  
4. In the classic view editor: **\+ Add column** → select from the fields listed above  
5. Set **Sort by** as specified  
6. Set **Filter** in the **Edit Filter** pane

### Naming convention for views

| Pattern | Example |
| :---- | :---- |
| `Active {TableName}` | Active Accounts |
| `Inactive {TableName}` | Inactive Accounts |
| `Won Opportunities` | supplementary view |
| `Lost Opportunities` | supplementary view |
| `Needs Review — Cases` | AI triage queue view |
| `My Open Cases` | personal queue view |
| `My Open Opportunities` | personal pipeline view |
| `Resolved Cases` | historical view |
| `Cancelled Cases` | historical view |
| `Recent Closures` | activity audit view |
| `Active Cases (AI Summary)` | alternative default view |

### Field Security reminder

These fields MUST be hidden from the seller role via Field Security Profile — they appear in Configuration area views (Ops/Admin audience) but never in seller-facing views:

- `tavu_cost` on Product  
- `tavu_unitcost` on Proposal Line  
- `tavu_linecost` on Proposal Line  
- `tavu_costrate` on Service Role  
- `tavu_totalcost` on Proposal  
- `tavu_grossmargin` on Proposal

Field Security is enforced at the platform level — even if a column is in a view's definition, unauthorized users see it blanked. The view definition still matters because including a protected column in a seller view causes confusion (a blank column with a label).

### Priority order to build views

Build in this order based on daily user impact:

1. **Active Cases** — most operationally critical, used multiple times per day  
2. **Active Opportunities** — pipeline management, used daily by sales  
3. **Active Contacts** — daily relationship tracking  
4. **Active Leads** \+ **Needs Human Decision** — AI review queue, time-sensitive  
5. **Active Accounts** — weekly portfolio review  
6. **Active Proposals** — weekly follow-up tracking  
7. **My Open Cases** \+ **My Open Opportunities** — personal queues  
8. **Needs Review — Cases** \+ **Active Cases (AI Summary)** — AI-augmented views  
9. **Won Opportunities** \+ **Lost Opportunities** — historical reporting  
10. **Resolved Cases** \+ **Cancelled Cases** — historical reporting  
11. All Inactive views for the user-facing tables (\#1–10 above)  
12. All Configuration table views (Customer Tier, Case Type, SLA, Product, Price List, Service Role, UOM, Kit Component)  
13. **Knowledge Article**, **System Settings**, **Business Line/Category/Subcategory** views (refine when field-level specs are finalized)  
14. **Recent Closures** (Opportunity Close activity audit)  
15. **Time Entry** views (My Open / Submitted / Completed)

### Conditional formatting recommendations

Native model-driven app conditional formatting is limited; the following are suggestions for what would benefit most from visual cues (implement via the App Designer's column formatting where possible, or via embedded canvas app components where richer formatting is needed):

| View | Column | Rule |
| :---- | :---- | :---- |
| Active Cases | `tavu_slastatus` | Breached \= red, At Risk \= amber, On Track \= green, Met \= grey |
| Active Cases | `tavu_priority` | Critical \= red, Expedited \= amber, Standard \= neutral |
| Active Leads | `tavu_daysinbuffer` | \>14 \= red, \>7 \= amber, ≤7 \= neutral |
| Active Opportunities | `tavu_estimatedclosedate` | Past \= red, ≤7 days \= amber, ≤30 days \= neutral |
| Active Proposals | `tavu_expecteddecisiondate` | Past \= red, ≤3 days \= amber |
| Active Cases (AI Summary) | `tavu_aisentiment` | Critical \= red, Frustrated \= amber, Concerned \= blue, Calm \= grey |

---

## Document control

| Version | Date | Author | Notes |
| :---- | :---- | :---- | :---- |
| 1.0 | May 14, 2026 | Gustavo González Villani | Initial view definitions for 15 tables. Covers Active \+ Inactive views per table, supplementary views for Opportunity and Case, field-by-field rationale, sort order, filters, Field Security reminders, and implementation priority order. |
| 1.1 | May 14, 2026 | Gustavo González Villani (revision with Claude Opus 4.7) | Reconciliation pass against sales-model.md and service-model.md. **Added missing tables**: Opportunity Close (activity), Time Entry (activity), Knowledge Article, System Settings, Business Line / Category / Subcategory. **Added fields per actual schema**: Engagement Type and Customer Tier (denormalized) in Active Opportunities; Email in Active Contacts (highest-frequency action from contact list); AI Summary alternative view for Active Cases; My Open Opportunities personal pipeline view; Pipeline by Engagement Type for forecast support; Status Reason renamed consistently (vs "Status"). **Refined sort logic**: Active Opportunities sorts by Customer Tier first, then close date (surfaces Strategic+near-closing first); Active Leads sorts by statuscode first (Manual Review at top), then `tavu_daysinbuffer`; Active Proposals sorts by statuscode first to surface "Awaiting Decision" before "Draft". **Corrected statecode references**: Opportunity uses Open/Won/Lost (not Active/Inactive — per Dataverse pattern); Case uses Active/Resolved/Cancelled [later corrected to Active/Inactive in v1.3]. **Added conditional formatting recommendations table**. **Added priority items 13–15** to the build order for completeness. Reorganized table numbering (now 21 tables) to reflect full schema. Field Security note expanded to clarify view-definition implications. |
| 1.2 | June 17, 2026 | Gustavo González Villani (revision with Claude) | Case views refined during Module 1 / AI Assessment PCF implementation. **Active Cases**: added `ownerid` (manager visibility) and `tavu_resolutiontargetdate` (second SLA deadline) → 9 columns, a deliberate exception to the 7-column guideline justified by this being the team's operational hub. **My Open Cases**: same as Active Cases minus Owner. **Needs Review — Cases**: reworked toward its real purpose (validating the AI's categorization) — added `tavu_subcategory`, removed Owner / SLA Status / target-date columns because cases at `Manual Review Required` are pre-assignment and have no SLA yet; final set = Title, Customer, Subcategory, AI Confidence, AI Summary, AI Sentiment, Multi-Intent, Created On. **Clarification recorded**: `tavu_type` drives SLA matching (Tier + Type) and queue routing, while the Business Line / Category / Subcategory cascade is the per-firm topical taxonomy produced by Module 1 — two different axes; the cascade belongs in review and reporting views, not the urgency-focused triage list. |
| 1.4 | July 10, 2026 | Gustavo González Villani (revision with Claude) | **Sales views synced to the implemented schema/lifecycle.** Proposals (§6): replaced the non-existent `tavu_documenttype` with **`tavu_version`** (v1, v2 …) in Active/Inactive Proposals; fixed lookup names `tavu_opportunityid` → `tavu_opportunity`, `tavu_customerid` → `tavu_customer`. Opportunities (§4): caught up to the v1.4 sales-model refactor — replaced the `statuscode`-as-stages column and the removed `tavu_engagementtype` with the **`tavu_salesstage`** lookup (Active view + Won/Lost + a new "Pipeline by Sales Stage" secondary view); corrected the estimated-revenue field to **`tavu_estimatedrevenue`**; fixed the state filters (Active = Open; Won/Lost = `statecode = Inactive` + `statuscode`). |
| 1.3 | June 17, 2026 | Gustavo González Villani (revision with Claude) | **Statecode correction.** Verified against the live `tavu_case` schema that a custom table's `statecode` has only Active / Inactive — Resolved/Cancelled cannot exist as states. Corrected the Resolved Cases and Cancelled Cases views to filter by `statecode = Inactive` + a `statuscode` group instead of the non-existent `statecode = Resolved` / `= Cancelled`. Resolved group = Solved, Information Provided, Duplicate, Out of Scope; Cancelled group = Cancelled by Customer, Cannot Reproduce, Closed without Resolution. `service-model.md` Section 6 was corrected in tandem (service-model v1.3). Also added an "Inactive Cases (all closed)" combined view alongside the split Resolved / Cancelled views — all three retained per the user's setup. |

*This document is the operational reference for OpenTavu's view definitions in the model-driven app.*  
