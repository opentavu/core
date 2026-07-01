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

- **Tables:** `account`/`contact` (standard, extended) + custom `tavu_lead`, `tavu_opportunity`, `tavu_proposal`, `tavu_case`, and configuration tables (`tavu_casetype`, `tavu_customertierdefinition`, `tavu_sla`, `tavu_businessline`/`tavu_category`/`tavu_subcategory`, `tavu_businesscalendar`/`tavu_calendarworkinghours`/`tavu_businessclosure`, `tavu_systemsettings`, product/pricing tables). See `service-model.md` and `sales-model.md`.
- **Plugins** (C#, sandbox, signed with `_Shared/Common/OpenTavu.snk`):
  - `Pl.Case.Categorize` — Module 1 Smart Case Categorization (async, Create of `tavu_case`).
  - `Pl.Case.SlaAssignment` — computes SLA target dates (calendar-aware, from `createdon`) and asks the gateway to schedule breach timers.
  - `Pl.SystemSettings.SingleRecordGuard` — enforces the settings singleton.
  - Existing: `Pl.Opportunity.LifecycleTracker`, `Pl.Opportunity.CustomerSync`, `Pl.ProposalLine.Calculator`.
- **PCF controls (React + Fluent v9):** `AiAssessment` (case AI panel), `SlaCountdown` (live SLA indicator).
- **Web resources:** `tavu_systemsettings_open.html` (opens the settings singleton directly).
- **Shared code** (linked, not a DLL): `_Shared/Common` (PluginBase, LocalPluginContext) and `_Shared/AI` (`IAIProvider`, `AzureOpenAIProvider`, `OpenAIProvider`, `AIProviderFactory`, `AIConfigResolver`).

---

## 2. Central layer — OpenTavu Azure Function App

One **C# / .NET isolated** Function App in the `opentavu.com` tenant, pay-as-you-go subscription. Two capabilities:

### 2a. AI Gateway (`/ai/complete`)
- Holds the **provider subscriptions and keys** (Azure OpenAI, OpenAI, Claude, Gemini) — OpenTavu contracts these centrally and bills clients per user (volume pricing).
- Does **model-per-task routing** and holds the **prompts/parameters** (moved out of client tenants).
- Receives from the in-tenant plugin: `taskKey` + case content + the client's **taxonomy** (sent in the payload) + a **per-tenant key**. Returns the completion; **meters usage** for billing.

### 2b. SLA Scheduler (Durable Functions)
- Receives `(caseId, tenant, warningTime, failureTime)` from `Pl.Case.SlaAssignment`.
- A **durable orchestration with timers** fires exactly at the warning/failure time (push, not polling).
- On fire: calls back into the client's Dataverse (S2S) to update `tavu_slastatus` and trigger actions (notify/escalate) **only if the case is still open**.
- On SLA change (re-categorization), the plugin cancels/reschedules using the stored orchestration instance id.

---

## 3. Authentication

- **Multi-tenant Entra app registration** (in `opentavu.com`). Each client **admin consents once** → an **application user** is created in their Dataverse. This lets the gateway write back to the client's Dataverse (S2S, client-credentials — no MFA, no interactive prompt).
- **Plugin → gateway:** the in-tenant plugin calls the gateway over HTTPS with a **per-tenant key** stored as a secret **environment variable** in the client's environment. The client tenant never holds the real AI keys — only the gateway URL + a scoped key.
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

> MVP note: today the AI config lives in the client (`tavu_aimodel`, `tavu_aitaskconfig`) for the direct-call model. When the gateway lands, that config migrates to the gateway and the plugin's `IAIProvider` becomes a `GatewayProvider` (call the gateway instead of the provider directly) — the module code does not change.

---

## 5. Flows

**Module 1 (categorization):** case created → `Pl.Case.Categorize` (async) gathers case text + active taxonomy → calls the gateway `/ai/complete` → validates the result against active config (anti-hallucination) → writes AI fields → sets status by confidence/multi-intent. The `AiAssessment` PCF renders the result.

**SLA:** categorization sets Type → `Pl.Case.SlaAssignment` resolves SLA (Tier+Type) + calendar, computes Response/Resolution Target Dates from `createdon` (business-hours + closures, DST-aware) → calls the gateway to schedule Durable timers → gateway fires at warning/failure → updates status + acts. The `SlaCountdown` PCF shows a live client-side countdown.

---

## 6. Regions, billing, data

- **Billing:** OpenTavu's Azure subscription (pay-as-you-go). Deploy the Function App in a **US region** (e.g., East US 2) to sit close to US clients.
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

- **Built (MVP, direct-call):** Module 1 categorization plugin + AI provider abstraction + AiAssessment PCF + settings singleton, all in the client tenant; AI keys via env variable in the dev tenant.
- **Next:** SLA plugin + SlaCountdown PCF; then the **central Azure layer** (AI Gateway + SLA Scheduler + multi-tenant auth), after which the AI config migrates from client tenants to the gateway.

---

## Document control

| Version | Date | Author | Notes |
|---|---|---|---|
| 1.0 | July 1, 2026 | Gustavo González Villani (with Claude) | Initial platform architecture: two-layer multi-tenant model (client managed solution + central OpenTavu Azure Function App = AI Gateway + SLA Scheduler); multi-tenant Entra auth + S2S; client/gateway config split; Module 1 and SLA flows; regions/billing/data residency; per-client onboarding; cost; runtime (C#/.NET isolated, Durable Functions). |

*This document is the platform-architecture reference for OpenTavu. Detailed table specs live in `service-model.md` and `sales-model.md`; the product vision in `VISION.md`.*
