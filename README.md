# Opentavu 🚀
### The AI-First SMB CRM Accelerator

**An open-source CRM framework for small and mid-market professional services firms — built on Microsoft Power Platform with AI embedded in the core workflow, not bolted on.**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Status: Early Development](https://img.shields.io/badge/Status-Early%20Development-orange.svg)]()
[![Platform: Power Platform](https://img.shields.io/badge/Platform-Microsoft%20Power%20Platform-742774.svg)]()
[![AI: Azure OpenAI](https://img.shields.io/badge/AI-Azure%20OpenAI-0078D4.svg)]()

---

## What is OpenTavu?

OpenTavu is a production-grade, AI-first CRM foundation for **professional services SMBs** — IT consultancies, B2B agencies, software/QA boutiques, and similar firms — that need more than a spreadsheet but cannot justify the cost and complexity of enterprise CRM platforms.

It is delivered as a **Microsoft Power Platform managed solution** that a consultant or integrator deploys into a client's Dataverse tenant in hours, not months. It includes a purpose-built data model, AI modules that automate high-friction workflows, automation flows, and complete documentation.

OpenTavu is **not a hosted SaaS product**. There is no monthly fee for the framework itself. You bring your own Microsoft 365 and Azure subscriptions; OpenTavu is the accelerator that makes them work together as an AI-first CRM.

> **Full product vision:** [VISION.md](VISION.md)

---

## The problem it solves

Professional services firms consistently lose revenue to the same operational failures:

- **CRM abandonment.** Consultants stop entering data because the system creates more work than it saves.
- **Manual case triage.** Every incoming request is routed by hand; no intelligent classification exists.
- **Context loss.** Call notes stay in email threads. The CRM holds a skeleton, not the actual relationship.
- **Proposal bottlenecks.** Proposals and SOWs are rebuilt from scratch for every opportunity.
- **Follow-up gaps.** Without automated reminders grounded in CRM context, deals stall silently.
- **RFP fatigue.** Small teams spend an average of 36 hours per RFP response with no intelligent assistance.

These problems persist because existing solutions are either too expensive (Dynamics 365 Sales Enterprise, Salesforce), too generic (HubSpot Free, Zoho), or too complex to configure for a 10–50-person firm. OpenTavu is designed specifically for this gap.

---

## Key design decisions

**Mixed-table architecture.** OpenTavu uses `account` and `contact` as standard Dataverse tables (no licensing restriction, naturally integrated with Microsoft 365 and Graph) and replaces licensing-restricted entities with custom `tavu_*` equivalents. This keeps the per-user cost at the Power Apps Premium tier (~$20/user/month) without sacrificing Microsoft ecosystem compatibility — and creates a stepping stone, not a dead-end, for clients who later upgrade to Dynamics 365.

**Single Lifecycle, Dual Entry commercial flow.** A hybrid between Dynamics 365's traditional Lead-qualify-convert pattern and HubSpot's modern lifecycle stage approach — adapted for how professional services firms actually work. The `tavu_lead` table exists only as an ingestion buffer for anonymous inbound; direct networking contacts bypass it entirely.

**B2B, B2C, and hybrid customer support.** The `tavu_customer` polymorphic field (with auto-populated typed lookups `tavu_account` and `tavu_contact`) supports all three firm types through a single `tavu_systemsettings.tavu_customermode` flag — no artificial adaptations required.

**Provider-agnostic AI layer.** AI invocations go through an `IAIProvider` interface. The default binding is Azure OpenAI. Alternative providers (Anthropic Claude, Google Gemini) are pluggable via configuration without changing the modules that consume them.

**Token-economics-first.** Cost-defensive patterns are built in from day one: Azure OpenAI Batch API for asynchronous workloads, per-module token budgets, confidence-gated operations, and configurable model tiers.

---

## Stack

| Layer | Technology |
|---|---|
| Data platform | Microsoft Dataverse |
| Standard entities | `account`, `contact` (with custom columns) |
| Custom entities | `tavu_lead`, `tavu_opportunity`, `tavu_case`, `tavu_proposal`, `tavu_knowledge_article`, `tavu_systemsettings`, and supporting tables |
| App layer | Model-driven apps + Canvas apps (Power Apps Premium) |
| Automation | Power Automate + custom C# Workflow Activities |
| AI | Azure OpenAI (default, sync + Batch API); provider-agnostic `IAIProvider` interface |
| Analytics | Power BI |
| Distribution | Managed solution (.zip) via GitHub Releases |

---

## Data model

OpenTavu's data model covers two operational areas — **Sales** and **Service** — sharing the same `account` and `contact` foundation.

### Sales model — Single Lifecycle, Dual Entry

The commercial flow supports two entry paths: direct contact creation (networking, referrals — the common case in professional services) and anonymous inbound buffering through `tavu_lead`. The lifecycle of opportunities is the single source of commercial truth.

| Table | Role | Who edits |
|---|---|---|
| `account` | Corporate client accounts with assigned Customer Tier | Sales / Ops during onboarding |
| `contact` | People; carries engagement status and customer flags (`tavu_iscustomer`, `tavu_engagementstatus`) | Sales daily |
| `tavu_lead` | Ingestion buffer for anonymous inbound only (web forms, cold emails) | System (AI) first, then sales |
| `tavu_opportunity` | Discovery-driven sales pipeline | Sales / Sales Manager |
| `tavu_opportunityclose` | Historical log of every close attempt (Won / Lost / Reopen) | Sales via guided pop-up |
| `tavu_proposal` | SOWs and proposals linked to opportunities (one opportunity → many proposals) | Sales + future AI Proposal Generator |

#### Quotation model

The proposal module includes a complete quotation layer with multi-currency support, kit bundling, and role-based margin visibility.

| Table | Role |
|---|---|
| `tavu_proposalline` | The seller's single grid — one row per service, license, or kit |
| `tavu_product` | Master catalog of services, licenses, and kits (`tavu_iskit` flag) |
| `tavu_uom` | Units of measure with conversion schedule (Hour, Day, Month, License, Unit) |
| `tavu_kitcomponent` | Kit bill-of-materials — internal composition, never exposed to the client |
| `tavu_pricelist` + `tavu_pricelistitem` | Multi-currency price lists |
| `tavu_servicerole` | Delivery roles with default rate and cost per profile |

Design decisions in the quotation model: kits are single-level in MVP (BOM expansion happens in memory at PDF generation time, never written back to Dataverse); tax is a manual decimal field; gross margin and total cost fields are hidden from sellers via Field Security Profile; all seed data ships pre-loaded in the managed solution.

---

### Service model — AI-first case management with configurable SLA

| Table | Role | Who edits |
|---|---|---|
| `tavu_customertierdefinition` | Client tier catalog | Admin during setup |
| `tavu_casetype` | Inquiry type catalog; each type carries an `tavu_aihint` that feeds Module 1's AI prompt | Admin during setup |
| `tavu_sla` | SLA matrix: response and resolution targets per Tier × Type combination | Admin during setup |
| `tavu_case` | Incoming cases; AI writes categorization, confidence score, and reasoning fields | AI (Module 1) + consultants |
| `tavu_timeentry` | Time worked against cases and opportunities; accumulates into `tavu_actualhours` | Consultants daily |

The core service loop: a case arrives → Module 1 categorizes it with a confidence score → system looks up the matching SLA (Tier × Type) → applies response and resolution targets → assigns to queue → consultant works → time entries accumulate → case resolves.

Cases below the confidence threshold (default: 0.85) are flagged for human review rather than auto-applied.

#### Case type seed data (pre-loaded)

| Name | Code | Default Priority |
|---|---|---|
| General Inquiry | GEN | Standard |
| Support Request | SUP | Standard |
| RFP / Proposal Inquiry | RFP | Expedited |
| Billing Inquiry | BIL | Standard |
| Scope Change Request | SCO | Expedited |
| Complaint | CMP | Critical |
| Other | OTH | Standard |

#### Customer tier seed data (pre-loaded)

| Name | Sort Order | Description |
|---|---|---|
| Standard | 100 | Default tier for regular clients |
| Premium | 50 | Clients with extended SLA or preferred contracts |
| Strategic | 10 | Top-tier — maximum priority |

Both seed datasets ship pre-loaded in the managed solution and can be extended without modifying defaults.

---

### Shared configuration

| Table | Role |
|---|---|
| `tavu_systemsettings` | Tenant-level settings including `tavu_customermode` (B2B_Only / B2C_Only / Mixed) |

The `tavu_customermode` flag controls the behavior of the `tavu_customer` polymorphic lookup across opportunities and cases. Changing from B2B_Only to Mixed does not affect existing records and requires no downtime.

---

## Initial AI Modules

### Module 1 — Smart Case Categorization *(in active development)*

Automatically categorizes incoming cases into the firm's own business lines, categories, and subcategories. Routes to the correct queue or owner. A custom C# Workflow Activity invokes Azure OpenAI with a structured JSON prompt that includes the case content and the firm's active typification hierarchy. The model returns categorization with a confidence score; the activity validates against active typifications and either auto-applies or flags for human review.

**Pain addressed:** manual triage on every incoming request — the most documented CRM abandonment driver in professional services.

**Status:** production-tested in a prior enterprise deployment (Azure OpenAI + Dynamics 365 Customer Service). The OpenTavu version is an abstracted, generalized re-implementation.

### Module 2 — Context-Aware Customer Communication Assistant *(target: Month 3)*

Generates first-draft responses grounded in the full CRM context: customer history, open opportunities, active cases, prior communications. Critically different from general-purpose AI assistants (including Microsoft 365 Copilot) in that it operates at the CRM-record level — the draft knows what the client bought, what they complained about last quarter, and what is currently at risk.

**Pain addressed:** follow-up discipline and context loss.

### Module 3 — AI Activity Capture & CRM Hygiene Assistant *(target: Month 4)*

Captures activity signals from email and meeting metadata, proposes structured updates to opportunity and account records, and surfaces relationships that have gone quiet. Uses the Azure OpenAI Batch API for retroactive and background processing.

**Pain addressed:** CRM abandonment — the root cause of most CRM implementation failures in SMBs.

---

## Roadmap modules

- **AI RFP & Proposal Architect** — highest strategic priority after foundational modules. Ingests RFP/DDQ documents, searches a corporate response library, assembles first proposal drafts. Addresses the 36-hour-per-RFP bottleneck.
- **AI Meeting Summarizer & Action Item Extractor** — processes Teams transcripts, extracts action items, writes structured updates back to opportunity records.
- **AI Relationship Health Monitor** — surfaces retainer relationships showing early cooling signals before churn.
- **AI-Assisted Forecasting & Capacity Planning** — combines pipeline data with delivery commitments and team capacity.
- **Document Intelligence** — automated processing of contracts, invoices, NDAs.
- **Conversational AI Search** — natural-language querying over CRM data.
- **AI Lead Scoring** — optional module for firms with higher inbound volume.

---

## Scope boundary

OpenTavu is a CRM framework, not a PSA tool. It does **not** implement invoicing, expense management, full project management, or resource planning. It integrates with specialized tools in those areas rather than replacing them.

---

## Configuration for different firm types

| Firm type | Tiers | Case types | SLA records | Customer mode |
|---|---|---|---|---|
| Small IT consultancy (12 people, B2B) | 3 | 5 | 3 | B2B_Only |
| Mid-size B2B agency (25 people) | 3 | 7 | 8 | B2B_Only |
| Software QA boutique (40 people) | 4 (incl. Trial) | 9 (incl. Bug Report, Test Cycle) | 12 | B2B_Only |
| Legal boutique (8 people, hybrid) | 2 | 6 | 6 | Mixed |

---

## Academic foundation

OpenTavu's data model and functional scope are grounded in a 2017 master's thesis that developed and empirically validated a two-phase model for cloud CRM selection, management, and operation in SMBs — validated with three SMB case companies (score: 4.68/5) and an ITIL expert panel (score: 4.52/5).

> González Villani, G. & Lasso Cortés, G. M. (2017). *Modelo para la selección, gestión y operación de sistemas que permitan efectuar la gestión de clientes en la nube para las PYMES.* Master's thesis, Universidad Icesi. Advisor: Álvaro Pachón de la Cruz, PhD.

The pain points driving the AI module design were validated through independent deep research with two separate AI systems (ChatGPT and Gemini) using a structured blind methodology — ensuring the problem framing is not self-referential.

---

## Project status

OpenTavu is in **early active development**. The data model and commercial flow design are complete and documented. Module 1 is in active construction. The first managed solution release is targeted for Month 3.

This repository currently contains foundational documentation. The managed solution (.zip) and implementation guide will be published as Releases when the first module reaches a deployable state.

---

## How to adopt

**If you are a business leader:** OpenTavu is deployed by a consultant who configures it for your tenant. You will need Power Apps Premium licenses and an Azure subscription with Azure OpenAI access. Contact the author via LinkedIn or open a Discussion in this repository.

**If you are a Power Platform consultant or integrator:** Fork the repository, evaluate the framework, adapt it for a client engagement, and contribute improvements back. The [vision document](VISION.md) and operational guides (`docs/sales-model.md`, `docs/service-model.md`) describe the full design rationale and specifications. To deploy OpenTavu into a client tenant, follow the [installation guide](docs/installation.md) and the [configuration guide](docs/configuration.md).

**Prerequisites:**
- Microsoft 365 tenant
- Power Apps Premium licenses (~$20/user/month)
- Azure subscription with Azure OpenAI access (or an alternative AI provider via `IAIProvider`)
- A consultant or integrator for configuration and customization

---

## How to contribute

OpenTavu is open-source under the MIT license. Contributions welcome:

- **Technical feedback** on the data model or AI module design — open an Issue or Discussion
- **Power Platform documentation** — pull requests to configuration guides
- **C# / Power Automate contributions** — PRs welcome once the first module is released
- **Pilot deployments** — if you deploy OpenTavu and are willing to share anonymized outcome data, please reach out

Please read the vision document and operational guides before contributing.

---

## Public commitments (12 months)

- 2–3 professional services SMB pilot deployments with documented outcomes
- Quantitative metrics from at least one pilot (with written client permission)
- 3–5 long-form technical articles (LinkedIn / Microsoft Tech Community)
- 1–2 talks at Power Platform user groups or technical conferences
- Active repository with documented releases and complete technical documentation

---
## 📄 License
This project is licensed under the **MIT License**—free for consultants and businesses to use, fork, and adapt.

## 📩 Contact & Community
**Gustavo González Villani** Founder & Architect | MSc. Gestión de Informática y Telecomunicaciones  
[LinkedIn](https://www.linkedin.com/in/gustavogonzalezvillani) | 📧 [gg@opentavu.com](mailto:gg@opentavu.com) | 🌐 [opentavu.com](https://opentavu.com)

---
*Opentavu is a community-driven framework. We welcome feedback from MVPs, integrators, and the Power Platform community.*
