# OpenTavu
## AI-First SMB CRM Accelerator
### Vision Document — v0.9

**Author:** Gustavo González Villani
**Date:** May 2026
**Status:** Public foundational draft — open to community feedback
**Target audience:** Power Platform consultants, MVPs, SMB integrators, and small-to-mid-market business leaders evaluating AI-enabled CRM strategies
**Project home:** opentavu.com

---

## 1. Executive Summary

**OpenTavu** is an open-source AI-First SMB CRM Accelerator (MIT license) that gives small and mid-market businesses a production-grade Customer Relationship Management foundation with artificial intelligence embedded in the core workflow — not added as an afterthought.

OpenTavu is built on **Microsoft Power Platform** (Dataverse + Power Apps Premium) and **Azure OpenAI** by default, designed to operate within the licensing and economic envelope that small and mid-market businesses can actually afford. Implementation is delivered through a network of consultants and integrators who download the framework from GitHub and adapt it to client needs, rather than as a turnkey product sold directly to end users.

**Why this matters now.** Enterprise AI capabilities for customer-facing operations have matured rapidly since 2023. Large enterprises have absorbed these capabilities through major platform investments. Small and mid-market businesses — which represent the vast majority of firms in the United States and most economies — have largely been left out: either priced out of the enterprise CRM tier where AI lives natively, or forced to retrofit AI onto legacy CRM systems that were not designed for it.

**What OpenTavu provides.** A production-grade, AI-first CRM foundation with three initial AI modules (Smart Case Categorization, Context-Aware Customer Communication Assistant, AI Activity Capture & CRM Hygiene Assistant), a base data model designed around how professional services firms actually work, automation flows, mobile-friendly canvas apps, analytics dashboards, and complete implementation documentation — released as a managed Power Platform solution that consultants can deploy into a client's tenant in hours, not months.

**Initial focus vertical:** Professional services SMBs in the United States — IT consultancies, B2B agencies, software/QA firms, professional services boutiques. The framework's architecture is horizontal and applicable to most SMB segments; the initial public adoption narrative is anchored in professional services because of direct domain experience and the size of that segment in the US economy.

**Customer model coverage.** OpenTavu explicitly supports three customer models within the Professional Services vertical: B2B-only firms (selling exclusively to businesses), B2C/individual-client firms (selling to natural persons — examples include some legal boutiques, independent coaches, individual wealth advisors), and hybrid firms (serving both segments simultaneously, such as legal boutiques with corporate and individual clients, accounting firms with business and personal returns, or wealth managers). The product architecture supports all three models without requiring artificial adaptations.

**Foundation.** OpenTavu extends a 2017 Master's thesis from Universidad Icesi that proposed and empirically validated a model for how SMBs should select, implement, and operate cloud CRM systems. The thesis identified the functional requirements common to most SMBs across sales, marketing, and customer service; OpenTavu carries that work forward by re-imagining how those functional requirements should be executed when artificial intelligence is available as a first-class capability rather than a bolted-on extension.

---

## 2. The Problem: Why Most SMBs Are Excluded From Enterprise AI

The mainstream narrative of 2024-2026 has been that "every business will use AI." In practice, the gap between enterprise adoption and small business adoption has widened, not narrowed. There are three concrete reasons.

**Cost asymmetry of enterprise CRM tiers.** The CRM platforms that ship AI capabilities natively — Microsoft Dynamics 365, Salesforce, HubSpot Enterprise — operate at price points (US$65–US$300 per user per month, plus implementation) that are unrealistic for a small firm of 10–50 employees. The result is that the tier of software where AI lives natively is structurally out of reach for the majority of SMBs.

**Retrofit complexity in lower tiers.** SMBs that adopt entry-level CRM tiers (or build their own on lower-cost platforms) face the opposite problem: AI capabilities must be added later, integrated awkwardly, and maintained without a dedicated technical team. The technical debt grows quickly. Most SMBs do not have an internal developer who can wire Azure OpenAI into a CRM workflow correctly, validate the output, or maintain prompt engineering as the underlying models evolve.

**Implementation gap.** Even when SMBs have access to capable platforms (Microsoft Power Platform with Power Apps Premium being a notable example), the practical question of *how* to design an AI-first CRM remains unsolved. There are tutorials. There are demos. There are reference architectures from vendors. What is missing is a production-grade, opinionated framework that a competent consultant can download, adapt, and deploy into a real SMB tenant in days or weeks, with AI capabilities working from day one rather than as a post-deployment project.

The combination of these three factors creates a structural exclusion: the businesses that most need productivity gains from AI — small firms competing with larger ones on the basis of agility — are the ones least likely to obtain them.

OpenTavu exists to close that implementation gap.

---

## 3. Pain Points: What the Evidence Shows

OpenTavu is not designed from intuition. The selection of initial modules and the architecture of the base data model are anchored in a review of:

- The Master's thesis methodology (González Villani & Lasso Cortés, 2017), which empirically validated functional requirements for SMB CRM adoption with three Colombian SMBs and a panel of ITIL experts.
- A 2026 review of professional services CRM literature, including comparative reviews on G2 and Capterra, industry guides from BigTime, Productive, Drum, OnePageCRM, Capsule, 4Degrees, Theo, DelveAnt, and Consulting Success, and feature analyses from CMap and Business News Daily.
- Cross-source triangulation across two independent AI-assisted research analyses (April–May 2026), where pain points were validated against multiple secondary sources to identify findings that converge across independent inquiries. A separate methodological evidence document records this triangulation process and its findings.

Seven concrete pain points emerged consistently across these sources for professional services SMBs:

**Pain #1 — Manual data entry leads to CRM abandonment.** Across nearly every source reviewed, the most cited reason CRM implementations fail in professional services firms is that consultants under-adopt the system because of manual data entry requirements. Reps and consultants frequently abandon the CRM, leading to "dirty pipeline" issues. Field-facing professionals attending 5–8 client meetings per day can spend 2–3 hours daily (10–15 hours weekly) on administrative CRM updates, which leads to "minimal compliance" patterns where data is entered at the end of the week with insufficient detail to be useful (DelveAnt, 2026; Theo CRM, 2026; industry adoption studies, 2025).

**Pain #2 — Disconnection between sales, delivery, and billing.** Consulting operations break when pipeline, delivery, and finance live in separate places. Approximately 72% of finance leaders in professional services identify data integration as their top problem, compounded by the inability to reconcile sold scope with delivery reality. Manual transfer of scope from CRM to delivery systems and finally to ERP creates revenue leakage on out-of-scope work (Productive, 2026; Certinia, 2025).

**Pain #3 — Proposal and SOW generation is slow and inconsistent.** An estimated 90%+ of professional services teams still generate Statements of Work manually (Zoma, 2025), consuming hours of senior consultant time per proposal. Manual SOW creation is slow enough that prospects often advance with alternative providers before the document is delivered. Tools that consolidate proposal generation alongside CRM workflow are repeatedly cited as differentiators (Drum, 2026; BigTime Software, 2026).

**Pain #4 — Follow-up discipline degrades as relationships scale.** Activity reminders that prevent missed follow-ups on proposals and client engagements are repeatedly cited as a common pain point for busy consultants. As consultants scale relationships from dozens to hundreds of contacts, the cognitive load exceeds what unassisted human memory can sustain. The structural problem is not forgetting to set an Outlook reminder — it is the absence of an algorithmic alert when a strategic relationship has fallen below an optimal interaction threshold (Theo CRM, 2026; Close, 2026).

**Pain #5 — Time tracking and capacity visibility are persistent gaps.** Tracking time and expenses is consistently identified as a pain point for firms in professional services. Capacity visibility — knowing which team members are available and for how long — is described as one of the most valuable yet often overlooked CRM features in the segment. Standard CRMs typically lack native billable-hour tracking or resource allocation, forcing consulting firms to rely on separate tools (CMap, 2026; BigTime, 2026; DelveAnt, 2026). OpenTavu addresses this through integration with specialized PSA tools rather than native implementation (see scope boundary in Section 9).

**Pain #6 — Email and meeting context is lost.** Consultants often do not update the CRM after calls and meetings, losing critical context. Without integrated email capture, different team members hold different fragments of the conversation history, and clients receive inconsistent information. Tactical knowledge about budgets, objections, and political risks remains trapped in individual email threads, inaccessible to the firm as an entity (DelveAnt, 2026; SigParser, 2025).

**Pain #7 — RFP and questionnaire fatigue.** Corporate procurement increasingly requires boutique firms to respond to extensive Request for Proposal documents (RFPs), Due Diligence Questionnaires (DDQs), and information-security assessments before winning contracts. Industry data indicates teams spend an average of 36 hours responding to a single RFP, with management consulting leading globally in submission volume. The workflow involves downloading complex documents, extracting requirements, searching past responses, and coordinating with solution engineers. Non-standard proposals and questionnaires effectively paralyze small firms; the lack of an intelligent system that categorizes requirements and generates first drafts based on corporate knowledge severely limits the volume of bids a 20-person firm can pursue. AI specialized in RFP analysis has been shown to reduce response times by 40–60%, allowing firms to participate in more opportunities without expanding headcount (industry RFP automation studies, 2025–2026).

These seven pain points anchor the design of OpenTavu. The three initial AI modules (Section 8) are selected to address Pains #1, #4, and #6 directly. The base data model and commercial flow (Section 7) are designed to mitigate Pains #1, #2, and #4. Future modules on the roadmap (Section 8) address Pains #3, #5, and #7.

---

## 4. The Vision: AI-First, Not AI-Bolted-On

The central design principle of OpenTavu is that artificial intelligence is treated as a **first-class element of the CRM workflow**, not a feature added on top of a traditional CRM.

The distinction matters in practice:

- **AI-bolted-on:** An incoming case is created by a human, categorized by a human, assigned by a human, drafted by a human. Then, somewhere in the workflow, an AI assistant is offered to "help" — usually as a sidebar or a button. The human remains the default agent of every action.
- **AI-first:** An incoming case arrives. The categorization is performed by AI with confidence thresholds, validated against the active typifications of the CRM, and only escalated to a human when confidence is insufficient or an exception is detected. The draft response is generated by AI based on the customer's history and the case context, presented to a human for review and editing rather than for composition. The AI is the default first responder; the human is the editor and the exception handler.

OpenTavu is opinionated about this. The base data model, the automation flows, and the canvas apps are designed assuming AI is part of the operational loop. Removing the AI components leaves a functional CRM, but the design is meant to be experienced with AI active.

This posture has direct implications:

- **Confidence and validation are first-order concerns.** Every AI output that affects a record passes through validation (against active typifications, against business rules, against confidence thresholds) before persistence. Hallucinations are treated as a risk to be engineered against, not a feature to be excused.
- **The economic envelope drives architecture.** OpenTavu is designed to operate on Power Apps Premium (~US$20 per user per month for the platform, plus pay-as-you-go Azure OpenAI consumption typically in the US$20–US$200/month range for an SMB), without requiring Dynamics 365 enterprise licensing. This is not a workaround; it is a deliberate architectural choice. The base entities use Dataverse standard tables where applicable (`account`, `contact`) and custom tables (`tavu_lead`, `tavu_opportunity`, `tavu_case`, `tavu_proposal`, `tavu_knowledge_article`, `tavu_systemsettings`) for entities that would otherwise be tied to Dynamics 365 licensing restrictions or for product-level configuration.
- **The framework is meant to be implemented by consultants, not end users.** The audience that downloads, evaluates, forks, and recommends OpenTavu is the Power Platform consultant and integrator community. SMB end users consume the result — a working AI-first CRM in their tenant — but they do not interact with GitHub. This shapes how the documentation is written and how releases are packaged.

---

## 5. Academic Foundation: From Thesis (2017) to Production (2026)

OpenTavu is the applied evolution of a 2017 Master's thesis at Universidad Icesi (Cali, Colombia), titled *"Modelo para la selección, gestión y operación de sistemas que permitan efectuar la gestión de clientes en la nube para las PYMES"* (Model for the Selection, Management, and Operation of Cloud Customer Management Systems for SMEs).

The thesis, co-authored with Ginna Marcela Lasso Cortés under the advisorship of Álvaro Pachón de la Cruz, PhD, produced a two-phase model:

**Phase 1 — Selection framework.** A scoring instrument that helps SMBs evaluate cloud CRM systems against five axes: functional fit (sales, marketing, customer service), technical robustness, cloud and integration capabilities, support and ecosystem, and provider stability. The instrument was applied to 44 cloud CRM products available at the time and validated empirically with three Colombian SMBs, scoring 4.68/5 in relevance, coherence, and applicability.

**Phase 2 — Operations guide.** An ITIL-based operational framework helping SMBs run the chosen CRM in steady state. Validated by a panel of ITIL experts, scoring 4.52/5.

What the thesis identified — and what remains substantially unchanged in 2026 — is the set of functional requirements that most SMBs share across sales, marketing, and customer service operations. The names of the products have shifted, the deployment models have moved from cloud-as-novelty to cloud-as-default, but the underlying functional needs are remarkably stable.

What has changed, and what OpenTavu addresses, is **how those functional requirements should be executed**. In 2017, generative AI was not a viable production capability. In 2026, it is. OpenTavu is the natural next step of the thesis: taking the same functional foundation and re-implementing it under the assumption that AI capabilities are part of the operational loop from day one.

This connection is not decorative. The thesis methodology directly informs OpenTavu's functional coverage map (Section 9) and its design principle of explicit validation against business rules and active configurations.

The thesis is publicly available through Universidad Icesi's institutional repository (citation will be linked once the public URL is confirmed) and is referenced in the author's professional profile.

---

## 6. Architecture and Design Principles

OpenTavu follows seven design principles.

**1. Operate within Power Apps Premium licensing.** OpenTavu uses a deliberate mixed-table architecture: Dataverse standard tables (`account`, `contact`) for entities that are not licensing-restricted, plus custom tables (`tavu_*` namespace) for entities that would otherwise require Dynamics 365 licensing. This is a deliberate architectural decision that allows the framework to run on Power Apps Premium licensing (~US$20 per user per month) rather than requiring Dynamics 365 enterprise licensing (US$65–US$150+ per user per month). The economic envelope of an SMB drives the architecture, not the other way around.

**2. AI as a first-class workflow component.** AI invocations are implemented as custom C# Workflow Activities and Power Automate flows that participate in the CRM's standard processing pipeline. They are not external integrations called via webhook. They run inside Dataverse transactions, with proper error handling and observability.

**3. Validation before persistence.** Any AI output that affects a record is validated before write. Smart Case Categorization, for example, validates the proposed category against the customer's active typification configuration, applies a confidence threshold, and only persists if both checks pass. Cases below the threshold are flagged for human review, not silently degraded.

**4. Configuration over code wherever possible.** OpenTavu prefers system parameter tables, JSON configuration entities, and metadata-driven behavior over hardcoded logic. This allows a consultant to adapt the framework to a client's vocabulary and process without writing C# code in most cases.

**5. Mobile-friendly by default.** SMB sales and service teams operate on the move. The base canvas apps are designed mobile-first, with model-driven apps providing the back-office desktop experience.

**6. Production-grade, not demo-grade.** OpenTavu prioritizes traceability, error logging, retry logic, token budgeting, and graceful degradation when external services are unavailable. The Smart Case Categorization module, for example, includes execution section tracking, hallucination prevention, and confidence-based routing — patterns derived from a real production deployment, not a tutorial.

**7. Documentation as a first-class deliverable.** OpenTavu is meant to be implemented by consultants who did not write it. Implementation documentation is part of every release, not an afterthought.

### Why mixed-table architecture (standard + custom)

OpenTavu's data model uses a deliberate combination of Dataverse standard tables and custom tables, rather than going purely custom or purely standard. The reasoning:

**Standard tables (`account`, `contact`)** are used because they are mature, well-indexed, integrated natively with Microsoft Graph (Outlook, Teams, Power BI), supported by the broader Power Platform ecosystem (third-party connectors, Microsoft documentation, MVPs' tutorials), and — critically — fully accessible under Power Apps Premium licensing without requiring Dynamics 365. Reinventing them as custom would lose all these advantages without producing meaningful differentiation. Microsoft itself recommends using standard tables when applicable and customizing them with additional columns rather than creating duplicate custom tables. The author follows that guidance.

**Custom tables (`tavu_lead`, `tavu_opportunity`, `tavu_case`, `tavu_proposal`, `tavu_knowledge_article`)** are used for entities whose Dataverse standard versions are restricted tables — meaning their use requires Dynamics 365 licensing for create, update, and delete operations. These restrictions make the standard versions unsuitable for the SMB price point OpenTavu targets. The custom versions serve the same functional purpose while remaining within Power Apps Premium licensing scope.

**Customer model coverage:** OpenTavu explicitly supports three customer models within the Professional Services SMB vertical:

- **B2B-only firms:** sell exclusively to businesses (IT consultancies, B2B agencies, software QA boutiques)
- **B2C/individual-client firms:** sell to natural persons (some legal boutiques, independent coaches, individual wealth advisors)
- **Hybrid firms:** serve both segments simultaneously (legal boutiques with corporate and individual clients, accounting firms with business and personal returns, wealth managers)

The product architecture supports all three models without requiring artificial adaptations (e.g., creating fake Accounts for individuals). Configuration is done via `tavu_systemsettings.tavu_customermode` with values B2B_Only, B2C_Only, or Mixed.

A useful side effect of this architecture: a client who outgrows the SMB tier and decides to upgrade to Dynamics 365 Sales finds that their `account` and `contact` data already lives in the right tables, simplifying the migration significantly. Custom entities can be migrated with established Dataverse migration tools. The architecture is therefore a stepping stone, not a dead-end.

### Provider-agnostic AI architecture

OpenTavu defaults to **Azure OpenAI** as its AI provider for three reasons: (a) architectural coherence with the Microsoft Power Platform stack, (b) unified enterprise contracts for SMB clients already on Microsoft 365 (a single Microsoft Customer Agreement covers Dataverse, Power Platform, and Azure OpenAI), and (c) authentication via Microsoft Entra ID without managing separate API key rotation across providers.

However, the framework's AI invocation layer is designed as a **provider abstraction interface**. Concrete implementations for alternative providers (Anthropic Claude, Google Gemini, others) can be added without changing the modules that consume them. This protects implementations from vendor lock-in, enables comparative evaluation when a client has specific preferences, and signals senior architectural intent rather than hard dependency on a single vendor.

In practical terms: the modules call an internal `IAIProvider` interface; the default binding is `AzureOpenAIProvider`; alternative bindings are pluggable via configuration. Documentation of how to add a custom provider is part of the framework's developer guide.

In the optional managed-service deployment, the same `IAIProvider` abstraction points at a **central AI gateway** operated by the provider: it holds the AI keys, routes each task to an appropriate model, and meters usage — so client tenants never handle AI credentials and the provider can contract capacity at volume. This is a deployment choice enabled by the abstraction, not a change to the modules. (See `architecture.md`.)

### Token economics and cost-defensive patterns

A common failure mode for AI-enabled SMB applications is unbounded growth in inference costs. Microsoft pricing calculators often show illustrative low-end estimates (e.g., a few US dollars per month for small workloads), but real production deployments — particularly those involving background tasks like data hygiene, lead enrichment, historical email analysis — can produce monthly costs an order of magnitude higher if naively implemented. For an SMB operating on tight margins, an unexpected Azure invoice will end adoption faster than any technical defect.

OpenTavu builds in cost-defensive patterns from day one:

- **Batch processing for asynchronous workloads.** Tasks that do not require real-time response (overnight database normalization, retroactive email analysis, periodic relationship-health computation) are routed through the **Azure OpenAI Batch API**, which Microsoft prices at approximately half the rate of synchronous calls and avoids per-minute rate limit pressure.
- **Token budgeting per module.** Each module declares a token budget; if exceeded within a configurable window, non-critical operations are deferred or downgraded to a smaller model.
- **Confidence-gated operations.** Operations with low expected utility (e.g., re-enriching a contact whose data has not changed) are skipped, not queued.
- **Configurable model tiers per task.** High-precision tasks (SOW drafting in a future module) use higher-capability models; routine tasks (case categorization, activity extraction) use cost-efficient models. The provider abstraction interface makes this swappable.

These patterns are documented in the framework's implementation guide so that consultants deploying OpenTavu in client tenants can configure budgets appropriate to that client.

### A note on first-party AI assistants (Microsoft Copilot for Sales, Salesforce Einstein, etc.)

Microsoft, Salesforce, and other large vendors increasingly bundle AI assistants directly into their enterprise CRM tiers. A reasonable question is: *why build a separate AI-first framework when first-party assistants are available?*

The answer is two-fold: price-tier accessibility, and depth of CRM integration.

**Price-tier accessibility.** Microsoft Copilot for Sales requires a Dynamics 365 Sales Enterprise license (US$95+ per user per month) plus a Copilot for Sales add-on (US$40+ per user per month) — a combined US$135+ per user per month before AI consumption costs. Salesforce Einstein and HubSpot Sales AI follow comparable patterns. These offerings are excellent for the businesses that can afford them, but they are precisely the tier from which the structural exclusion described in Section 2 arises.

**Depth of CRM integration.** General-purpose AI assistants like Microsoft 365 Copilot operate primarily on user-level signals (the email currently open, the document being edited, the meeting just held). They are not deeply tied to CRM record structure or to organization-wide context. OpenTavu's modules, by contrast, operate inside Dataverse: they read from and write to specific records, validate against organization-specific configuration (typifications, configurable pipeline stages, business rules), and treat the CRM as the system of record rather than a reference.

OpenTavu is not designed to outperform Copilot for Sales feature-for-feature; it is designed to bring AI-augmented CRM workflow to the SMB price tier (~US$20 per user per month + AI consumption), with deeper CRM-record integration than general-purpose assistants. That is a different segment of the market and a different value proposition. Where a client has the budget for Dynamics 365 Sales Enterprise and Copilot for Sales, that is often the right choice; where they do not, OpenTavu exists.

**A note on convergence with incumbent direction.** As of 2026, major CRM vendors — HubSpot prominent among them — are publicly articulating the same thesis OpenTavu is built on: that the bottleneck for AI in customer-facing operations is not model capability but the availability of business context at the moment of decision, and that AI grounded in the system of record outperforms general-purpose assistants operating on isolated signals. This convergence is corroborating evidence that the problem OpenTavu targets is real and economically significant; it is not a claim of conceptual uniqueness. OpenTavu's defensibility does not rest on the idea of context-grounded AI — that idea is now shared by well-funded incumbents. It rests on three structural factors those incumbents do not address for this segment: (1) **price tier** — incumbents embed AI in paid tiers (HubSpot's AI-bearing Hubs, Dynamics 365 Sales Enterprise + Copilot for Sales, Salesforce Einstein) priced well above the ~US$20/user + consumption envelope OpenTavu targets, which is precisely the structural exclusion described in Section 2; (2) **Microsoft/Dataverse ecosystem fit** — for an SMB already operating in Microsoft 365, OpenTavu requires no second vendor, while incumbent platforms are separate ecosystems; (3) **open-source distribution and configuration-over-code multi-tenancy** — OpenTavu is forked, deployed into the client's own tenant, and reconfigured per firm without code, a model the proprietary incumbents do not offer. The differentiator is tier, ecosystem, and distribution model — not the concept.

### Stack at a glance

| Layer | Technology |
|---|---|
| Data platform | Microsoft Dataverse |
| Standard entities | `account`, `contact` (Dataverse standard, with custom columns including `tavu_iscustomer`, `tavu_customertier`, `tavu_engagementstatus`) |
| Custom entities | `tavu_lead`, `tavu_opportunity`, `tavu_case`, `tavu_proposal`, `tavu_knowledge_article`, `tavu_systemsettings`, plus master configuration tables (`tavu_customertierdefinition`, `tavu_casetype`, `tavu_sla`, `tavu_salesstage`) |
| App layer | Model-driven apps + Canvas apps (Power Apps Premium) |
| Workflow & automation | Power Automate + custom C# Workflow Activities |
| AI capabilities | Azure OpenAI (default, sync + Batch API); provider-agnostic interface for alternatives |
| Analytics | Power BI |
| Distribution | Managed solution (.zip) via GitHub Releases |

---

## 7. Commercial Flow Design: Single Lifecycle, Dual Entry

Most CRM platforms inherit a commercial flow originally designed for high-volume B2C sales or product-driven manufacturing: a *Lead* entity acts as a quarantine buffer, gets *qualified*, and only then is converted into an *Account*, a *Contact*, and an *Opportunity*. This pattern, embedded in Microsoft Dynamics, Salesforce, and others, is well-suited to its original use cases but creates measurable friction in professional services firms — confirmed by the evidence reviewed in Section 3 (CRM abandonment, manual data entry, lost context).

A more recent pattern, popularized by HubSpot and adopted by modern CRMs like Attio, Folk, 4Degrees, and Theo, eliminates the Lead as a separate entity entirely and uses a **Lifecycle Stage** field on the Contact record. This is closer to how professional services firms actually work, but it introduces a different problem: anonymous inbound (a form submission from someone unknown, a `sales@` email, an unsolicited message) has nowhere to land before being promoted to a full Contact and Account, which can pollute the master database with low-quality records.

OpenTavu adopts a **hybrid model: Single Lifecycle, Dual Entry**.

> **Terminology note.** "Single Lifecycle" refers to the lifecycle of opportunities (where the commercial cycle truly lives), not to a Lifecycle Stage field on Contact. Earlier model proposals included a Lifecycle Stage on Contact (Cold → Engaged → Qualified → Customer); this approach was deprecated upon recognizing that in B2B Professional Services the commercial subject is the Account, while in B2C it is the Contact, and that representing this state through relationships with opportunities and cases is more accurate than collapsing it into a single label field on Contact. "Dual Entry" remains valid: Path A (direct Contact + Account creation) vs Path B (anonymous inbound buffered in tavu_lead).

### Data model

- **Primary entities:** `account`, `contact` (Dataverse standard), and `tavu_opportunity` (custom). These are the operational core. The consultant works with these every day.
- **Customer status is captured at the right level: Account for B2B, Contact for B2C.** Both standard tables receive custom columns (`tavu_iscustomer`, `tavu_customersince`, `tavu_lastengagementdate`); Contact additionally receives `tavu_engagementstatus` (Cold / Engaged / Inactive) for tracking the communication relationship separately from the contractual customer status. The marker `tavu_iscustomer = Yes` is set automatically on the appropriate entity when an opportunity closes Won: on Account if the opportunity's customer points to an Account, on Contact if it points to a Contact (the B2C case). This dual approach correctly supports firms that sell exclusively to businesses, exclusively to individuals, or to both simultaneously.
- **`tavu_lead` exists but as an optional ingestion buffer**, not a mandatory pre-step. It is used only when an inbound signal cannot be cleanly attributed to an existing Contact or Account at the moment of arrival.
- **`tavu_opportunity` uses discovery-driven, configurable pipeline stages** rather than the traditional Quote-driven flow, because professional services sell scope, not SKUs. Pipeline stages live in a dedicated configuration table (`tavu_salesstage`) so each firm can adopt its own vocabulary (a consultancy might use "Discovery / Proposal Drafted / Proposal Sent / Negotiation"; a QA boutique might use "RFI / Evaluation / RFP / Offer / Negotiation"; a digital agency might use "Qualification / Discovery Call / Pitch / Close"). Each stage carries a default probability and a forecast category (Pipeline / Best Case / Committed / Closed), giving Sales Managers standard forecasting vocabulary out of the box and providing the data foundation that the future AI-Assisted Forecasting module (see Section 8 roadmap) needs to learn per-firm conversion patterns.

### Dual entry flow

**Path A — Outbound (consultant-initiated, the common case):**
1. Consultant identifies a real prospect through their network, an event, a referral, or an existing relationship.
2. Consultant creates a `contact` record directly, linked to a new or existing `account` (B2B case), or as a standalone Contact without Account (B2C case — when the customer is a natural person, common in legal boutiques, independent coaches, individual wealth advisors). Engagement is tracked via `tavu_engagementstatus` on the Contact.
3. When a real opportunity emerges, the consultant creates a `tavu_opportunity` from the Contact.
4. **The `tavu_lead` entity is never used** in this path. The consultant works with full Account + Contact records from the first interaction.

**Path B — Inbound (anonymous or low-confidence ingestion):**
1. An external signal arrives (web form, generic `sales@` email, LinkedIn message from someone not in the CRM, partner referral with incomplete data).
2. The signal lands as a `tavu_lead` record — an ingestion buffer, not a master record.
3. The **AI Activity Capture & CRM Hygiene module (Section 8, Module 3)** processes the `tavu_lead` automatically: it attempts to match against existing `account` and `contact` records, evaluates the signal's quality, and presents the consultant with a recommendation — promote to `contact`, link to existing `account`, or discard as noise.
4. The consultant approves the AI's suggestion or overrides it. Promoted records become regular Contact and Account records (or just Contact in the B2C case), tracked via `tavu_engagementstatus` and (when applicable, after a Won opportunity) `tavu_iscustomer`.

### Why this design

This hybrid is opinionated and defensible:

- It **respects how professional services firms actually work.** Most relationships start by direct contact, not by anonymous form. Forcing every interaction through a Lead-Qualify-Convert flow contradicts daily reality and produces the CRM abandonment described in Pain #1.
- It **preserves the buffer function of the Lead entity** for the cases where it matters: anonymous inbound, low-quality signals, partner data of variable quality. The buffer exists, but it is not a barrier the consultant has to walk through every time.
- It **gives the AI Activity Capture module a clear, valuable role:** orchestrating the promotion from buffer to master record automatically, with confidence thresholds, instead of asking the consultant to do it manually.
- It **does not radically reinvent CRM data modeling.** It synthesizes proven patterns from Dynamics (entity separation for ingestion buffer; polymorphic customer field with auto-populated typed lookups, as used in Quote, Order, and Invoice) and from HubSpot (lifecycle tracking concepts) into a model adapted to professional services SMBs. The hybrid customer field architecture (polymorphic primary `tavu_customer` with auto-populated typed `tavu_account` and `tavu_contact`) gives both UX simplicity and reporting cleanliness — a deliberate engineering judgment, not novelty for its own sake.

Implementation details (specific Choice columns, status reasons, security roles, view configurations) are documented in OpenTavu's technical guide.

---

## 8. Initial AI Modules

OpenTavu launches with three AI modules in priority order. Each module ships as an independently consumable component within the framework, with its own documentation, configuration, and validation logic. Each module addresses one or more of the pain points identified in Section 3.

### Module 1 — Smart Case Categorization

**Pain addressed:** Pain #1 (manual data entry) and partially Pain #6 (lost context).

**What it does.** Automatically categorizes incoming cases (from email, web forms, or manual creation) into business lines, categories, and subcategories defined by the SMB's own typification configuration. Routes to the correct queue or owner based on the categorization.

**How it works.** A custom C# Workflow Activity invokes Azure OpenAI with a structured JSON prompt that includes the case content and the active hierarchical typification of the customer. The model returns a categorization with a confidence score. The activity validates the output against active typifications, applies a configurable confidence threshold, and either persists or flags for human review.

**Why it matters for SMBs.** Small support teams cannot afford to spend the first 30 seconds of every case manually deciding where it belongs. Automating categorization frees scarce attention for the parts of customer service that require human judgment. In professional services contexts, this applies equally to RFP intake, billing inquiries, scope-change requests, and routine support — the categorization vocabulary changes per client, but the pattern is universal. A natural extension toward RFP-specific intake analysis is recorded on the roadmap below.

**Status.** Production-tested in a prior deployment. The OpenTavu version is an abstracted, sanitized, and generalized re-implementation, released as the first module of the framework.

### Module 2 — Context-Aware Customer Communication Assistant

**Pain addressed:** Pain #4 (follow-up discipline) and Pain #6 (lost context).

**What it does.** Generates draft responses to customer emails and suggests proactive follow-up communications based on the customer's full CRM history — not just the email currently open. Pulls from related cases, prior interactions, active opportunities, and engagement patterns. Drafts are presented to the human user for review and editing rather than auto-sent.

**How it works.** A Power Automate flow gathers structured context from across Dataverse (customer record, related cases, prior interactions, active opportunity if relevant, recent meeting summaries), invokes the configured AI provider (Azure OpenAI by default) with a prompt template configurable by the implementing consultant, and writes the generated draft to a draft column on the case or opportunity record for human review. The CRM-record-grounded context distinguishes this module from general-purpose AI email assistants that operate only on the email thread itself.

**Why it matters for SMBs and how this differs from Microsoft 365 Copilot.** General-purpose assistants like Microsoft 365 Copilot draft email responses based on the email thread and the user's recent activity. They do not have first-class access to CRM record structure or organization-wide history beyond what Microsoft Graph exposes. OpenTavu's Module 2 is grounded in the full Dataverse context: it knows about prior cases for that account, opportunity stage history, the lifecycle stage of the contact, and patterns across the consultant's portfolio. For professional services firms, where consistency of tone and context across a portfolio of client engagements is a documented gap, the differentiator is depth of CRM integration, not generic drafting capability.

**Status.** Initial development. Target release: Month 3.

### Module 3 — AI Activity Capture & CRM Hygiene Assistant

**Pain addressed:** Pain #1 (manual data entry / CRM abandonment) — the most cited pain point in the evidence reviewed. Also addresses Pain #6 (lost context) and supports the commercial flow described in Section 7.

**What it does.** Automatically captures consultant activity (emails sent and received, meetings held, calls logged) from connected sources, extracts the relevant context using AI, and updates the CRM without manual intervention. Suggests fields to update on existing records, links communications to the correct opportunity or account, detects clients or prospects with no recent activity that warrant follow-up, and orchestrates the promotion of `tavu_lead` records (ingestion buffer) into `contact` records (master) with confidence thresholds.

**How it works.** A combination of Power Automate flows and custom C# Workflow Activities. Inbound signals from Microsoft Graph (Outlook email, Teams meetings, calendar events) are processed by an AI extraction step that identifies which existing records the activity refers to, what new information it implies, and whether any state transitions should be suggested. All suggestions are presented to the consultant for one-tap approval; nothing writes to the master records without confirmation, except for low-risk activity logging (e.g., "an email was sent to this contact at this time"). Heavy retroactive operations (historical email analysis, periodic relationship-health computation) run via Azure OpenAI Batch API to control cost.

**Why it matters for SMBs.** This module addresses the single most consistently cited reason CRM implementations fail in professional services firms: consultants do not update the CRM because manual entry is friction they cannot afford. By capturing activity automatically and proposing record updates, OpenTavu reduces CRM abandonment and produces the "clean pipeline" that downstream analytics and forecasting depend on. The downstream effect is also measurable: higher percentage of activity captured, fewer dropped follow-ups, better forecast quality.

In this sense Module 3 is **foundational, not merely additive**: Modules 1 and 2 — and every roadmap module — depend on a clean, context-complete record layer. AI operating on stale or partial data scales effort, not results (the "context gap"). The hygiene layer is therefore a prerequisite for the rest of the AI-first thesis rather than a third add-on, and its sequencing reflects that.

**Status.** Initial development. Target release: Month 4.

### Roadmap modules (documented but not yet built)

The following modules are explicitly on the roadmap and address the remaining pain points identified in Section 3. They are documented now so OpenTavu's direction is clear, but they will not be built in the first 12 months.

- **AI RFP & Proposal Architect** — addresses Pain #3 and Pain #7. Ingests RFP and DDQ documents, parses requirements, searches a corporate response library, and assembles first drafts of structured proposals or Statements of Work. Leverages the `tavu_proposal` entity. The strategic priority of this module is high — it addresses the costliest bottleneck for senior consultants — but the implementation complexity (template engineering, prior-content libraries, legal sensitivity) places it after the three foundational modules establish a clean data layer.
- **AI Meeting Summarizer & Action Item Extractor** — addresses Pain #6. Takes Teams meeting transcripts (or pasted notes), generates a structured summary, extracts action items with owner and deadline, creates follow-up tasks linked to the relevant opportunity or account. Crucially, also writes structured updates back to opportunity records (e.g., new close date, identified budget) — going beyond generic transcription summaries.
- **AI Relationship Health Monitor** — addresses Pain #4. Analyzes communication cadence and sentiment for retainer clients, surfaces relationships that show signs of cooling before they cancel. Maps passive relationship strength based on email reciprocity and meeting frequency.
- **AI-Assisted Forecasting & Capacity Planning** — addresses Pain #2 and Pain #5. Combines pipeline data with delivery commitments and team capacity to produce more reliable revenue and resource forecasts. Builds directly on the configurable `tavu_salesstage` architecture: once a tenant has accumulated sufficient closed-opportunity history (typically 30+ records), the module analyzes per-firm Won/Lost conversion ratios at each stage and proposes data-driven adjustments to the default probability values configured by the admin (e.g., *"your Negotiation stage actually closes at 65%, not the 80% currently configured"*). Generates forecast confidence intervals using each firm's actual conversion curves and detects stuck opportunities relative to typical stage duration. Requires sufficient historical data to be useful, which is why it is positioned as a roadmap item rather than an initial module.
- **AI Lead Scoring** — useful for SMBs with higher inbound volume than typical professional services boutiques (e.g., digital agencies running marketing campaigns). Documented as an optional module rather than a default one.
- **Document Intelligence** — automated processing of contracts, invoices, NDAs.
- **Conversational AI Search** — natural-language querying over CRM data.
- **AI grounding transparency (context completeness)** — surfaces, per AI-assisted operation, how many of the available grounding sources actually fed it (e.g., *"this draft used 3 of 5 available context sources: case history ✓, active opportunity ✓, meeting summaries ✗"*). It operationalizes trust — the leading adoption barrier for AI in customer-facing work — by making the AI auditable about its own grounding, and hooks natively into the existing confidence-gating. Documented as roadmap; the enabling design decision (each module emitting its grounding signal to a shared convention from the start, rather than retrofitting the telemetry later) is reserved now.
- **Channel-agnostic activity capture** — extends Module 3's capture architecture beyond the default Microsoft Graph connectors (Outlook email, Teams meetings, calendar). Module 3 is designed so that capture sources are pluggable connectors rather than hardcoded integrations; Microsoft Graph is the default binding. Additional channels (WhatsApp Business, SMS, other messaging platforms) can be added as configurable per-tenant connectors without altering the module's core logic, consistent with OpenTavu's configuration-over-code principle. This is documented as roadmap because the initial vertical (US Professional Services SMBs) is served well by the Microsoft Graph default; channels such as WhatsApp are dominant in some non-US markets (industry data places WhatsApp among the top business communication channels in Latin America and Spain), where an implementing consultant could enable the corresponding connector. WhatsApp is therefore never product architecture — it is a connector a tenant may activate.

---

## 9. Functional Coverage Map

OpenTavu covers the functional surface area common to most professional services SMB CRM operations across three areas, derived from the 2017 thesis methodology and updated to reflect 2026 capabilities and the pain points identified in Section 3. AI-augmented capabilities are highlighted in **bold**.

### Sales

- Account and contact management (`account`, `contact` standard Dataverse)
- Inbound signal ingestion through `tavu_lead` buffer (see Section 7)
- Customer status tracking on Account and Contact (`tavu_iscustomer`, `tavu_customersince`, `tavu_lastengagementdate`); engagement tracking on Contact (`tavu_engagementstatus`: Cold / Engaged / Inactive)
- Tenant-level Customer Mode configuration (`tavu_systemsettings.tavu_customermode`: B2B_Only / B2C_Only / Mixed) controlling lookup behavior across the application
- Hybrid customer field architecture in opportunity and case (polymorphic primary + auto-populated typed lookups), supporting B2B, B2C, and hybrid firms without artificial adaptations
- Opportunity pipeline with discovery-driven, per-firm configurable stages (`tavu_opportunity` + `tavu_salesstage` configuration table with default probability and forecast category per stage)
- **Project handoff workflow** — structured transition from a won opportunity to delivery (assigning project owner, creating delivery folder structure, kicking off implementation tasks). Addresses Pain #2.
- **Retainer / recurring engagement tracking** — first-class support for ongoing client relationships, not only one-time deals. Common in consultancies, agencies, and boutique firms.
- Activity tracking (calls, meetings, emails, tasks)
- Sales forecasting and pipeline reporting with standard categories (Pipeline / Best Case / Committed / Closed)
- **AI-driven activity capture and record updates (Module 3)**
- **AI-generated next best action suggestions (roadmap)**

### Marketing

- Contact segmentation by engagement status, customer status, and other attributes
- Lightweight campaign tracking (not a full marketing automation suite)
- Email engagement tracking via Microsoft Graph integration
- Marketing-to-sales handoff via engagement status progression and opportunity creation
- **AI customer communication drafting (Module 2 extension)**
- **AI-assisted segment definition (roadmap)**

### Customer service

- Case capture from multiple channels (`tavu_case`)
- Knowledge base (`tavu_knowledge_article`)
- Queue and assignment management
- SLA tracking (lightweight)
- Customer satisfaction tracking
- **AI smart case categorization and routing (Module 1)**
- **AI context-aware response generation (Module 2)**
- **AI knowledge base search (roadmap)**

### Cross-functional

- Mobile-friendly canvas apps for field roles
- **Capacity visibility** — basic reporting on consultant availability, current commitments, and upcoming workload, supporting resource decisions during the sales-to-delivery handoff.
- **Time tracking integration** — OpenTavu does not implement its own time-tracking engine. It integrates with specialized tools (Harvest, Toggl, Clockify, Microsoft Project, BigTime) so that time entries can be associated with opportunities and cases without duplicating data. Addresses Pain #5.
- Power BI dashboards (sales pipeline, conversion rates, agent performance, AI confidence metrics, time-on-engagement)
- Implementation documentation and consultant onboarding kit
- Configuration entities for tenant-specific customization

### Scope boundary: CRM, not PSA

**OpenTavu is a CRM AI-First framework, not a Professional Services Automation (PSA) suite.** It deliberately does not implement invoicing, expense management, full project management, or detailed resource planning. These functions are well-served by specialized tools (BigTime, Productive, Kantata, Microsoft Project, accounting suites), and reproducing them inside OpenTavu would dilute focus and inflate complexity without competitive advantage.

OpenTavu's role in the SMB tech stack is to be the **system of record for client relationships and AI-augmented operational workflow**, integrated cleanly with PSA, accounting, and project tools rather than replacing them. This boundary is deliberate.

---

## 10. Roadmap and 12-Month Public Commitments

This section commits to specific, modest, defensible targets for the first 12 months of OpenTavu's public life. These commitments are public on purpose: they exist to be evaluated against actual progress.

### Concrete commitments (controllable)

- **Implement OpenTavu with 2–3 SMB clients** in the professional services vertical, with documented metrics and formal written permission to publish results.
- **Publish at least 3 long-form technical articles** documenting the architecture, implementation, and lessons learned — through LinkedIn Articles, Microsoft Tech Community, or equivalent platforms.
- **Deliver 1–2 public talks** at Power Platform user groups or technical conferences.
- **Maintain an active GitHub repository** with documented releases, complete technical documentation, and timely engagement with community questions and issues.
- **Document one production case study** with quantitative metrics (categorization accuracy, response-time reduction, percentage of activity automatically captured, or equivalent), with the client's written permission to cite.

### Aspirational targets (modest ranges, partially outside direct control)

- **20–50 GitHub stars** in the first 12 months
- **3–8 forks**
- **2–5 substantive issues or discussions** opened by external contributors
- **1–3 public mentions** of OpenTavu by people outside the author's direct network
- **3–5 public testimonials** from verifiable individuals (Microsoft MVPs, recognized Power Platform consultants, academic faculty) who have reviewed, commented on, or validated the framework

### Qualitative direction (no specific numbers)

- Independent adoption by consultants and integrators outside the author's direct network
- Recognition within the Microsoft Power Platform community as a reference framework for AI-enabled CRM in small and mid-market businesses
- Citations or references in academic or industry publications

These commitments will be reviewed and reported transparently in OpenTavu's public roadmap at opentavu.com.

---

## 11. How to Contribute and Adopt

OpenTavu is open-source under the MIT license, and the framework itself is free — this open contribution to the Power Platform ecosystem is the point. There is no commercial license tier or enterprise edition of the framework. Two adoption paths coexist: (a) a consultant or integrator downloads it from GitHub and deploys it into a client's own tenant, and (b) an optional **managed service** — offered by the author and, in time, other partners — in which the provider hosts the shared AI and scheduling infrastructure so a client with no technical team can adopt the CRM for a single per-user price. The managed service is a **vehicle for adoption and impact, not a gate on the technology**: the framework remains fully usable and free on its own. (Architecture of the managed layer is documented in `architecture.md`.)

### For SMB business leaders

If you are a small or mid-market business considering an AI-first CRM, OpenTavu is meant to be deployed by a consultant or integrator who downloads it, configures it for your tenant, and provides ongoing support. You will need:

- A Microsoft 365 tenant with Power Apps Premium licenses (per user)
- An Azure subscription with Azure OpenAI access (or willingness to provision one), or an alternative AI provider via OpenTavu's provider-agnostic interface — **unless** you adopt via the managed service, in which case the provider supplies the AI layer and you configure nothing
- An implementation partner — the author, a consultant from the Power Platform ecosystem, or your existing Microsoft partner

Contact the author through LinkedIn or via the repository's discussions for an introduction or implementation referral.

### For consultants and integrators

You can clone or fork the repository, evaluate OpenTavu, adapt it for a client engagement, and contribute improvements back. Contributions welcomed include:

- Bug reports and reproductions
- New AI modules or extensions of existing ones
- Documentation improvements, especially translations
- Implementation case studies (with client permission)
- Integration patterns for common SMB tooling (accounting, e-commerce, support tools, PSA suites)
- Alternative AI provider implementations against the provider-agnostic interface

Contribution guidelines are in `CONTRIBUTING.md`.

### For Microsoft MVPs and the Power Platform community

Reviews, technical critique, and feedback on architectural choices are explicitly invited. OpenTavu is opinionated, but the opinions are open to revision based on community input. Feel free to open an issue, write a review, or reach out directly.

### License and warranty

OpenTavu is released under the **MIT License**. It is provided as-is, without warranty of any kind, express or implied. This is a community framework; production deployments require independent evaluation, testing, and adaptation to the specific environment.

---

## 12. References

### Academic foundation

- González Villani, G., & Lasso Cortés, G. M. (2017). *Modelo para la selección, gestión y operación de sistemas que permitan efectuar la gestión de clientes en la nube para las PYMES* (Master's thesis, Advisor: Álvaro Pachón de la Cruz, PhD). Universidad Icesi, Cali, Colombia. *(Public repository URL to be linked.)*

### Industry and policy context

- The White House. (2025). *America's AI Action Plan*. Office of Science and Technology Policy.
- U.S. Small Business Administration. *Small Business Profile* (annual). https://advocacy.sba.gov

### Pain point evidence (industry comparative reviews and CRM analyses, 2025–2026)

- BigTime Software. (2026). *10 Best CRMs for Professional Services Firms — 2026 Guide*. https://www.bigtime.net/blogs/crm-for-professional-services/
- Certinia. (2025). *Why disconnected sales and resource planning processes lead to unreliable forecasts*. https://www.certinia.com/learn/
- CMap. *How CRM Software for Professional Services Can Transform Your Firm*. https://www.cmap.io/blog/how-crm-software-for-professional-services-can-transform-your-firm
- Capsule CRM. *Best CRM for Consulting Firms in 2025*. https://capsulecrm.com/blog/crm-for-consulting/
- Close. (2026). *How CRM reduces missed follow-ups in professional services*. https://www.close.com/blog/
- Consulting Success. (2025). *The Complete Guide to Consulting CRMs: 10 Top Tools Compared*. https://www.consultingsuccess.com/crms-for-consultants
- DelveAnt. (2026). *10 Best CRM for Professional Services in 2026*. https://delveant.com/blog/crm-for-professional-services/
- Drum. (2026). *7 Best CRMs for Professional Services in 2026 (Compared)*. https://getdrum.com/best-crm-for-professional-services
- 4Degrees. (2026). *The 9 Best CRM Systems for Consultants in 2025*. https://www.4degrees.ai/blog/the-9-best-crm-systems-for-consultants-in-2025
- G2. *Best CRM Software, User Reviews, April 2026*. https://www.g2.com/categories/crm
- Hey Dan. (2025). *Why CRM implementations fail in professional services firms*. https://www.heydan.ai/blog/
- NetHunt CRM. (2026). *7 Best CRM System for Consultants in 2026*. https://nethunt.com/blog/7-best-crm-for-consultants/
- OnePageCRM. (2025). *Best CRMs for Consultants in 2026*. https://www.onepagecrm.com/blog/consulting-crm/
- Productive. (2026). *Top 10 Consulting CRM in 2026 — Decision Guide*. https://productive.io/blog/consulting-crm/
- SigParser. (2025). *Why automatic email and meeting capture matters for professional services CRM*. https://www.sigparser.com/blog/
- Theo CRM. (2026). *Best CRM for Professional Services: Comparing Top 6 Platforms*. https://www.theocrm.com/resources/best-crm-for-professional-services-firms
- Zoma. (2025). *Why 90%+ of professional services teams still build SOWs manually* (LinkedIn industry post).

### Technical references

- Microsoft Learn. *Power Apps licensing overview*. https://learn.microsoft.com/en-us/power-platform/admin/pricing-billing-skus
- Microsoft Learn. *License requirements for tables*. https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-entity-licenses
- Microsoft Learn. *Restricted tables requiring Dynamics 365 licenses*. https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-restricted-entities
- Microsoft Learn. *Solutions in Power Apps and Application Lifecycle Management*. https://learn.microsoft.com/en-us/power-apps/maker/data-platform/solutions-overview
- Microsoft Learn. *Azure OpenAI Service Pricing*. https://azure.microsoft.com/en-us/pricing/details/azure-openai/
- Microsoft Learn. *Azure OpenAI Batch API*. https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/batch
- Microsoft Learn. *Microsoft Copilot for Sales licensing*. https://learn.microsoft.com/en-us/microsoft-sales-copilot/
- Microsoft Learn. *Custom Connectors with Microsoft Entra ID Authentication*. https://learn.microsoft.com/en-us/connectors/custom-connectors/azure-active-directory-authentication

---

## Document control

| Version | Date | Author | Notes |
|---|---|---|---|
| 0.1 | April 2026 | Gustavo González Villani | Foundational draft. Public for community feedback. |
| 0.2 | April 2026 | Gustavo González Villani | Evidence-anchored revision: added Section 3 (Pain Points); added Section 7 (Commercial Flow Design); replaced Lead Scoring with AI Activity Capture as Module 3; added provider-agnostic AI architecture; expanded functional coverage; added explicit CRM-not-PSA scope boundary; expanded References. |
| 0.3 | April 2026 | Gustavo González Villani | Naming and architecture revision: adopted "OpenKyte"; corrected data model to mixed-table architecture (`account`, `contact` standard + custom entities for licensing-restricted equivalents); added explicit rationale (Section 6). |
| 0.4 | April 2026 | Gustavo González Villani | Naming revision: "OpenKyte" replaced by "OpenTavu" after USPTO TESS verification (OpenKyte conflicted with Kyte Systems Inc. and Kyte Dynamics Inc. registrations in Class 042). USPTO TESS for "tavu" returned no live conflicts in software classes. Custom prefix changed to `tavu_`. Domain: opentavu.com. Tenant: opentavu.crm.dynamics.com. |
| 0.5 | April 2026 | Gustavo González Villani | Methodology and refinement revision: cross-source triangulation note added to Section 3; Zoma (2025) statistic added to Pain #3; explicit positioning subsection added to Section 6 addressing first-party AI assistants; AI-Assisted Forecasting added to roadmap; refined wording in Pain #2, #4, #5, #6 with additional source attributions; expanded References. |
| 0.6 | May 2026 | Gustavo González Villani | Second-source triangulation revision (Gemini Deep Research): added Pain #7 (RFP and questionnaire fatigue) anchored in industry data on average response times (~36 hours per RFP); reframed Module 2 as "Context-Aware Customer Communication Assistant" with explicit differentiation from Microsoft 365 Copilot based on depth of CRM-record integration; added "Token economics and cost-defensive patterns" subsection in Section 6 covering Azure OpenAI Batch API, token budgeting, confidence-gated operations, and configurable model tiers; expanded "first-party AI assistants" subsection to address depth-of-CRM-integration in addition to price-tier accessibility; promoted "AI RFP & Proposal Architect" to top of roadmap addressing Pain #3 and Pain #7; refined Module 3 description to mention Batch API for retroactive operations; refined Pain #1 with adoption study evidence; added Hey Dan (2025) and Microsoft Azure OpenAI Batch API references. |
| 0.7 | May 2026 | Gustavo González Villani | Architectural correction following critical review and design refinement: (1) Removed Lifecycle Stage from Contact (model inconsistency with B2B Professional Services where the commercial subject is the Account, not the Contact). (2) Added `tavu_iscustomer`, `tavu_customersince`, `tavu_lastengagementdate` on both Account and Contact, supporting B2B and B2C firms uniformly. (3) Added `tavu_engagementstatus` on Contact (Cold / Engaged / Inactive) — separate from contractual customer status. (4) Adopted hybrid customer field architecture for `tavu_opportunity` and `tavu_case`: polymorphic primary `tavu_customer` plus auto-populated typed `tavu_account` and `tavu_contact`, plus `tavu_primarycontact` for the human interlocutor. This matches Microsoft's pattern in Quote, Order, and Invoice. (5) Added `tavu_systemsettings` table for product-level configuration, starting with `tavu_customermode` (B2B_Only / B2C_Only / Mixed) controlling lookup filtering across the application. (6) Vertical target description explicitly extended to support B2B-only, B2C-only, and hybrid firms (legal boutiques, accountants with personal returns, coaches, wealth managers as valid examples). (7) Updated Section 7 narrative to clarify that "Single Lifecycle" refers to opportunity lifecycle, not a Stage field on Contact. (8) Updated functional coverage in Section 9 to reflect new customer status tracking model. |
| 0.8 | May 2026 | Gustavo González Villani | Sales pipeline architectural refinement to maximize multi-tenant deployability and prepare the data foundation for the AI-Assisted Forecasting roadmap module: (1) Replaced hardcoded `tavu_opportunity` status reasons (Discovery / Proposal Drafted / Proposal Sent / Negotiation) with a per-tenant configurable `tavu_salesstage` master table, each stage carrying its own default probability, forecast category (Pipeline / Best Case / Committed / Closed), display order, and color — recognizing that every Professional Services firm has its own pipeline vocabulary (e.g., RFI / Evaluation / RFP / Offer / Negotiation in bid-driven shops). Updated Section 7 data model bullet and Section 9 Sales functional coverage accordingly. (2) Simplified `tavu_opportunity.statuscode` to Open / Won / Lost only. (3) Removed `tavu_engagementtype` (One-time Project / Retainer / Ongoing / T&M) — taxonomy that did not address any documented pain point and that most firms defaulted to a single dominant value. (4) Strengthened the AI-Assisted Forecasting roadmap entry (Section 8) to explicitly describe how that future module will analyze per-tenant Won/Lost conversion ratios at each configured stage and propose data-driven adjustments to the admin-configured default probabilities. (5) Refined the depth-of-CRM-integration positioning paragraph (Section 6) to refer to "configurable pipeline stages" rather than "lifecycle stages". (6) Added `tavu_salesstage` to the Stack at a glance custom entities list. Detailed schema, plugin logic, and seed data for `tavu_salesstage` documented in `sales-model.md` v1.4. |
| 0.9 | June 2026 | Gustavo González Villani | Competitive positioning and roadmap refinement following review of an incumbent market-research report (HubSpot GTM 2026): (1) Added a "convergence with incumbent direction" paragraph to Section 6 making explicit that OpenTavu's defensibility rests on price tier + Microsoft/Dataverse ecosystem fit + open-source/configuration-over-code distribution — not on the context-grounded AI concept itself, which is now shared by well-funded incumbents. (2) Added "Channel-agnostic activity capture" to the Section 8 roadmap, framing additional channels (WhatsApp, SMS) as pluggable per-tenant connectors over Module 3's default Microsoft Graph binding, explicitly preserving the US Professional Services framing while accommodating non-US deployments without product-architecture changes. |
| 1.1 | July 2026 | Gustavo González Villani (revision with Claude) | Dispersed remaining conclusions from the HubSpot GTM 2026 report analysis. In this document: (1) framed **Module 3 explicitly as the foundational hygiene layer** — Modules 1/2 and every roadmap module depend on a clean, context-complete record layer (the "context gap"); it is a prerequisite, not a third add-on. (2) Added **"AI grounding transparency (context completeness)"** to the Section 8 roadmap, reserving the enabling design decision now (modules emit their grounding signal to a shared convention from the start) to avoid retrofitting telemetry. Evidence corroboration (HubSpot GTM 2026) lives in the triangulation doc; commercial strategy in the private `commercial-strategy.md`. |
| 1.0 | July 2026 | Gustavo González Villani (revision with Claude) | Commercial-model clarification: framed OpenTavu as two coexisting layers — the free MIT open-source framework (the contribution to the field) plus an **optional managed service** in which the provider hosts a shared AI/scheduling gateway so SMBs without a technical team adopt for a single per-user price. Positioned the managed service as a vehicle for adoption and impact, not a gate on the technology. Added a light note in Section 6 (provider-agnostic AI) that the same `IAIProvider` abstraction can point at a central AI gateway in the managed deployment. Detailed architecture (multi-tenant, S2S auth, config split, onboarding, SLA scheduler) lives in the new `architecture.md`. |

This document is meant to be revised as OpenTavu evolves. Substantive changes will be tracked here.
*Feedback and critique welcomed via the repository's Discussions tab or directly to the author through LinkedIn.*