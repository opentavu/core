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

Professional services firms — consultancies, agencies, boutique software shops — consistently lose revenue to the same operational failures:

- **CRM abandonment.** Consultants stop entering data because the system creates more work than it saves.
- **Manual case triage.** Every incoming request is routed by hand; no intelligent classification exists.
- **Context loss.** Call notes stay in email threads. The CRM holds a skeleton, not the actual relationship.
- **Proposal bottlenecks.** Proposals and SOWs are rebuilt from scratch for every opportunity.
- **Follow-up gaps.** Without automated reminders grounded in CRM context, deals stall silently.

These are not new problems. They persist because existing solutions are either too expensive (Dynamics 365 Sales Enterprise, Salesforce), too generic (HubSpot Free, Zoho), or too complex to configure for the realities of a 10–50-person professional services firm.

OpenTavu is designed specifically for this gap.

---

## Key design decisions

**Mixed-table architecture.** OpenTavu uses `account` and `contact` as standard Dataverse tables (no licensing restriction, naturally integrated with Microsoft 365 and Graph) and replaces licensing-restricted entities with custom equivalents: `tavu_lead`, `tavu_opportunity`, `tavu_case`, `tavu_proposal`, `tavu_knowledge_article`. This keeps the per-user cost at the Power Apps Premium tier (~$20/user/month) without sacrificing compatibility with Microsoft's ecosystem.

**Single Lifecycle, Dual Entry commercial flow.** A hybrid model between Dynamics 365's traditional Lead-qualify-convert pattern and HubSpot's modern lifecycle stage approach — adapted for how professional services firms actually work.

**Provider-agnostic AI layer.** AI invocations go through an `IAIProvider` interface. The default binding is Azure OpenAI. Alternative providers (Anthropic Claude, Google Gemini) are pluggable via configuration without changing the modules that consume them.

**Token-economics-first.** Cost-defensive patterns are built in from day one: Azure OpenAI Batch API for asynchronous workloads, per-module token budgets, confidence-gated operations, and configurable model tiers. An unexpected Azure bill ends SMB adoption faster than any technical defect.

---

## Stack

| Layer | Technology |
|---|---|
| Data platform | Microsoft Dataverse |
| Standard entities | `account`, `contact` (Dataverse standard + custom columns) |
| Custom entities | `tavu_lead`, `tavu_opportunity`, `tavu_case`, `tavu_proposal`, `tavu_knowledge_article`, `tavu_systemsettings`, configuration tables |
| App layer | Model-driven apps + Canvas apps (Power Apps Premium) |
| Automation | Power Automate + custom C# Workflow Activities |
| AI | Azure OpenAI (default, sync + Batch API); provider-agnostic `IAIProvider` interface |
| Analytics | Power BI |
| Distribution | Managed solution (.zip) via GitHub Releases |

---

## Initial AI Modules

OpenTavu launches with three AI modules, each independently deployable within the framework.

### Module 1 — Smart Case Categorization *(in active development)*
Automatically categorizes incoming cases into the firm's own business lines, categories, and subcategories. Routes to the correct queue or owner. Uses a custom C# Workflow Activity invoking Azure OpenAI with structured JSON prompts, confidence thresholds, and validation against active typifications. Addresses the single most common failure point in professional services CRM: the manual triage tax on every incoming request.

### Module 2 — Context-Aware Customer Communication Assistant *(target: Month 3)*
Generates first-draft responses grounded in the full CRM context: the customer's history, open opportunities, active cases, prior communications. Critically different from general-purpose AI assistants (including Microsoft 365 Copilot) in that it operates at the CRM-record level, not the email/user level — the draft knows what the client bought, what they complained about last quarter, and what is currently at risk.

### Module 3 — AI Activity Capture & CRM Hygiene Assistant *(target: Month 4)*
Reduces CRM abandonment by minimizing the manual overhead of keeping records current. Captures activity signals from email and meeting metadata, proposes structured updates to opportunity and account records, and surfaces relationships that have gone quiet. Uses the Azure OpenAI Batch API for retroactive and background processing to keep inference costs predictable.

---

## Roadmap modules

The following modules are documented on the roadmap and address the remaining identified pain points. They will not be built in the first 12 months but are planned:

- **AI RFP & Proposal Architect** — ingests RFP/DDQ documents, searches a corporate response library, assembles first proposal drafts. Highest strategic priority after the foundational modules.
- **AI Meeting Summarizer & Action Item Extractor** — processes Teams transcripts, extracts action items with owners and deadlines, updates opportunity records.
- **AI Relationship Health Monitor** — surfaces retainer relationships showing early cooling signals before churn occurs.
- **AI-Assisted Forecasting & Capacity Planning** — combines pipeline data with delivery commitments and team capacity.
- **Document Intelligence** — automated processing of contracts, invoices, NDAs.
- **Conversational AI Search** — natural-language querying over CRM data.

---

## Scope boundary

OpenTavu is a CRM framework, not a PSA (Professional Services Automation) tool. It does **not** implement invoicing, expense management, full project management, or detailed resource planning. It is designed to integrate with specialized tools in those areas (e.g., Harvest, Toggl Track, FreshBooks, Monday.com), not replace them.

---

## Academic foundation

OpenTavu's data model and functional scope are grounded in a 2017 master's thesis that developed and empirically validated a two-phase model for cloud CRM selection, management, and operation in SMBs — validated with three SMB case companies (score: 4.68/5) and an ITIL expert panel (score: 4.52/5).

> González Villani, G. & Lasso Cortés, G. M. (2017). *Modelo para la selección, gestión y operación de sistemas que permitan efectuar la gestión de clientes en la nube para las PYMES.* Master's thesis, Universidad Icesi. Advisor: Álvaro Pachón de la Cruz, PhD.

The pain points driving the current AI module design were validated through independent deep research with two separate AI systems (ChatGPT and Gemini) using a structured blind methodology — ensuring the problem framing is not self-referential.

---

## Project status

OpenTavu is in **early active development**. The data model and commercial flow design are complete and documented. Module 1 (Smart Case Categorization) is in active construction. The first managed solution release is targeted for Month 3.

This repository currently contains the foundational documentation. The managed solution (.zip) and technical implementation guide will be published as Releases when the first module reaches a deployable state.

---

## How to adopt

**If you are a business leader:** OpenTavu is meant to be deployed by a consultant who configures it for your tenant. You will need Power Apps Premium licenses and an Azure subscription with Azure OpenAI access. Contact the author via LinkedIn or open a Discussion in this repository for an implementation introduction.

**If you are a Power Platform consultant or integrator:** You can fork this repository, evaluate the framework, adapt it for a client engagement, and contribute improvements back. The vision document ([VISION.md](VISION.md)) describes the full design rationale, data model decisions, and AI module specifications.

**Prerequisites for deployment:**
- Microsoft 365 tenant
- Power Apps Premium licenses (per user — ~$20/user/month)
- Azure subscription with Azure OpenAI access (or an alternative AI provider via the `IAIProvider` interface)
- A consultant or integrator for configuration and customization

---

## How to contribute

OpenTavu is open-source under the MIT license. Contributions are welcome in any of the following forms:

- **Technical feedback** on the data model, commercial flow, or AI module design — open an Issue or Discussion
- **Power Platform implementation guidance** — pull requests to documentation or configuration
- **C# / Power Automate contributions** — once the first module is released, PRs welcome
- **Pilot deployments** — if you deploy OpenTavu in a professional services firm and are willing to share (anonymized) outcome data, please reach out

Please read the vision document before contributing to ensure alignment with the design principles.

---

## Public commitments (12 months)

- 2–3 professional services SMB pilot deployments
- Documented quantitative metrics from at least one pilot (with written permission)
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
