# Opentavu Architecture

## One stack per firm, in the firm's own accounts

**Audience:** implementers and contributors. **Purpose:** describe how Opentavu is deployed for a firm and how its parts talk to each other, so every installation is the same and needs little per-firm work.

Every client firm has its **own Microsoft tenant and its own Azure subscription**, created at installation, and the firm pays Microsoft directly: Power Platform licenses and capacity, AI usage, and the gateway's hosting (Decision 62). Opentavu runs in two layers, both inside the firm's accounts:

1. **The managed solution, in the firm's Dataverse environment:** tables, plugins, PCF controls, web resources and the model-driven app. It holds the firm's data, its business configuration, and how its agents behave.
2. **The firm's gateway, in the firm's Azure subscription:** one Azure Function App per firm (the AI gateway, the SLA scheduler, the intakes and the agent runtime) with its storage account, Key Vault and Application Insights. It runs in the same Entra tenant as the firm's Dataverse and Microsoft 365.

```
Firm's Microsoft tenant and Azure subscription
+------------------------------------------------------------+
|  Dataverse + managed solution  <--- HTTPS + tenant key ---  |
|  (data, configuration, agents)  --- S2S write-back ----->   |
|                                     Function App (gateway)  |
|  Microsoft 365 (mail, Teams,        + storage, Key Vault,   |
|  Bookings)  <------ Graph -------   Application Insights    |
+------------------------------------------------------------+
        ^ Azure Lighthouse (Azure only) and an admin account
        | with three roles (until GDAP): Opentavu's support access
```

The same deployment serves a firm that self-hosts the open-source framework. Opentavu LLC sells implementation and the managed support of this infrastructure; it never resells Microsoft or AI consumption. No client data passes through Opentavu's infrastructure. Opentavu's support access can see the firm's data; it is visible to the firm and revocable by it (Azure Lighthouse, GDAP or the admin account, §3).

---

## 1. Client layer: the managed solution

Deployed into the firm's Dataverse. Power Apps licenses: the pay-as-you-go plan or Premium per user; which one costs less depends on the number of users and the database size. Either way no Dynamics 365 license, because Opentavu uses custom `tavu_*` tables and plugins. Current components:

- **Tables:** `account`/`contact` (standard, extended) + custom `tavu_lead`, `tavu_opportunity`, `tavu_proposal`, `tavu_case`, and configuration tables (`tavu_casetype`, `tavu_customertierdefinition`, `tavu_sla`, `tavu_businessline`/`tavu_category`/`tavu_subcategory`, `tavu_businesscalendar`/`tavu_calendarworkinghours`/`tavu_businessclosure`, `tavu_systemsettings`, product/pricing tables). See `service-model.md` and `sales-model.md`. Human-readable record IDs use the autonumber standard `OTC/OTO/OTP-{DATETIMEUTC:yyyy}-{SEQNUM:5}` on Case / Opportunity / Proposal.
- **Agentic layer tables** (Lead Qualification Agent). The runtime that uses them is built in the gateway (`github.com/opentavu/gateway`): the agent runtime (guards, model turn, safety flags, decision rules), the named actions, in-chat booking through Microsoft Bookings and CreateOrUpdateLead. Nothing of it runs in this repo. Design: `lead-qualification-agent-design.md`.
  - `tavu_agent` (organization-owned, configuration): the agent definition as data. `tavu_agenttype` (Lead Qualification, Post-Meeting), `tavu_mode` (Act by default, or Propose), lookups to `tavu_aitaskconfiguration` (model and prompt) and `tavu_businesscalendar` (working hours), handoff target `tavu_handoffuser` or `tavu_handoffteam`, `tavu_createleadfordisqualified` (Yes/No, default No: a disqualified conversation also creates or updates a lead; a qualified one always does), messages (`tavu_greeting`, `tavu_outofhoursmessage`, `tavu_nextstepmessage`, `tavu_holdingmessage`, `tavu_bookingurl`), in-chat booking (`tavu_bookingsbusiness` and `tavu_bookingsserviceid`, the Microsoft Bookings business and service the agent books meetings in: with both set, a qualified person picks a slot in the conversation; otherwise the agent sends `tavu_bookingurl`), limits (`tavu_turnlimit`, `tavu_abandonafterhours`, `tavu_reopenafterhours`, `tavu_approvaltimeoutminutes`, `tavu_tokenbudget`), the Conversation tab (`tavu_slotsearchdays`, `tavu_slotsperoffer`, `tavu_bookingdeclinesbeforelink`, `tavu_movedeclinesbeforekeep`, `tavu_confirmationlimitminutes`, `tavu_owneremailintervalminutes`, `tavu_offtopiclimit`: optional, empty = the gateway's default, clamped to the gateway operator's range), `tavu_whatsappphonenumberid` (the WhatsApp number the agent receives on and replies from), `tavu_onlinemeeting` (Yes/No, default Yes: booked meetings get an online meeting link) and source switches (`tavu_usecompanyprofile`, `tavu_useworkinghours`, `tavu_useknowledgearticles`). Whole numbers cannot carry a column default in Dataverse, so the runtime treats an empty turn limit, abandon time or approval timeout as 12, 48 and 5. `tavu_reopenafterhours` (0 to 720, empty = 24) is how long after an off-topic, opted-out or disqualified close the person's messages are only recorded; after it, a new message starts a new conversation (0 = at once).
  - `tavu_qualificationcriterion` (organization-owned): the questions as data. Parental to `tavu_agent` (deleting the agent deletes its criteria); `tavu_order`, `tavu_purpose`, `tavu_required`, `tavu_weight` (0 to 100), `tavu_disqualifyingrule`, `tavu_leadfield`.
  - `tavu_conversation` (user or team owned): one record per conversation, messages inside the `tavu_transcript` File column (JSON, up to 32 MB). Header columns for reporting: `tavu_channel`, `tavu_channelcontactid`, `tavu_phone` (the person's phone as the channel gave it, used to reply outside an inbound turn), `tavu_displayname`, `tavu_language`, `tavu_score` (sum of criterion weights), `tavu_intent` (High, Medium, Low), `tavu_outcomereason`, response timings, `tavu_messagecount`, `tavu_tokensused`, `tavu_answers` (JSON), `tavu_summary` (plain text written by the agent: the answers so far, score, intent and booking result; the same text the lead gets in `tavu_sourcedetails`, shown on the form instead of the JSON), `tavu_pendingreply` (Propose mode), `tavu_lastmessagepreview`, `tavu_booking` (JSON written by the agent while it books a meeting: step, time zone, offered slots) and `tavu_appointmentid` (the Bookings appointment it created). On the form, `tavu_answers`, `tavu_booking` and `tavu_appointmentid` sit in a Technical tab. Lookups to `tavu_agent`, `tavu_lead` and `contact` (delete removes the link). The gateway's CreateOrUpdateLead action sets `tavu_lead`: it updates the open lead with the same email or mobile phone, or creates one with `tavu_source` WhatsApp and the qualification summary in `tavu_sourcedetails`, owned by the agent's handoff user or team; `Pl.Lead.Triage` then runs on a created lead as on any other. The "Inactive Conversations" view lists the closed ones (Qualified, Disqualified, Closed) with outcome reason, score, intent and lead; the Conversation Associated View lists every state and is the one the lead form's Conversations subgrid uses. Status reasons: Active, Awaiting Approval, With Human; inactive Qualified, Disqualified, Closed.
  - Extensions: `tavu_companyprofile` (`tavu_servicesdescription`, `tavu_targetcustomer`, `tavu_tone`, `tavu_neversay`, read by every agent; `tavu_publiclinks`, one `label: URL` per line, the links an agent may share besides `tavu_website`: both are listed to the model and allowed by the hard-limit check only when the agent's company profile source is on); `tavu_knowledgearticle` (`tavu_body`, `tavu_audience` Public or Internal with Internal as default, status reasons Draft and Published; agents read only Public and Published); `contact.tavu_whatsappid` (returning-person match); `tavu_systemsettings.tavu_conversationretentiondays` (empty keeps conversations). Choices: `tavu_conversationchannel`, `tavu_intent`, `tavu_agenttype`, `tavu_agentmode`, `tavu_knowledgeaudience`; `tavu_leadsource` gains WhatsApp and Email; `tavu_aitaskkeyconfig` gains Lead Qualification.
  - Security: OpenTavu Sales reads agents and criteria and works conversations at business-unit depth; OpenTavu Admin has full access; OpenTavu Gateway creates and updates conversations and leads, assigns them to the handoff owner, and reads agents, criteria, teams and the published sources.
- **Plugins** (C#, sandbox, signed with `_Shared/Common/OpenTavu.snk`; one assembly each, all shipped in the managed solution):
  - Service:
    - `Pl.Case.Categorize`: Module 1 Smart Case Categorization (async, Create of `tavu_case`); routes AI through the gateway via `GatewayProvider`.
    - `Pl.Case.SlaAssignment`: Pre-Op computes SLA target dates (calendar-aware, from `createdon`); async Post-Op calls the gateway to schedule the Warning/Breach durable timers and stores `tavu_slaorchestrationid`.
    - `Pl.Case.CustomerSync`: mirrors the polymorphic `tavu_customer` to typed `tavu_account`/`tavu_contact` (so Quick View forms load) and sets `tavu_primarycontact` for B2C; validates Customer Mode. Case-side twin of `Pl.Opportunity.CustomerSync`.
    - `Pl.CaseInteraction.CaseSync`: on Create of `tavu_caseinteraction`, propagates derived fields to the parent case (the first outbound interaction stamps `tavu_firstresponsedate`).
  - Sales:
    - `Pl.Opportunity.LifecycleTracker`: lifecycle fields (stage-change date, stage probability default), close and reopen probability, close-input validation.
    - `Pl.Opportunity.CustomerSync`: polymorphic `tavu_customer` to typed lookups, with Customer Mode validation.
    - `Pl.Opportunity.CloseOrchestrator`: Post-Op on close: writes the `tavu_opportunityclose` history log and marks `tavu_iscustomer` on Won. See `opportunity-close-dialog.md`.
    - `Pl.Opportunity.ForecastStamp`: links each opportunity to the Sales Target it counts toward (`tavu_salestarget`), which the target rollups aggregate.
    - `Pl.SalesTarget.KeyStamp`: stamps the dedup key `tavu_targetkey` on `tavu_salestarget` from its dimensions.
    - `Pl.ProposalLine.Calculator`: line money fields, header rollup totals and the parent-lock guard.
    - `Pl.Proposal.LifecycleTracker`: version default, transition guard, lock, one Approved per opportunity. See `proposal-lifecycle.md`.
    - `Pl.Proposal.CloneVersion`: implements `tavu_CloneProposalVersion` (Create New Version).
    - `Pl.Proposal.BuildEmailDraft`: implements `tavu_BuildProposalEmailDraft`: the gateway writes the email and renders the branded PDF; the plugin creates the draft email with the PDF attached.
  - Leads and activity capture (Module 3):
    - `Pl.Lead.Triage`: AI lead triage (async, Create of `tavu_lead`): deterministic matches first, then AI for fuzzy matches, real prospect or noise, and field extraction.
    - `Pl.Lead.PromoteLead`: implements `tavu_PromoteLead`, the human step that links a lead to an existing contact or creates the contact and account.
    - `Pl.Meeting.Capture`: AI extraction on Create of `tavu_meeting` (summary, discovery, attendee match, suggested opportunity); it never associates by itself.
    - `Pl.Meeting.Associate`: implements `tavu_AssociateMeeting`, the human step that commits a meeting to an opportunity.
    - `Pl.Meeting.BuildEmailDraft`: implements `tavu_BuildMeetingEmailDraft`, the AI follow-up email as a draft.
    - `Pl.MeetingSource.Probe`: implements `tavu_ProbeMeetingSource`, the server-side proxy the "Enable Teams sync" wizard uses to read the gateway's Teams health without exposing the tenant key.
  - Configuration:
    - `Pl.Setup.Initialize`: implements `tavu_InitializeConfiguration` ("Verify and complete configuration"): seeds the reference rows, creates sales periods and runs the checks. See `setup-initialize-design.md`.
    - `Pl.SystemSettings.SingleRecordGuard` and `Pl.CompanyProfile.SingleRecordGuard`: keep System Settings and the company profile to one record each.
- **Custom APIs:** `tavu_InitializeConfiguration`, `tavu_PromoteLead`, `tavu_AssociateMeeting`, `tavu_BuildMeetingEmailDraft`, `tavu_ProbeMeetingSource`, `tavu_CloneProposalVersion`, `tavu_BuildProposalEmailDraft`, each implemented by the plugin named above.
- **PCF controls (React + Fluent v9):** `Ctrl.Case.AiAssessment` (case AI panel), `Ctrl.Case.SlaCountdown` (live SLA countdown bars on the Response and Resolution target dates), `Ctrl.Case.CaseConversation` (the case's threaded conversation over `tavu_caseinteraction`), `Ctrl.Meeting.AiSummary` (the meeting's AI capture panel), `Ctrl.Forecast.Dashboard` (attainment and forecast composition by company, business line or rep).
- **Custom pages:** `tavu_opportunityclosedialog`: guided Close as Won/Lost dialog for the opportunity.
- **Web resources:** `tavu_systemsettings_open.html` (settings singleton); form scripts `tavu_opportunity_form.js` (Customer Mode filter/mirror, lifecycle UI, close/reopen), `tavu_proposal_form.js` (lifecycle buttons, visual lock, header totals auto-refresh), plus `tavu_account_form.js`, `tavu_contact_form.js`, `tavu_product_form.js`, `tavu_proposalline_form.js`.
- **Shared code** (linked, not a DLL): `_Shared/Common` (PluginBase, LocalPluginContext) and `_Shared/AI` (`IAIProvider`, `AzureOpenAIProvider`, `OpenAIProvider`, `GatewayProvider`, `AIProviderFactory`, `AIConfigResolver`).

---

## 2. The firm's gateway (Azure Function App)

One **C# .NET 10 isolated** Function App per firm (`github.com/opentavu/gateway`), with a storage account (Durable Functions state, the agent's queues and tables), a Key Vault and Application Insights with a daily cap (Decision 60). It holds no firm data at rest besides that working state.

- **Hosting target:** Flex Consumption (Linux) with one 512 MB always-ready instance for every firm, so there is no cold start against the agent's reply target and no storage account key (the host and the gateway's own clients connect with the managed identity). **Until the move:** Windows Consumption, which needs the storage key (its content share), kept as a Key Vault reference. Linux Consumption cannot run .NET 10.
- **Capabilities:**
  - **AI gateway** (`/api/ai/complete`): the in-tenant plugins send the task's prompt and content with the tenant key; the gateway calls the firm's model (an Azure OpenAI resource in the firm's subscription, or another provider with the firm's key) and returns the completion and token counts. Which model a task uses is the task's row in `tavu_aitaskconfiguration`, among the models the gateway allows.
  - **SLA scheduler** (Durable Functions): `Pl.Case.SlaAssignment` sends `(caseId, warningTime, failureTime)`; a durable orchestration fires at each time and writes `tavu_slastatus` back only while the case is open; a re-categorization cancels and reschedules by the stored instance id.
  - **Intakes:** email to case (Microsoft 365 through Graph, or IMAP), Teams transcripts for Meeting Capture.
  - **Agent runtime:** the Lead Qualification Agent on WhatsApp (webhook, queues, guards, model turn, named actions, in-chat booking through Microsoft Bookings, owner notifications by email).
- **One gateway per firm** until a tenant registry pays for itself (Decision 62): the registry (tenant key to Dataverse URL, Entra tenant, Key Vault secret names, WhatsApp number and mailboxes, resolved per request) is built only when operating many gateways costs more than building it.

---

## 3. Authentication and access

- **Gateway to Dataverse and Microsoft 365:** an Entra app registration in the firm's own tenant, added as an application user in its Dataverse (S2S, client credentials, no interactive prompt). Graph application permissions are scoped in Exchange Online to the mailboxes the gateway uses (the notification sender, the case intake mailbox, the Bookings business). **Target, with Flex Consumption:** the Function App's managed identity instead of the app's client secret, possible because the gateway runs in the firm's own tenant.
- **Plugins to gateway:** HTTPS with a tenant key in the `X-OpenTavu-Tenant-Key` header. The firm's environment holds only the gateway base URL and that key, in two environment variables (`tavu_GatewayUrl`, `tavu_GatewayKey`), never a model key. Endpoints are Anonymous at the Functions layer; the tenant key is the credential (unknown key: 401).
- **WhatsApp webhook:** Meta's signature on every event, checked with the Meta app secret.
- **Opentavu's support access:** Azure resources through **Azure Lighthouse** (the firm sees and can revoke the delegation: Contributor on the Opentavu resources, a restricted User Access Administrator to assign named roles to the gateway's identity, and the role to remove the delegation). Microsoft 365, Entra and Power Platform: **GDAP** as the target (Power Platform Administrator, Cloud Application Administrator, Exchange Administrator, never Global Administrator); until then, a named Opentavu admin account in the firm's tenant with only those three roles and MFA, removed at offboarding.
- **MFA** applies to human sign-ins only; it does not affect the service-to-service flows above.

---

## 4. Where each setting lives (Decision 62)

| Kind | Home | Examples |
|---|---|---|
| The firm's data and business configuration | **Dataverse** | taxonomy, business calendars, SLAs, tiers, AI task prompts and models (`tavu_aitaskconfiguration`), confidence threshold and AI kill switch (`tavu_systemsettings`) |
| How the firm's agents behave, and its identity | **Dataverse** | per agent on `tavu_agent`: messages, limits, the Conversation tab, the WhatsApp number, online meetings; firm-wide on System Settings: the notification sender and case intake mailboxes |
| Secrets | **Key Vault** (references read by the Function App's managed identity) | WhatsApp token and app secret, IMAP password, Entra client secret, model key, tenant keys; the storage key only while the plan needs one |
| Bootstrap and operator settings | **Function App app settings** | Dataverse URL, tenant id, Entra client id; the models the gateway allows; API versions; technical protections; the operator's range for every per-agent value (the gateway clamps, never refuses) |

The test for where a value lives is whose decision it is: the firm's goes to Dataverse, the operator's to the gateway, and a secret to Key Vault.

---

## 5. Flows

**Module 1 (categorization):** case created → `Pl.Case.Categorize` (async) gathers case text + active taxonomy → calls the gateway `/ai/complete` → validates the result against active config (anti-hallucination) → writes AI fields → sets status by confidence/multi-intent. The `AiAssessment` PCF renders the result.

**SLA:** categorization sets Type → `Pl.Case.SlaAssignment` resolves SLA (Tier+Type) + calendar, computes Response/Resolution Target Dates from `createdon` (business-hours + closures, DST-aware) → calls the gateway to schedule Durable timers → gateway fires at warning/failure → updates status + acts. The `SlaCountdown` PCF shows a live client-side countdown.

---

## 6. Regions, billing, data

- **Billing:** the firm's own Azure subscription (it needs a company credit card, on the installation checklist) and its own Microsoft licenses. Opentavu's own instance runs in Opentavu's subscription, in Central US.
- **Data residency:** the firm's data lives in its Dataverse and Microsoft 365, in their regions. The gateway, in the firm's subscription, processes it transiently and keeps only its working state (queues, tables, Durable state). The model is the firm's own resource; Azure OpenAI and OpenAI do not train on API data.
- **Data protection:** no client data passes through Opentavu's infrastructure. Opentavu's support access (Lighthouse, GDAP or the admin account) can see the firm's data; the firm sees it and can revoke it, and the client agreement covers it. *(Legal review required.)*

---

## 7. Per-firm installation (the repeatable part)

1. **Accounts:** the firm's Microsoft tenant and Azure subscription (the firm's card); Power Platform licenses and a Dataverse environment.
2. **Access:** the Lighthouse delegation from an ARM template, and Opentavu's admin account (until GDAP).
3. **Gateway:** deploy the Function App, storage, Key Vault and Application Insights (daily cap) in the firm's subscription; the Entra app registration and its application user in Dataverse; secrets into Key Vault; the bootstrap app settings.
4. **Solution:** import the managed solution; set `tavu_GatewayUrl` and `tavu_GatewayKey`; run Verify Setup.
5. **Configure:** business calendars, SLAs, tiers, taxonomy, System Settings (with the two mailboxes), and each agent (its WhatsApp number). Much ships as seed; the rest is a short checklist (`configuration.md`).

---

## 8. Cost (paid by the firm)

- **Gateway hosting:** Flex Consumption with one 512 MB always-ready instance, about USD 5 a month for the baseline plus near-zero execution at a small firm's load (confirm in the pricing calculator). On Windows Consumption, until the move, the free grant covers this workload. Storage, Key Vault and Application Insights (with its daily cap) add cents to a few dollars.
- **AI:** the firm's own model usage, measured per task and per conversation.
- **Power Platform:** licenses and capacity (Decision 60).

---

## 9. Current status and roadmap

- **Built and live:** the AI gateway, the SLA scheduler, the intakes and the Lead Qualification Agent run on Opentavu's own gateway (`opentavu-gateway`, Central US, .NET 10 on Windows Consumption since 2026-10-29). Module 1 categorization runs through the gateway; the model key is not in the client tenant.
- **Next (hosting):** move to Flex Consumption with one always-ready instance, the managed identity for Dataverse, Graph and storage (no Entra secret, no storage key), before the agent goes live on Opentavu's own number.
- **Next (access):** GDAP once Opentavu LLC is enrolled as a CSP indirect reseller.
- **Later:** the tenant registry, only when operating many gateways costs more than building it.

---

## Document control

| Version | Date | Author | Notes |
|---|---|---|---|
| 2.0 | October 29, 2026 | Gustavo González Villani (with Claude) | Decision 62: one stack per firm in the firm's own Microsoft tenant and Azure subscription, paid by the firm; the gateway is one Function App per firm (Flex Consumption with one always-ready instance as the target, Windows Consumption until then); authentication and Opentavu's support access (Lighthouse, GDAP, an admin account until then); where each setting lives (Dataverse, Key Vault, app settings); installation, cost and status rewritten; the plugin, Custom API and PCF lists now match what ships. Replaces the shared multi-tenant central layer of 1.0. No em dashes. |
| 1.9 | October 28, 2026 | Gustavo González Villani (with Claude) | The firm's identity moves from gateway app settings to its Dataverse: on `tavu_agent`, `tavu_whatsappphonenumberid`, `tavu_onlinemeeting` and the seven Conversation tab limits (collapsed tab after Scheduling); on `tavu_systemsettings`, `tavu_notificationsendermailbox` and `tavu_caseintakemailbox` (Microsoft 365 Mailboxes section). |
| 1.8 | October 16, 2026 | Gustavo González Villani (with Claude) | `tavu_reopenafterhours` on `tavu_agent` (Limits section, after Abandon After (Hours)): the reopen window after an off-topic, opted-out or disqualified close. The agentic layer bullet now says the runtime is built in the gateway. |
| 1.7 | October 15, 2026 | Gustavo González Villani (with Claude) | `tavu_summary` on `tavu_conversation` (readable summary on the form; the JSON columns move to a Technical tab); the Conversation Associated View shows every state and backs the lead form's Conversations subgrid. |
| 1.6 | October 14, 2026 | Gustavo González Villani (with Claude) | Lead Qualification Agent creates leads: `tavu_createleadfordisqualified` on `tavu_agent` (toggle after Handoff Team on the form), the CreateOrUpdateLead behavior on `tavu_conversation.tavu_lead`, and the reshaped "Inactive Conversations" view. |
| 1.5 | October 13, 2026 | Gustavo González Villani (with Claude) | Lead Qualification Agent: `tavu_publiclinks` on the company profile; in-chat booking columns `tavu_bookingsbusiness` and `tavu_bookingsserviceid` on `tavu_agent` (Booking section of the form, with `tavu_bookingurl`) and `tavu_booking` and `tavu_appointmentid` on `tavu_conversation` (read-only, Outcome section). |
| 1.4 | October 8, 2026 | Gustavo González Villani (with Claude) | Added `tavu_phone` to `tavu_conversation` (Lead Qualification Agent step 3), on the main form next to Channel Contact ID. |
| 1.3 | October 6, 2026 | Gustavo González Villani (with Claude) | Added the agentic layer data model for the Lead Qualification Agent (step 1, schema only): `tavu_agent`, `tavu_qualificationcriterion`, `tavu_conversation`, the company profile, knowledge article, contact and system settings extensions, the new choices and the role privileges. |
| 1.2 | July 10, 2026 | Gustavo González Villani (with Claude) | Synced the client-layer inventory to the implemented sales cycle. Added plugins `Pl.Opportunity.CloseOrchestrator`, `Pl.Proposal.LifecycleTracker`, `Pl.Proposal.CloneVersion` (and expanded the LifecycleTracker/Calculator descriptions). Added the `tavu_CloneProposalVersion` Custom API, the `tavu_opportunityclosedialog` custom page, and the opportunity/proposal form web resources. Documented the `OTC/OTO/OTP-{yyyy}-{seqnum:5}` autonumber standard. Detail in `opportunity-close-dialog.md` and `proposal-lifecycle.md`. |
| 1.1 | July 3, 2026 | Gustavo González Villani (with Claude) | Synced to the **live** deployment: gateway deployed (`opentavu-gateway`, Central US) + KeepWarm; AI now routed through the gateway via `GatewayProvider` (AI key removed from the client tenant); endpoints Anonymous + `X-OpenTavu-Tenant-Key`; wiring via `tavu_GatewayUrl`/`tavu_GatewayKey` env vars. Added `Pl.Case.CustomerSync` and the async SLA-scheduling step of `Pl.Case.SlaAssignment` (`tavu_slaorchestrationid`); `SlaCountdown` dual bars. Updated status/roadmap (case-lifecycle closure pending research; prompt/model-routing migration and auth hardening pending). Region note: Central US (East US had 0 vCPU quota). |
| 1.0 | July 1, 2026 | Gustavo González Villani (with Claude) | Initial platform architecture: two-layer multi-tenant model (client managed solution + central OpenTavu Azure Function App = AI Gateway + SLA Scheduler); multi-tenant Entra auth + S2S; client/gateway config split; Module 1 and SLA flows; regions/billing/data residency; per-client onboarding; cost; runtime (C#/.NET isolated, Durable Functions). |

*This document is the architecture reference for Opentavu. Detailed table specs live in `service-model.md` and `sales-model.md`; the product vision in `VISION.md`.*
