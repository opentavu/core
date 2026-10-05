# OpenTavu
### Open-source AI-first CRM framework for professional services SMBs, built on Microsoft Power Platform

**AI embedded in the core workflow, not bolted on. MIT licensed. Deployed into your own Microsoft tenant.**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Release: v1.1.0](https://img.shields.io/badge/Release-v1.1.0-green.svg)](https://github.com/opentavu/core/releases/tag/v1.1.0)
[![Platform: Power Platform](https://img.shields.io/badge/Platform-Microsoft%20Power%20Platform-742774.svg)]()
[![AI: Provider-agnostic](https://img.shields.io/badge/AI-Provider--agnostic-0078D4.svg)]()
[![Languages: EN | ES](https://img.shields.io/badge/UI-English%20%7C%20Espa%C3%B1ol-lightgrey.svg)]()

---

## What is OpenTavu?

OpenTavu is a production-grade, AI-first CRM framework for **professional services SMBs** (engineering and architecture practices, B2B agencies, legal and accounting firms, QA boutiques, and IT and management consultancies) that need more than a spreadsheet but cannot justify the cost and complexity of enterprise CRM platforms.

It is delivered as a **Microsoft Power Platform managed solution** that a consultant or integrator deploys into a client's Dataverse environment. It includes a purpose-built data model, AI features that automate high-friction workflows, automation flows, and complete documentation.

OpenTavu is **not a hosted SaaS product**. There is no fee for the framework itself. You bring your own Microsoft 365 subscription and AI provider; OpenTavu is the framework that turns them into an AI-first CRM.

> **Full product vision:** [VISION.md](VISION.md)

---

## Get started

1. Download the managed solutions from the latest release: [**OpenTavu v1.1.0**](https://github.com/opentavu/core/releases/tag/v1.1.0). The **Core** package is all you need to start; it installs needing only the Dataverse connection.
2. (Optional) Add the **OpenTavu Integrations** package when you want the flows that use email, Microsoft Forms, and OneDrive. Install Core first, then Integrations.
3. Follow the [installation guide](docs/installation.md) (prerequisites, import, verification, upgrades).
4. Follow the [configuration guide](docs/configuration.md) (AI wiring, system settings, security, SLA matrix, smoke test).

Import into a sandbox environment first, run the smoke test, then promote to production.

---

## The problem it solves

Professional services firms consistently lose revenue to the same operational failures:

- **CRM abandonment.** Consultants stop entering data because the system creates more work than it saves.
- **Manual case triage.** Every incoming request is routed by hand; no intelligent classification exists.
- **Context loss.** Call notes stay in email threads. The CRM holds a skeleton, not the actual relationship.
- **Proposal bottlenecks.** Proposals and SOWs are rebuilt from scratch for every opportunity.
- **Follow-up gaps.** Without automated reminders grounded in CRM context, deals stall silently.
- **RFP fatigue.** Small teams spend an average of 36 hours per RFP response with no intelligent assistance.

These problems persist because existing solutions are either too expensive (Dynamics 365 Sales Enterprise, Salesforce), too generic (HubSpot Free, Zoho), or too complex to configure for a 10 to 50 person firm. OpenTavu is designed specifically for this gap.

---

## Key design decisions

**Mixed-table architecture.** OpenTavu uses `account` and `contact` as standard Dataverse tables (no licensing restriction, naturally integrated with Microsoft 365 and Graph) and replaces licensing-restricted entities with custom `tavu_*` equivalents. This keeps the per-user cost at the Power Apps Premium tier (about US$20/user/month) without sacrificing Microsoft ecosystem compatibility, and it is a stepping stone, not a dead end, for clients who later upgrade to Dynamics 365.

**Single Lifecycle, Dual Entry commercial flow.** A hybrid between Dynamics 365's traditional lead-qualify-convert pattern and HubSpot's lifecycle approach, adapted to how professional services firms actually work. The `tavu_lead` table exists only as an ingestion buffer for anonymous inbound; direct networking contacts bypass it entirely.

**B2B, B2C, and hybrid customer support.** The `tavu_customer` polymorphic field (with auto-populated typed lookups `tavu_account` and `tavu_contact`) supports all three firm types through a single `tavu_systemsettings.tavu_customermode` flag, with no artificial adaptations.

**Configuration over code.** Pipeline stages, case types, SLAs, customer tiers, business lines, and AI tasks live in configuration tables, so each firm adapts OpenTavu from the app instead of forking the code.

**Provider-agnostic AI layer.** AI invocations go through an `IAIProvider` interface. Azure OpenAI is the default; OpenAI, Anthropic, Google Gemini, or a local model are pluggable through the gateway or direct mode, without changing the features that consume them.

**Token-economics-first.** Cost-defensive patterns are built in from day one: Batch API for asynchronous workloads, per-task token budgets, confidence-gated operations, and configurable model tiers.

---

## Stack

| Layer | Technology |
|---|---|
| Data platform | Microsoft Dataverse |
| Standard entities | `account`, `contact` (with custom columns) |
| Custom entities | `tavu_lead`, `tavu_opportunity`, `tavu_proposal`, `tavu_case`, `tavu_knowledge_article`, `tavu_systemsettings`, forecasting tables, and supporting configuration tables |
| App layer | Model-driven app + PCF controls (Power Apps Premium) |
| Automation | C# plugins and Custom APIs, Power Automate flows |
| AI | Provider-agnostic `IAIProvider` (Azure OpenAI default); per-task configuration in `tavu_aitaskconfiguration` |
| AI gateway (optional) | Open-source [reference gateway](https://github.com/opentavu/gateway) (Azure Functions, .NET 8, MIT), self-hosted with your own keys; or direct mode without a gateway |
| Analytics | Native model-driven dashboards and PCF chart controls; no per-user BI license required |
| Localization | English and Spanish (LCID 3082) |
| Distribution | Managed solutions (.zip) via GitHub Releases: Core (installs with the Dataverse connection only) plus an optional Integrations module (email, Forms, OneDrive flows) |

---

## Data model

OpenTavu's data model covers two operational areas, **Sales** and **Service**, sharing the same `account` and `contact` foundation.

### Sales model: Single Lifecycle, Dual Entry

The commercial flow supports two entry paths: direct contact creation (networking and referrals, the common case in professional services) and anonymous inbound buffering through `tavu_lead`. The lifecycle of opportunities is the single source of commercial truth.

| Table | Role | Who edits |
|---|---|---|
| `account` | Corporate client accounts with assigned Customer Tier | Sales / Ops during onboarding |
| `contact` | People; carries engagement status and customer flags (`tavu_iscustomer`, `tavu_engagementstatus`) | Sales daily |
| `tavu_lead` | Ingestion buffer for anonymous inbound only (web forms, cold emails) | System (AI) first, then sales |
| `tavu_opportunity` | Discovery-driven sales pipeline with per-firm configurable stages (`tavu_salesstage`) | Sales / Sales Manager |
| `tavu_opportunityclose` | Historical log of every close attempt (Won / Lost / Reopen) | Sales via guided pop-up |
| `tavu_proposal` | SOWs and proposals linked to opportunities (one opportunity, many proposals), with versioning | Sales |

#### Quotation model

The proposal module includes a complete quotation layer with multi-currency support, kit bundling, and role-based margin visibility.

| Table | Role |
|---|---|
| `tavu_proposalline` | The seller's single grid: one row per service, license, or kit |
| `tavu_product` | Master catalog of services, licenses, and kits (`tavu_iskit` flag) |
| `tavu_uom` | Units of measure with conversion schedule (Hour, Day, Month, License, Unit) |
| `tavu_kitcomponent` | Kit bill of materials: internal composition, never exposed to the client |
| `tavu_pricelist` + `tavu_pricelistitem` | Multi-currency price lists |
| `tavu_servicerole` | Delivery roles with default rate and cost per profile |

Design decisions in the quotation model: kits are single-level in MVP (BOM expansion happens in memory at PDF generation time, never written back to Dataverse); tax is a manual decimal field; gross margin and total cost fields are hidden from sellers via Field Security Profile; reference data is created after import by the **Verify Setup** button on System Settings (a managed solution carries no rows).

#### Forecasting model (new in v1.0.0)

| Table | Role |
|---|---|
| `tavu_salesperiod` | Target periods (Month or Quarter), grouped by fiscal year |
| `tavu_salestarget` | Revenue targets per scope (User, Business Line, Company) and period, with attainment rollups and a Manager Adjusted Amount |
| `tavu_goaltype` | Goal type catalog (Revenue in this release) |
| `tavu_forecastsnapshot` | Point-in-time history for pacing |
| `tavu_reportingcache` | Pre-aggregated current state that feeds the forecasting dashboard |

Forecast categories (Omitted / Pipeline / Best Case / Committed / Closed) default from the opportunity's stage and are settable per opportunity. Omitted deals are excluded from open pipeline, coverage, and forecast until they advance.

---

### Service model: AI-first case management with configurable SLA

| Table | Role | Who edits |
|---|---|---|
| `tavu_customertierdefinition` | Client tier catalog | Admin during setup |
| `tavu_casetype` | Inquiry type catalog; each type carries a `tavu_aihint` that feeds the categorization prompt | Admin during setup |
| `tavu_sla` | SLA matrix: response and resolution targets per Tier and Type combination | Admin during setup |
| `tavu_case` | Incoming cases; AI writes categorization, confidence score, and reasoning fields | AI + consultants |
| `tavu_caseinteraction` | The case conversation: inbound, outbound, and internal notes, with attachments | Consultants + email intake |
| `tavu_timeentry` | Time worked against cases and opportunities; accumulates into `tavu_actualhours` | Consultants daily |

The core service loop: a case arrives, AI categorizes it with a confidence score, the system looks up the matching SLA (Tier and Type), applies business-calendar response and resolution targets, assigns it to a queue, the consultant works it, and the case resolves.

Cases below the confidence threshold (default: 0.85) are flagged for human review rather than auto-applied.

#### Reference data

A managed solution carries tables, plugins and flows, but no rows. After import, the **Verify Setup** button on System Settings (Custom API `tavu_InitializeConfiguration`, plugin `Pl.Setup.Initialize`) creates the reference data the engine needs and checks the install: case statuses with their behavior flags, case types, customer tiers, a Standard 8x5 business calendar in the installer's time zone, the SLA matrix, a starter case taxonomy, sales stages, the Revenue goal type, units of measure, the AI task prompts, and an optional geography pack (United States and Colombia). It is idempotent: it matches rows by code or name, only fills empty fields, and never overwrites what the firm changed, so it is safe to run after every upgrade. The data lives in [`opentavu-seed.json`](src/Plugins/Pl.Setup.Initialize/Seed/opentavu-seed.json).

Case types:

| Name | Code | Default Priority |
|---|---|---|
| General Inquiry (default) | CT-1001 | Standard |
| Support Request | CT-1002 | Standard |
| RFP/Proposal Inquiry | CT-1003 | Expedited |
| Billing Inquiry | CT-1004 | Standard |
| Scope Change Request | CT-1005 | Expedited |
| Complaint | CT-1006 | Critical |
| Other | CT-1007 | Standard |

Customer tiers:

| Name | Code | Sort Order |
|---|---|---|
| Strategic | CTD-1002 | 10 |
| Premium | CTD-1001 | 20 |
| Standard (default) | CTD-1000 | 30 |

Firms extend or rename these freely; the engine resolves them by flags and codes, not by names.

---

### Shared configuration

| Table | Role |
|---|---|
| `tavu_systemsettings` | Tenant-level settings, including `tavu_customermode` (B2B_Only / B2C_Only / Mixed), AI kill switch and defaults, lead buffer aging, and forecasting cadence |
| `tavu_companyprofile` | The firm's branding (logo, accent color, default proposal terms) used in generated documents |
| `tavu_aitaskconfiguration` | One row per AI task: model, temperature, confidence threshold, max output tokens, and prompt |

---

## Capabilities (in client lifecycle order)

OpenTavu's AI is organized as three modules for design and evidence purposes: Module 1 (Smart Case Categorization), Module 2 (Context-Aware Communication), and Module 3 (Activity Capture and CRM Hygiene). The module numbers are identity labels; the capabilities below follow the client lifecycle, sales first. Every AI feature proposes and a human confirms before a new master record is created or a client email is sent.

### Lead Triage *(live, Module 3 Part A)*

Reads each anonymous inbound lead in the `tavu_lead` buffer (for example from a web form), matches it against existing contacts and accounts, and recommends promote, link, or discard. Creating a new master record always needs a one-click human approval (`Pl.Lead.Triage` plus the `tavu_PromoteLead` action). Leads that sit unattended age from Fresh to Aging to Stale automatically.

**Pain addressed:** manual CRM entry and low-quality inbound (Pain #1).

### Meeting Capture *(live, MVP, Module 3 Part B)*

Captures a client meeting transcript (Teams native, with manual paste as a first-class fallback), extracts the discovery notes with AI, flags potential clients and their company, lets the rep create an opportunity (need, contact, account) from the meeting with one button (`Pl.Meeting.Capture` plus `tavu_AssociateMeeting`), and drafts a follow-up email to review and send. A setup wizard diagnoses the Teams transcript prerequisites. Capture-only MVP; OpenTavu does not schedule meetings.

**Pain addressed:** context lost after meetings (Pain #6), manual CRM entry (Pain #1), and follow-up discipline (Pain #4).

### Proposal email draft *(live, Module 2 at a gate)*

When a proposal is sent (Send to Client), OpenTavu writes the client email (AI body grounded in the proposal) and attaches a branded PDF for the seller to review and send. Config-gated by `tavu_proposalemaildraftenabled` (default on). This is the Context-Aware Communication pattern applied at a concrete gate; broader case and opportunity drafting stays on the roadmap.

**Pain addressed:** follow-up discipline, context loss, and slow proposal turnaround.

### Sales forecasting *(live in v1.0.0, deterministic layer)*

Revenue targets per user, business line, and company for monthly or quarterly periods, with attainment, gap to goal, pipeline coverage, forecast categories, manager adjustments, and snapshot-based pacing, shown in a native dashboard control. This layer uses no AI and needs no historical data. The AI layer (probability calibration from each firm's own win rates and deal-risk detection) is on the roadmap.

**Pain addressed:** disconnection between sales and delivery planning (Pain #2).

### Smart Case Categorization *(live, Module 1)*

Once a prospect becomes a client, every incoming case is read, categorized into the firm's own business lines, categories, and subcategories, and routed to the correct queue or owner. `Pl.Case.Categorize` builds a structured JSON prompt from the case content and the firm's active typification hierarchy, validates the result against active typifications, and either auto-applies it or flags the case for human review by confidence.

**Pain addressed:** manual triage on every incoming request, a top CRM abandonment driver in professional services.

**Status:** originally production-tested in a prior enterprise deployment (Azure OpenAI + Dynamics 365 Customer Service); the OpenTavu version is an abstracted, generalized re-implementation.

> **Gateway note.** AI features run through the open-source reference gateway or in direct mode. Email-to-case intake, Teams transcript sync, and SLA timer transitions (Warning / Breached) currently run in the hosted gateway and are being published to the [reference gateway](https://github.com/opentavu/gateway) so that every capability can be self-hosted.

---

## Roadmap

- **AI Lead Scoring:** optional module for firms with higher inbound volume.
- **Channel-agnostic activity capture:** additional capture channels (for example WhatsApp Business or SMS) as per-tenant connectors over the same capture architecture.
- **AI Meeting Summarizer & Action Item Extractor:** extracts action items from meetings and writes structured updates back to opportunity records.
- **AI Relationship Health Monitor:** surfaces retainer relationships showing early cooling signals before churn.
- **AI RFP & Proposal Architect:** ingests RFP and DDQ documents, searches a corporate response library, and assembles first proposal drafts. Addresses the 36-hour-per-RFP bottleneck.
- **AI-Assisted Forecasting, AI layer:** probability calibration from each firm's Won/Lost history and deal-risk detection over captured activity.
- **Document Intelligence:** automated processing of contracts, invoices, and NDAs.
- **Conversational AI Search:** natural-language querying over CRM data.

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

OpenTavu's data model and functional scope are grounded in a 2017 master's thesis that developed and empirically validated a two-phase model for cloud CRM selection, management, and operation in SMBs, validated with three SMB case companies (score: 4.68/5) and an ITIL expert panel (score: 4.52/5).

> González Villani, G. & Lasso Cortés, G. M. (2017). *Modelo para la selección, gestión y operación de sistemas que permitan efectuar la gestión de clientes en la nube para las PYMES.* Master's thesis, Universidad Icesi. Advisor: Álvaro Pachón de la Cruz, PhD. https://hdl.handle.net/10906/130635

The pain points driving the AI design were validated through independent deep research with two separate AI systems (ChatGPT and Gemini) using a structured blind methodology, so the problem framing is not self-referential.

---

## Project status

**v1.0.0, the first public release, shipped on September 21, 2026.** It includes the core data model, the full sales cycle (leads, opportunities with the close engine, proposals with quotation and versioning), the service model (cases, SLA matrix, case conversation), the live AI features above (Lead Triage, Meeting Capture, proposal email draft, Smart Case Categorization), deterministic sales forecasting, Spanish localization, and complete installation and configuration guides.

**v1.1.0 repackages the release into a Core package plus an optional Integrations module**, so the Core installs needing only the Dataverse connection (the email, Microsoft Forms, and OneDrive flows moved to the optional module, and Spanish now covers the forecasting module too). Install order is Core first, then Integrations.

Known limitations are listed in the [release notes](https://github.com/opentavu/core/releases/tag/v1.1.0). Work continues on the roadmap and on hardening the live features across more tenants and clients.

---

## How to adopt

**If you are a business leader:** OpenTavu is deployed by a consultant who configures it for your tenant. You will need Power Apps Premium licenses and an AI provider (Azure OpenAI by default, or an alternative). Contact the author via LinkedIn or open a Discussion in this repository.

**If you are a Power Platform consultant or integrator:** download the [latest release](https://github.com/opentavu/core/releases), evaluate the framework, adapt it for a client engagement, and contribute improvements back. The [vision document](VISION.md) and operational guides (`docs/sales-model.md`, `docs/service-model.md`) describe the full design rationale and specifications. Deploy with the [installation guide](docs/installation.md) and the [configuration guide](docs/configuration.md).

**Prerequisites:**
- Microsoft 365 tenant and a Dataverse environment with a database
- Power Apps Premium licenses (about US$20/user/month)
- An AI provider: Azure OpenAI (default), or OpenAI, Anthropic, Google Gemini, or a local model through the gateway or direct mode
- System Administrator rights in the target environment
- A consultant or integrator for configuration and customization

---

## How to contribute

OpenTavu is open source under the MIT license. Contributions welcome:

- **Technical feedback** on the data model or AI design: open an Issue or Discussion
- **Documentation:** pull requests to the installation, configuration, and operational guides, including translations
- **C# / Power Automate contributions:** pull requests welcome
- **Pilot deployments:** if you deploy OpenTavu and are willing to share anonymized outcome data, please reach out

Please read the vision document and operational guides before contributing.

---

## Public commitments (12 months)

- 2 to 3 professional services SMB pilot deployments with documented outcomes
- Quantitative metrics from at least one pilot (with written client permission)
- 3 to 5 long-form technical articles (LinkedIn / Microsoft Tech Community)
- 1 to 2 talks at Power Platform user groups or technical conferences
- Active repository with documented releases and complete technical documentation

---

## License
This project is licensed under the **MIT License**: free for consultants and businesses to use, fork, and adapt.

## Contact & Community
**Gustavo González Villani**, Founder & Architect | MSc. Gestión de Informática y Telecomunicaciones  
[LinkedIn](https://www.linkedin.com/in/gustavogonzalezvillani) | 📧 [gg@opentavu.com](mailto:gg@opentavu.com) | 🌐 [opentavu.com](https://opentavu.com)

---
*OpenTavu is a community-driven framework. We welcome feedback from MVPs, integrators, and the Power Platform community.*
