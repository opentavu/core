# OpenTavu Installation Guide

**Deploying OpenTavu into a client's Microsoft Dataverse environment.**

---

## About this guide

This guide walks a Power Platform consultant or integrator through installing OpenTavu into a client tenant: checking prerequisites, obtaining the managed solution, importing it, verifying the import, and handling version upgrades.

OpenTavu ships as two **Microsoft Power Platform managed solutions** (`.zip` files) you import into the client's Dataverse environment:

- **OpenTavu Core**: tables, plugins, PCF controls, web resources, the model-driven app, forecasting, and the setup routine that creates the reference data after import (configuration guide §4). It installs needing only the **Dataverse** connection, so a first deployment is friction-free.
- **OpenTavu Integrations** (optional): the Power Automate flows that use email (SMTP / Office 365 Outlook), Microsoft Forms, and OneDrive. Add it only when the client wants those flows; it needs those extra connections.

Install order is always **Core first, then Integrations**. Opentavu is not a hosted SaaS product. Every client firm has its own Microsoft tenant and its own Azure subscription, and everything Opentavu runs lives there: the Dataverse environment, the gateway and the AI model. The firm pays Microsoft directly for licenses, capacity, AI usage and hosting; Opentavu LLC, or the integrator you are, implements and supports the installation (see [architecture.md](architecture.md)).

This document covers **deployment only**. Once the solution is imported, continue with [configuration.md](configuration.md), which covers AI wiring, system settings, security, Module 1, the SLA matrix, and the end-to-end smoke test.

OpenTavu is open source under the MIT license. Fork the repository, deploy it for a client engagement, and contribute improvements back.

> **Conventions used in this guide**
> - `> 📸 **Screenshot:**` marks a point where a screenshot belongs in the published version.
> - `> ✅ **Checkpoint:**` closes each section with a concrete "you are done when" test. Do not proceed until the checkpoint passes.

---

## 1. Prerequisites

Confirm every item below before you download anything. Missing a prerequisite here is the most common cause of a failed or half-working deployment.

### 1.1 Microsoft 365 and Dataverse

- A **Microsoft 365 tenant** for the client firm (its own, not the integrator's).
- An **Azure subscription** in that same tenant, owned and paid by the firm (it needs a company credit card). The gateway, its storage, Key Vault, Application Insights and the AI model are created there.
- A **Dataverse environment** to deploy into. For a first deployment, use a dedicated **development or sandbox environment**, validate the full smoke test, then promote to production. Never do a first-time import directly into a production environment.
- The environment must have a **Dataverse database** provisioned (an environment without a database cannot host a solution).
- **Language (optional).** To use the Spanish UI (LCID 3082), enable the Spanish language in the environment (Power Platform admin center, Settings, Languages). If Spanish is not enabled, the Spanish labels are skipped during import with a harmless warning and the UI stays in English.

### 1.2 Licensing

- **Power Apps licenses:** the pay-as-you-go plan (about USD 10 per active user per app per month, billed to the firm's Azure subscription) or Premium per user (about USD 20 per user per month); which one costs less depends on the number of users and the database size. Either way no Dynamics 365 license, because Opentavu uses custom `tavu_*` tables and plugins.

### 1.3 AI provider

OpenTavu's AI modules (starting with Module 1, Smart Case Categorization) need a language-model backend. Decide **now** which of the two open wiring paths you will use; both are configured after import in [configuration.md](configuration.md).

- **Gateway mode (recommended):** the firm's environment points at **its own gateway** through two environment variables (`tavu_GatewayUrl`, `tavu_GatewayKey`). The gateway is the open-source Opentavu gateway (MIT, `github.com/opentavu/gateway`), deployed as one Azure Function App per firm in the firm's own Azure subscription; the model key lives in that subscription's Key Vault, never in Dataverse. Hosting: Flex Consumption with one always-ready instance is the target; Windows Consumption until then (Linux Consumption cannot run .NET 10). See the gateway README for its deployment. The gateway also runs the SLA timers, the email and Teams intakes and the Lead Qualification Agent.
- **Direct mode (simplest):** no gateway. The provider key is stored in the client tenant on the `tavu_aimodel` record. This is the easiest path to a working demo, with the trade-off that the key lives inside the client environment.

Either way, the model is the firm's: an **Azure OpenAI resource in the firm's subscription** (the default provider), or the firm's own credentials for an alternative provider (Anthropic Claude, Google Gemini, or a local model) that Opentavu reaches through its provider-agnostic `IAIProvider` interface.

### 1.4 Permissions

- **System Administrator** security role on the target Dataverse environment (required to import a solution and register plugins).
- For **gateway mode**, a **Microsoft Entra administrator** of the firm's tenant to create the gateway's app registration there and grant its admin consent, and to add it as an application user in the firm's Dataverse so the gateway can write results back. Rights to create resources in the firm's Azure subscription (Contributor on its resource group).
- **Support access, when Opentavu LLC (or another provider) manages the installation:** the firm delegates its Azure resources through **Azure Lighthouse** (an ARM template; the firm sees and can revoke it under "Service providers"), and, until GDAP is available, creates a named admin account for the provider with only Power Platform Administrator, Cloud Application Administrator and Exchange Administrator, with MFA. Never Global Administrator.

### 1.5 Tooling

- Access to the **Power Platform admin center** (admin.powerplatform.microsoft.com) and **make.powerapps.com**.
- Optional but recommended: the **Power Platform CLI** (`pac`) for scripted or repeatable imports.

> ✅ **Checkpoint:** You have a target environment with a Dataverse database, Power Apps licenses (pay-as-you-go or Premium), an AI provider decision made, System Administrator rights, and (for gateway mode) the firm's own Azure subscription, an Entra administrator of the firm's tenant lined up for the app registration and consent, and, for a managed installation, the Lighthouse delegation and the provider's admin account.

---

## 2. Get the managed solutions

OpenTavu releases are published as managed solution files on **GitHub Releases**. A release carries two assets:

| Package | Asset (example) | Needs | When |
|---|---|---|---|
| **OpenTavu Core** | `OpenTavu_1_2_0_0_managed.zip` | Dataverse connection only | Always (this is the product) |
| **OpenTavu Integrations** (optional) | `OpentavuIntegrations_1_0_0_0_managed.zip` | Email (SMTP / Office 365 Outlook), Microsoft Forms, OneDrive connections | Only if the client wants the email / Forms / OneDrive flows |

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
pac solution import --path .\OpenTavu_1_2_0_0_managed.zip --async
# optional, ONLY if you use the email / Forms / OneDrive flows, and AFTER Core:
pac solution import --path .\OpentavuIntegrations_1_0_0_0_managed.zip --async
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
2. **Back up** the environment: export the current solution, or take an environment backup from the Power Platform admin center. Client configuration and data (seed extensions, calendars, SLA rows, cases, opportunities) live as **data**, not as solution components, so a standard import does not overwrite them; the backup is your rollback safety net regardless. Keep a configuration export as well (§6): it is the firm's setup as files, independent of the environment.
3. Review the release notes for the new version to see whether any post-upgrade configuration steps are required.

### 5.2 Performing the upgrade

1. Download the newer `OpenTavu_*.zip` from GitHub Releases.
2. Import it the same way as in §3. Because a solution with the same name already exists, the platform treats this as an **upgrade**.
3. When prompted, choose the **Stage and Upgrade** option so the new version is staged and then applied, which cleanly removes components deleted in the new release. (The classic "Update" option leaves removed components behind and should be avoided unless a release note specifically calls for it.)
4. Wait for the upgrade to finish, then re-run the **post-import verification** in §4.
5. Open **System Settings** and click **Verify Setup**. It adds any reference rows a new release introduced and reports disabled plugin steps or missing settings. It never overwrites values you changed.

### 5.3 Rollback

If an upgrade causes a problem you cannot resolve quickly, restore from the environment backup taken in §5.1, or re-import the previously known-good `OpenTavu_*.zip`. Because business data is separate from solution components, restoring the prior solution version does not lose the client's records.

> ✅ **Checkpoint:** After an upgrade, the Solutions list shows the new version, §4 verification passes again, and any post-upgrade steps from the release notes are complete.

---

## 6. Move a firm's configuration between environments

A managed solution carries the schema, not the firm's configuration rows: the agent and its criteria and texts, the company profile and logo, the AI task prompts and models, System Settings, the business calendar and its holidays, the meeting source. Two scripts in `tools/` copy those rows from one environment to another (from a sandbox to production, into a rebuilt environment, or as the configuration half of a backup):

- `tools/export-config.ps1` reads an environment and writes one JSON file per table plus `manifest.json` (and the logo as a file) into a folder.
- `tools/import-config.ps1` reads that folder and upserts the rows into a target environment.

```powershell
az login   # a user who can read the source and is System Administrator in the target
.	ools\export-config.ps1 -EnvironmentUrl https://<source-org>.crm.dynamics.com -OutputDir .\<firm>-config
.	ools\import-config.ps1 -EnvironmentUrl https://<target-org>.crm.dynamics.com -InputDir .\<firm>-config -WhatIf
.	ools\import-config.ps1 -EnvironmentUrl https://<target-org>.crm.dynamics.com -InputDir .\<firm>-config
```

How rows are matched. Never by GUID, which differs between environments: by a natural key (AI tasks by their task key, calendars and working hours by code, holidays by calendar and date, AI models and agents by name, criteria by agent and name, System Settings and the company profile as the single row). References are written by the referenced row's key and resolved in the target: the handoff user by email, the handoff team by name, the calendar by code, the AI task by task key. A reference the target cannot resolve is left as it is and listed as a manual step.

What an import does. It creates missing rows and updates only the columns whose value differs, so a second run changes nothing; rows that exist only in the target are left alone. Unlike Verify Setup, which only fills empty fields, it makes the carried columns equal to the files. Run **Verify Setup** in the target first, so the seeded reference rows (business lines, countries, customer tiers) exist, and always run with `-WhatIf` before the real import.

What is never exported. Secrets of any kind (model keys, the gateway key, client secrets; an AI model carries only the name of its secret), environment variable values, business data (leads, conversations, accounts, cases), and user-specific settings. After an import, do these by hand in the target, as the script lists them:

1. Set the environment variables `tavu_GatewayUrl` and `tavu_GatewayKey`.
2. Add the gateway's app registration as an application user with its security role.
3. Store the model keys under the secret names the AI models carry: in the gateway's Key Vault in gateway mode, or as the values of the environment variables with those names in direct mode (Verify Setup reports a missing one).
4. Turn on **Meeting Sync** on each user whose Teams meetings are captured.

The export folder holds the firm's own texts and settings: keep it private.

Both scripts are tested on Windows PowerShell 5.1 only.

---

## 7. Next step

The solution is installed and verified. Continue with **[configuration.md](configuration.md)** to wire up AI, set system settings, verify and extend seed data, configure security and Field Security Profiles, configure Module 1 (Smart Case Categorization) and the SLA matrix, and run the end-to-end smoke test.
