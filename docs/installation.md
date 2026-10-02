# OpenTavu Installation Guide

**Deploying OpenTavu into a client's Microsoft Dataverse environment.**

---

## About this guide

This guide walks a Power Platform consultant or integrator through installing OpenTavu into a client tenant: checking prerequisites, obtaining the managed solution, importing it, verifying the import, and handling version upgrades.

OpenTavu ships as two **Microsoft Power Platform managed solutions** (`.zip` files) you import into the client's Dataverse environment:

- **OpenTavu Core**: tables, plugins, PCF controls, web resources, the model-driven app, forecasting, and pre-loaded seed data. It installs needing only the **Dataverse** connection, so a first deployment is friction-free.
- **OpenTavu Integrations** (optional): the Power Automate flows that use email (SMTP / Office 365 Outlook), Microsoft Forms, and OneDrive. Add it only when the client wants those flows; it needs those extra connections.

Install order is always **Core first, then Integrations**. OpenTavu is not a hosted SaaS product: the client brings their own Microsoft 365 and Azure (or alternative AI provider) subscriptions, and you configure OpenTavu to work on top of them.

This document covers **deployment only**. Once the solution is imported, continue with [configuration.md](configuration.md), which covers AI wiring, system settings, security, Module 1, the SLA matrix, and the end-to-end smoke test.

OpenTavu is open source under the MIT license. Fork the repository, deploy it for a client engagement, and contribute improvements back.

> **Conventions used in this guide**
> - `> 📸 **Screenshot:**` marks a point where a screenshot belongs in the published version.
> - `> ✅ **Checkpoint:**` closes each section with a concrete "you are done when" test. Do not proceed until the checkpoint passes.

---

## 1. Prerequisites

Confirm every item below before you download anything. Missing a prerequisite here is the most common cause of a failed or half-working deployment.

### 1.1 Microsoft 365 and Dataverse

- A **Microsoft 365 tenant** for the client.
- A **Dataverse environment** to deploy into. For a first deployment, use a dedicated **development or sandbox environment**, validate the full smoke test, then promote to production. Never do a first-time import directly into a production environment.
- The environment must have a **Dataverse database** provisioned (an environment without a database cannot host a solution).
- **Language (optional).** To use the Spanish UI (LCID 3082), enable the Spanish language in the environment (Power Platform admin center, Settings, Languages). If Spanish is not enabled, the Spanish labels are skipped during import with a harmless warning and the UI stays in English.

### 1.2 Licensing

- **Power Apps Premium** licenses (approximately $20 per user per month) for every user who will work in OpenTavu. Premium is required because OpenTavu uses custom `tavu_*` tables and plugins; the seeded standard tables (`account`, `contact`) alone would not be enough.
- No Dynamics 365 license is required. OpenTavu deliberately replaces licensing-restricted entities with custom `tavu_*` equivalents so the per-user cost stays at the Premium tier.

### 1.3 AI provider

OpenTavu's AI modules (starting with Module 1, Smart Case Categorization) need a language-model backend. Decide **now** which of the two open wiring paths you will use; both are configured after import in [configuration.md](configuration.md).

- **Gateway mode (recommended):** the client tenant points at an **AI gateway** through two environment variables (`tavu_GatewayUrl`, `tavu_GatewayKey`). The real provider keys live in the gateway, never in the client tenant. OpenTavu publishes a **self-hosted reference gateway** (MIT, single-tenant, bring-your-own-model) that you deploy with the client's own keys. See the gateway project's README for its deployment.
- **Direct mode (simplest):** no gateway. The provider key is stored in the client tenant on the `tavu_aimodel` record. This is the easiest path to a working demo, with the trade-off that the key lives inside the client environment.

Either way, you need an **Azure subscription with Azure OpenAI access** (the default provider), or credentials for an alternative provider (Anthropic Claude, Google Gemini, or a local model) that OpenTavu reaches through its provider-agnostic `IAIProvider` interface.

### 1.4 Permissions

- **System Administrator** security role on the target Dataverse environment (required to import a solution and register plugins).
- For **gateway mode**, a **Microsoft Entra tenant administrator** to grant one-time admin consent to the gateway's multi-tenant app registration (this creates an application user in the client's Dataverse so the gateway can write results back). This is a one-click consent, described in the gateway deployment steps.

### 1.5 Tooling

- Access to the **Power Platform admin center** (admin.powerplatform.microsoft.com) and **make.powerapps.com**.
- Optional but recommended: the **Power Platform CLI** (`pac`) for scripted or repeatable imports.

> ✅ **Checkpoint:** You have a target environment with a Dataverse database, Premium licenses assigned, an AI provider decision made, System Administrator rights, and (for gateway mode) a tenant admin lined up for consent.

---

## 2. Get the managed solutions

OpenTavu releases are published as managed solution files on **GitHub Releases**. A release carries two assets:

| Package | Asset (example) | Needs | When |
|---|---|---|---|
| **OpenTavu Core** | `OpenTavu_1_1_0_managed.zip` | Dataverse connection only | Always (this is the product) |
| **OpenTavu Integrations** (optional) | `OpenTavuIntegrations_1_0_0_managed.zip` | Email (SMTP / Office 365 Outlook), Microsoft Forms, OneDrive connections | Only if the client wants the email / Forms / OneDrive flows |

1. Open the OpenTavu `core` repository on GitHub and go to the **Releases** page.
2. Download the **Core** asset. Download the **Integrations** asset too only if you will use those flows.
3. Confirm you downloaded the **managed** builds. Managed solutions are what you deploy into a client tenant: their components are locked against direct editing, which is exactly what you want in a client's production environment. Unmanaged builds are for OpenTavu development only, not for client deployment.

> 📸 **Screenshot:** GitHub Releases page with the Core `.zip` asset highlighted.

> ✅ **Checkpoint:** You have the latest Core `.zip` (and, if needed, the Integrations `.zip`) downloaded locally, and you have noted the version for your deployment record.

---

## 3. Import the solution

You can import through the maker portal (recommended for a first deployment because the dialogs are explicit) or through the `pac` CLI (recommended for repeatable deployments).

### 3.1 Import via make.powerapps.com

1. Sign in to **make.powerapps.com** and select the **target environment** in the top-right environment picker. Double-check you are in the intended client environment before continuing.
2. In the left navigation, open **Solutions**.
3. Select **Import solution** on the command bar.
4. Choose **Browse**, select the `OpenTavu_*.zip` file, then select **Next**.

   > 📸 **Screenshot:** Import solution dialog with the OpenTavu package selected, showing the solution name, version, and "Managed" package type.

5. Review the solution details (name, version, publisher). Confirm the **package type is Managed**.
6. Handle the prompts:
   - **Connection reference (Core):** Core prompts only for the **Microsoft Dataverse** connection. Sign in with an account that can run flows; it turns green and you continue. The email, Forms, and OneDrive connections are requested only by the optional Integrations package, not by Core.
   - **Environment variables for AI wiring** (`tavu_GatewayUrl`, `tavu_GatewayKey`): leave these blank during import and set them in [configuration.md](configuration.md) §2. They do not block the import.
7. Select **Import** and wait for the operation to complete. A managed solution of this size typically imports in a few minutes.

   > 📸 **Screenshot:** Import progress bar, followed by the "solution imported successfully" confirmation banner.

### 3.2 Import via the Power Platform CLI (alternative)

For scripted deployments:

```
pac auth create --environment <ENVIRONMENT_URL>
pac solution import --path .\OpenTavu_1_1_0_managed.zip --async
# optional, ONLY if you use the email / Forms / OneDrive flows, and AFTER Core:
pac solution import --path .\OpenTavuIntegrations_1_0_0_managed.zip --async
```

`pac solution import` imports the file as-is (managed, because the packaged file is managed). Use `--async` for large solutions so the CLI polls the job to completion rather than timing out. The CLI does not force connection mapping up front; set connections after import if any flow needs them.

> ✅ **Checkpoint:** The import completes with **no errors**. If the import surfaces warnings about missing dependencies, resolve those before proceeding: a partial import is not a valid OpenTavu deployment. If the import fails, read the downloadable import log, correct the cause (most often a missing license or an environment without a database), and re-import.

---

## 4. Post-import verification

Do not start configuration until every check below passes. This confirms all components registered correctly.

### 4.1 Solution and app

1. In **Solutions**, confirm **OpenTavu** appears in the list with the version you imported and a **Managed** badge.
2. Open the OpenTavu **model-driven app** (from **Apps**, or from inside the solution). It should launch without missing-component errors.

   > 📸 **Screenshot:** The OpenTavu model-driven app open on its default area, with the site map (Sales, Service, Configuration areas) visible.

### 4.2 Tables

Confirm the core tables exist and open:

- Standard, extended: `account`, `contact`.
- Sales: `tavu_lead`, `tavu_opportunity`, `tavu_opportunityclose`, `tavu_proposal`, `tavu_proposalline`, plus the product and pricing tables.
- Service: `tavu_case`, `tavu_casetype`, `tavu_customertierdefinition`, `tavu_sla`, `tavu_timeentry`.
- Calendars: `tavu_businesscalendar`, `tavu_calendarworkinghours`, `tavu_businessclosure`.
- Configuration: `tavu_systemsettings`.

### 4.3 Code components

- **Plugins registered:** confirm the OpenTavu plugin steps are present (for example `Pl.Case.Categorize`, `Pl.Case.SlaAssignment`, `Pl.Case.CustomerSync`, `Pl.Opportunity.CustomerSync`, `Pl.Opportunity.LifecycleTracker`, `Pl.Opportunity.CloseOrchestrator`, `Pl.ProposalLine.Calculator`, `Pl.Proposal.LifecycleTracker`, `Pl.SystemSettings.SingleRecordGuard`).
- **PCF controls load:** open a `tavu_case` record and confirm the `AiAssessment` panel and the `SlaCountdown` bars render (they will show empty or "not yet processed" states on a blank record, which is expected).
- **Custom page:** the guided opportunity close dialog (`tavu_opportunityclosedialog`) is present.

   > 📸 **Screenshot:** A blank `tavu_case` form showing the `AiAssessment` panel and `SlaCountdown` controls rendering (empty state).

> ✅ **Checkpoint:** The app opens, every table above is present, plugins are registered, and the PCF controls render on a case form. No "missing component" or "control failed to load" errors anywhere. The deployment is now installed; proceed to configuration.

---

## 5. Version upgrades

New OpenTavu releases are imported **on top of** the existing managed solution. Handle upgrades deliberately: they can add or change components, and re-importing carelessly can disrupt a live client environment.

### 5.1 Before upgrading

1. **Record the current version** (from the Solutions list) so you can identify what changed.
2. **Back up** the environment: export the current solution, or take an environment backup from the Power Platform admin center. Client configuration and data (seed extensions, calendars, SLA rows, cases, opportunities) live as **data**, not as solution components, so a standard import does not overwrite them; the backup is your rollback safety net regardless.
3. Review the release notes for the new version to see whether any post-upgrade configuration steps are required.

### 5.2 Performing the upgrade

1. Download the newer `OpenTavu_*.zip` from GitHub Releases.
2. Import it the same way as in §3. Because a solution with the same name already exists, the platform treats this as an **upgrade**.
3. When prompted, choose the **Stage and Upgrade** option so the new version is staged and then applied, which cleanly removes components deleted in the new release. (The classic "Update" option leaves removed components behind and should be avoided unless a release note specifically calls for it.)
4. Wait for the upgrade to finish, then re-run the **post-import verification** in §4.

### 5.3 Rollback

If an upgrade causes a problem you cannot resolve quickly, restore from the environment backup taken in §5.1, or re-import the previously known-good `OpenTavu_*.zip`. Because business data is separate from solution components, restoring the prior solution version does not lose the client's records.

> ✅ **Checkpoint:** After an upgrade, the Solutions list shows the new version, §4 verification passes again, and any post-upgrade steps from the release notes are complete.

---

## 6. Next step

The solution is installed and verified. Continue with **[configuration.md](configuration.md)** to wire up AI, set system settings, verify and extend seed data, configure security and Field Security Profiles, configure Module 1 (Smart Case Categorization) and the SLA matrix, and run the end-to-end smoke test.
