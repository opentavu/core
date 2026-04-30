# Opentavu 🚀
### The AI-First SMB CRM Accelerator

**Opentavu** is a production-grade, open-source framework (MIT License) designed to bridge the gap between traditional CRM systems and modern Generative AI. Built natively on the **Microsoft Power Platform** and **Azure OpenAI**, it provides a scalable, cost-effective foundation specifically engineered for Small and Mid-market Businesses (SMBs).

---

## 🌟 Vision & Purpose
Unlike traditional CRMs where AI is a "bolted-on" feature, Opentavu is **AI-First**. Our architecture assumes AI is part of the operational loop from day one, acting as the default first responder while humans handle exceptions and high-value editing.

This project extends a 2017 Master's thesis (Universidad Icesi) that empirically validated the functional requirements for SMB CRM success, now re-imagined for the Generative AI era.

## ✨ Key AI Modules (v0.2 Roadmap)
1. **Smart Case Categorization:** Automated routing and classification using Azure OpenAI with confidence-based validation.
2. **AI Email Drafting Assistant:** Context-aware response generation based on CRM history and case intent.
3. **AI Activity Capture & Hygiene:** Automated extraction of context from emails and meetings to reduce manual data entry—the #1 cause of CRM abandonment.

## 🏗 Architectural Principles
- **License-Optimized:** Uses custom entities (`smb_*`) to operate within the **Power Apps Premium** envelope, avoiding expensive enterprise-tier licensing.
- **Hybrid Commercial Flow:** A "Single Lifecycle, Dual Entry" model that supports both high-trust outbound relationships and automated inbound lead buffering.
- **Provider-Agnostic AI:** While defaulting to Azure OpenAI for enterprise security, the framework includes an abstraction interface for other AI providers.
- **Validation Before Persistence:** Every AI output passes through business-rule validation and confidence thresholds before affecting records.

## 📂 Project Structure
- `/solutions`: Managed and unmanaged Power Platform solutions.
- `/docs`: Technical implementation guides and functional coverage maps.
- `/src`: Custom C# Workflow Activities and plugin logic.

## 🛠 Tech Stack
- **Data Platform:** Microsoft Dataverse.
- **Logic:** Power Automate + Custom C# Activities.
- **AI:** Azure OpenAI Service (GPT-4o / Turbo).
- **Frontend:** Power Apps (Model-Driven & Mobile-First Canvas Apps).

## 📄 License
This project is licensed under the **MIT License**—free for consultants and businesses to use, fork, and adapt.

## 📩 Contact & Community
**Gustavo González Villani** Founder & Architect | MSc. Gestión de Informática y Telecomunicaciones  
📧 [gg@opentavu.com](mailto:gg@opentavu.com) | 🌐 [opentavu.com](https://opentavu.com)

---
*Opentavu is a community-driven framework. We welcome feedback from MVPs, integrators, and the Power Platform community.*
