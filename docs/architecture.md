# OpenTavu — Platform Architecture

## Two-layer, multi-tenant design

**Audience:** implementers and contributors. **Purpose:** describe how OpenTavu is deployed and how its parts talk to each other, so the framework can be delivered to many clients with minimal per-client work.

OpenTavu runs as **two layers**:

1. **Client layer — the managed solution (per client tenant).** Deployed into each client's own Microsoft Dataverse environment: tables, plugins, PCF controls, web resources, model-driven app. This is what an implementer imports per client.
2. **Central layer — the OpenTavu Azure services (one, shared).** A single multi-tenant **Azure Function App** owned by OpenTavu (in the `opentavu.com` Entra tenant): the **AI Gateway** and the **SLA Scheduler**. Deployed and maintained **once**; every client's environment connects to it.

```
Client A tenant ─┐
Client B tenant ─┼──HTTPS──►  OpenTavu Azure (opentavu.com)
Client C tenant ─┘  ◄─S2S──   Function App: AI Gateway + SLA Scheduler
 (Dataverse +                  (holds AI keys, routes models,
  managed solution)             schedules SLA timers, meters usage)
```

Only `C:\Code\OpenTavu\core` ships to GitHub / clients. The Azure layer is a separate deployment OpenTavu operates.

---

## 1. Client layer — managed solution

Deployed into the client's Dataverse (Power Apps Premium, no Dynamics 365 license). Current components:

- **Tables:** `account`/`contact` (standard, extended) + custom `tavu_lead`, `tavu_opportunity`, `tavu_proposal`, `tavu_case`, and configuration tables (`tavu_casetype`, `tavu_customertierdefinition`, `tavu_sla`, `tavu_businessline`/`tavu_category`/`tavu_subcategory`, `tavu_businesscalendar`/`tavu_calendarworkinghours`/`tavu_businessclosure`, `tavu_systemsettings`, product/pricing tables). See `service-model.md` and `sales-model.md`. Human-readable record IDs use the autonumber standard `OTC/OTO/OTP-{DATETIMEUTC:yyyy}-{SEQNUM:5}` on Case / Opportunity / Proposal.
- **Plugins** (C#, sandbox, signed with `_Shared/Common/OpenTavu.snk`):
  - `Pl.Case.Categorize` — Module 1 Smart Case Categorization (async, Create of `tavu_case`); routes AI through the gateway via `GatewayProvider`.
  - `Pl.Case.SlaAssignment` — Pre-Op computes SLA target dates (calendar-aware, from `createdon`); async Post-Op calls the gateway to schedule the Warning/Breach durable timers and stores `tavu_slaorchestrationid`.
  - `Pl.Case.CustomerSync` — mirrors the polymorphic `tavu_customer` to typed `tavu_account`/`tavu_contact` (so Quick View forms load) + sets `tavu_primarycontact` for B2C; validates Customer Mode. Case-side twin of `Pl.Opportunity.CustomerSync`.
  - `Pl.SystemSettings.SingleRecordGuard` — enforces the settings singleton.
  - `Pl.Opportunity.LifecycleTracker` — lifecycle fields (stage-change date, stage probability default) + close/reopen probability and close-input validation.
  - `Pl.Opportunity.CustomerSync` — polymorphic `tavu_customer` → typed lookups + Customer Mode validation.
  - `Pl.Opportunity.CloseOrchestrator` — Post-Op on close: writes the `tavu_opportunityclose` history log and marks `tavu_iscustomer` on Won. See `opportunity-close-dialog.md`.
  - `Pl.ProposalLine.Calculator` — line money fields + header rollup totals + parent-lock guard.
  - `Pl.Proposal.LifecycleTracker` — version default, transition guard, lock, one Approved per opportunity. See `proposal-lifecycle.md`.
  - `Pl.Proposal.CloneVersion` — implements the `tavu_CloneProposalVersion` Custom API (Create New Version).
- **Custom APIs:** `tavu_CloneProposalVersion` (Global action; clones a proposal into a new draft version, supersedes the source; implemented by `Pl.Proposal.CloneVersion`).
- **PCF controls (React + Fluent v9):** `AiAssessment` (case AI panel), `SlaCountdown` (live SLA countdown bar; placed on Response and Resolution target dates).
- **Custom pages:** `tavu_opportunityclosedialog` — guided Close as Won/Lost dialog for the opportunity.
- **Web resources:** `tavu_systemsettings_open.html` (settings singleton); form scripts `tavu_opportunity_form.js` (Customer Mode filter/mirror, lifecycle UI, close/reopen), `tavu_proposal_form.js` (lifecycle buttons, visual lock, header totals auto-refresh), plus `tavu_account_form.js`, `tavu_contact_form.js`, `tavu_product_form.js`, `tavu_proposalline_form.js`.
- **Shared code** (linked, not a DLL): `_Shared/Common` (PluginBase, LocalPluginContext) and `_Shared/AI` (`IAIProvider`, `AzureOpenAIProvider`, `OpenAIProvider`, `GatewayProvider`, `AIProviderFactory`, `AIConfigResolver`).

---

## 2. Central layer — OpenTavu Azure Function App

One **C# / .NET isolated** Function App in the `opentavu.com` tenant, pay-as-you-go subscription. **Deployed** as `opentavu-gateway` (Central US, Consumption plan) with a KeepWarm timer to cut cold starts. Two capabilities:

### 2a. AI Gateway (`/api/ai/complete`)
- Holds the **provider subscriptions and keys** (Azure OpenAI, OpenAI, Claude, Gemini) — OpenTavu contracts these centrally and bills clients per user (volume pricing). **The AI key lives only here, never in the client tenant.**
- Receives from the in-tenant plugin: `systemPrompt` + `userContent` (case text + the client's **taxonomy**) + a **per-tenant key**. Returns the completion + token counts.
- *Today (MVP):* uses a single default model from app settings and the prompt is built in the client and sent in the payload. *Target:* per-task/per-tenant **model routing** and gateway-held prompts (config migrates from the client).

### 2b. SLA Scheduler (Durable Functions)
- Receives `(caseId, tenant, warningTime, failureTime)` from `Pl.Case.SlaAssignment`.
- A **durable orchestration with timers** fires exactly at the warning/failure time (push, not polling).
- On fire: calls back into the client's Dataverse (S2S) to update `tavu_slastatus` and trigger actions (notify/escalate) **only if the case is still open**.
- On SLA change (re-categorization), the plugin cancels/reschedules using the stored orchestration instance id.

---

## 3. Authentication

- **Multi-tenant Entra app registration** (in `opentavu.com`). Each client **admin consents once** → an **application user** is created in their Dataverse. This lets the gateway write back to the client's Dataverse (S2S, client-credentials — no MFA, no interactive prompt).
- **Plugin → gateway:** the in-tenant plugin calls the gateway over HTTPS with a **per-tenant key** sent in the `X-OpenTavu-Tenant-Key` header. The client tenant holds only the gateway base URL + that scoped key, in two environment variables (`tavu_GatewayUrl`, `tavu_GatewayKey`) — never the real AI keys. Endpoints are **Anonymous** at the Functions layer; the tenant key is the credential (validated server-side, unknown → 401). Can be hardened later with IP allow-list / APIM.
- **MFA** applies to **human** sign-ins only (admin accounts); it does **not** affect the service-to-service flows above.

---

## 4. Configuration split (client vs gateway)

| Config | Home |
|---|---|
| AI provider endpoints / keys / deployments / provider | **Gateway** (never in client tenant) |
| Task→model routing + prompts + parameters | **Gateway** |
| Taxonomy (Case Types + Business Line/Category/Subcategory) | **Client** (sent to the gateway in the payload) |
| Confidence threshold / AI Enabled kill switch | Client (`tavu_systemsettings`) or per-tenant at gateway |
| Business calendars / SLAs / tiers | **Client** (business config) |
| AI output fields on the case | **Client** (the data) |

> Status: the gateway is **live** and the plugin's `IAIProvider` resolves to `GatewayProvider` whenever `tavu_GatewayUrl` + `tavu_GatewayKey` are set (else it falls back to the direct providers). The AI key has been **removed from the client tenant**. Still pending migration: the task **prompts** (`tavu_aitaskconfig`) and model routing currently stay in the client and are sent in the payload; these move to the gateway later without changing the module code.

---

## 5. Flows

**Module 1 (categorization):** case created → `Pl.Case.Categorize` (async) gathers case text + active taxonomy → calls the gateway `/ai/complete` → validates the result against active config (anti-hallucination) → writes AI fields → sets status by confidence/multi-intent. The `AiAssessment` PCF renders the result.

**SLA:** categorization sets Type → `Pl.Case.SlaAssignment` resolves SLA (Tier+Type) + calendar, computes Response/Resolution Target Dates from `createdon` (business-hours + closures, DST-aware) → calls the gateway to schedule Durable timers → gateway fires at warning/failure → updates status + acts. The `SlaCountdown` PCF shows a live client-side countdown.

---

## 6. Regions, billing, data

- **Billing:** OpenTavu's Azure subscription (pay-as-you-go). Function App deployed in **Central US** (East US / East US 2 had 0 vCPU quota on the new subscription; Central US had capacity — no functional difference for US clients).
- **Data residency:** each client's case data lives in **their** Dataverse tenant/region. The gateway processes it **transiently** (does not store case data); AI providers (Azure OpenAI / OpenAI) do not train on API data.
- **Sub-processor:** because case content transits the gateway, OpenTavu is a data sub-processor → covered in the client DPA. *(Legal review required.)*

---

## 7. Per-client onboarding (the repeatable part)

1. **Import** the managed solution into the client's Dataverse.
2. Client **admin consents** to the multi-tenant app (one click) → application user created.
3. **Register** the client in the gateway's config (Dataverse URL + env id + generate a per-tenant key; store the key as a secret env var in the client env).
4. **Configure** client data: business calendars, SLAs, tiers, taxonomy, System Settings. Much ships as seed; the rest is a short checklist.

**No Azure work per client.** The Function App is deployed once and is multi-tenant.

---

## 8. Cost

Azure Functions **Consumption** plan: 1M executions + 400,000 GB-s free/month → this workload is well within the free grant. Only a Storage account (Durable Functions state) costs a few cents/month. Effective total: **~$0–3/month** for the platform. AI consumption is separate (provider bills, cents per case). Do **not** use the Premium plan.

---

## 9. Current status & roadmap

- **Built & live (end-to-end):**
  - Central gateway **deployed** (`opentavu-gateway`, Central US) with AI Gateway (`/api/ai/complete`), SLA Scheduler (`/api/sla/schedule`, `/api/sla/cancel`, Durable timers) and KeepWarm.
  - **Module 1 categorization** runs through the gateway (`GatewayProvider`); AI key removed from the client tenant.
  - **SLA engine**: `Pl.Case.SlaAssignment` computes calendar-aware target dates and schedules the Warning/Breach timers via the gateway; the scheduler writes `tavu_slastatus` back (S2S) only while the case is open.
  - **PCF**: `AiAssessment` + `SlaCountdown` (dual Response/Resolution bars). `Pl.Case.CustomerSync` fixes the polymorphic-customer Quick View.
- **Next (case flow):** close the case lifecycle — set `First Response Date`, `Resolution Date`, and `SLA Status = Met/Breached` on resolve; Response-SLA tracking; SLA pause on "Waiting on Customer"; reopen. *(Under industry-best-practice research before implementation.)*
- **Next (platform):** migrate prompts + model routing from the client to the gateway; per-tenant registry (replace single-tenant app settings); auth hardening (IP allow-list / APIM).

---

## Document control

| Version | Date | Author | Notes |
|---|---|---|---|
| 1.2 | July 10, 2026 | Gustavo González Villani (with Claude) | Synced the client-layer inventory to the implemented sales cycle. Added plugins `Pl.Opportunity.CloseOrchestrator`, `Pl.Proposal.LifecycleTracker`, `Pl.Proposal.CloneVersion` (and expanded the LifecycleTracker/Calculator descriptions). Added the `tavu_CloneProposalVersion` Custom API, the `tavu_opportunityclosedialog` custom page, and the opportunity/proposal form web resources. Documented the `OTC/OTO/OTP-{yyyy}-{seqnum:5}` autonumber standard. Detail in `opportunity-close-dialog.md` and `proposal-lifecycle.md`. |
| 1.1 | July 3, 2026 | Gustavo González Villani (with Claude) | Synced to the **live** deployment: gateway deployed (`opentavu-gateway`, Central US) + KeepWarm; AI now routed through the gateway via `GatewayProvider` (AI key removed from the client tenant); endpoints Anonymous + `X-OpenTavu-Tenant-Key`; wiring via `tavu_GatewayUrl`/`tavu_GatewayKey` env vars. Added `Pl.Case.CustomerSync` and the async SLA-scheduling step of `Pl.Case.SlaAssignment` (`tavu_slaorchestrationid`); `SlaCountdown` dual bars. Updated status/roadmap (case-lifecycle closure pending research; prompt/model-routing migration and auth hardening pending). Region note: Central US (East US had 0 vCPU quota). |
| 1.0 | July 1, 2026 | Gustavo González Villani (with Claude) | Initial platform architecture: two-layer multi-tenant model (client managed solution + central OpenTavu Azure Function App = AI Gateway + SLA Scheduler); multi-tenant Entra auth + S2S; client/gateway config split; Module 1 and SLA flows; regions/billing/data residency; per-client onboarding; cost; runtime (C#/.NET isolated, Durable Functions). |

*This document is the platform-architecture reference for OpenTavu. Detailed table specs live in `service-model.md` and `sales-model.md`; the product vision in `VISION.md`.*
