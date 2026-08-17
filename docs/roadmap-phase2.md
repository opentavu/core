# OpenTavu — Phase 2 Roadmap (Case module)

Backlog of enhancements after the core case + email + SLA module (Phase 1, complete). Each item follows the OpenTavu method: **research the state of the art → implement best practices → apply the AI-first lens (AI replaces the human step where it genuinely adds value, human as second-line reviewer) → document.**

**Last updated:** August 13, 2026.

---

## 1. Case Assignment / Workload-based Routing  *(next — research phase)*

**Problem.** Today case assignment is **manual**: an agent picks up a case (native Dataverse queues, service-model §10). With more than one agent, we want **workload/capacity-aware assignment** configured per firm — distribute new cases according to each agent's configured capacity and current load, not by whoever grabs it first.

**Constraints / context.**
- Must run on Power Apps Premium — **no Dynamics 365 Customer Service license**, so native Unified Routing is out; the engine will be **custom** (plugin + config tables), building on the existing native queues.
- Cases already carry priority, type, customer tier, topical classification, and SLA — all usable as routing signals.
- Config-over-code: capacity, skills, and rules live in `tavu_*` config tables, not hardcoded.

**Approach.**
1. **Research** industry assignment/routing (round-robin, load-balanced, skills-based, presence-based, AI/ML routing) across Zendesk, Freshdesk, ServiceNow, Salesforce Omni-Channel, Dynamics Unified Routing, Jira SM, Intercom, HubSpot. Prompt: run in ChatGPT Deep Research (saved with this session); expected output → a `Conclusiones_Analisis_*` doc like the SLA one (NIW methodological evidence).
2. **Design** a workload-aware model: per-agent capacity (max concurrent open cases, optionally weighted by priority/effort), presence/working-hours, optional skills matching; assignment triggered **after categorization** (so priority/type/skills are known). Decide fairness/anti-starvation rules.
3. **AI-first lens.** Where AI genuinely helps: predicting best-agent match (effort estimate, topic-to-skill affinity, historical resolution success) and **proposing** the assignment; deterministic rules for the plumbing (capacity counting, presence). Human overrides exceptions. Do **not** AI-wash the round-robin/capacity math.
4. **Implement** as a plugin (assign on the categorized case) + config tables (`tavu_agentcapacity` / skills / routing rules — TBD after research).
5. **Document** in service-model (new section) + a conclusions doc.

**Status:** research prompt ready; pending the Deep Research run before design.

---

## 2. SLA pause governance (anti-gaming)

Layer on the status-driven pause (§11.1): **pause-duration metrics** (time paused, time paused after a customer reply, per case/agent) and an optional **auto-expire** (a paused case auto-resumes after N business days). Mitigates the "pause and forget" vector, especially when `tavu_slaautoresume = No`.

---

## 3. Module 2 — Context-Aware Customer Communication (AI-drafted responses)

The compose's AI-first endgame: Module 2 **drafts the reply** and **proposes** the status change / SLA pause by reading the thread (detecting a genuine customer-wait), with the agent as reviewer. Also: vision over inbound attachments (extract error from a screenshot/log). The conversation model, status-driven pause, and interaction deltas were built to be its render + action surface.

---

## 4. Outlook Add-in — capture contacts / leads from email (AI-assisted)

**Problem.** Reps live in Outlook. Turning an inbound email into a CRM contact or lead is manual today (copy the name, email, company, phone from the signature). This is Pain #1 (manual CRM entry) on the email surface. Microsoft's **Dynamics 365 App for Outlook** does this, but it is heavyweight and requires Dynamics licensing; OpenTavu wants a **focused, AI-assisted** version that reinforces the "tightly Microsoft-connected" positioning and shows well in demos.

**Constraints / context.**
- An **Office Add-in** (Office.js) that runs in **Outlook on the web and desktop** (New + classic). Works on the Power Apps Premium stack, with **no dependency on the Dynamics 365 App for Outlook**. Note: the add-in runs in **Outlook on the web with Business Basic**; the **desktop** Outlook app needs Business Standard.
- **Auth:** Entra SSO (nested-app auth) so calls to the Dataverse Web API run as the acting user (respect privileges; same human-gate philosophy as lead promotion). The taskpane can be hosted on the OpenTavu gateway static site.
- **Match-first (dedup):** match contact by email, account by domain/name **before** creating, reusing the meeting auto-provision / `tavu_PromoteLead` helpers. Never create duplicates.

**Approach.**
1. **Research** Office Add-in patterns and the Dynamics App for Outlook UX; identify the minimal "capture the sender" flow and the New-Outlook add-in constraints.
2. **Design** the taskpane: show the sender, the **AI-extracted fields** (name, company, title, phone, editable), and buttons **Create contact** / **Create lead**; if an existing match is found, offer **Link** instead of create (same 2nd-line human gate as leads/meetings).
3. **AI-first lens.** The gateway AI reads the email **signature/body** and **proposes** the contact + account fields and a suggested action (contact vs lead); the human confirms. Deterministic dedup plumbing (email/domain match). Do not AI-wash the matching.
4. **Implement** as: add-in manifest + taskpane (hosted on the gateway) + Dataverse Web API calls via SSO, reusing the existing account/contact match-or-create logic (shared with Module 3B provisioning).
5. **Document** as its own module doc; connect to Pain #1 and the Microsoft-native narrative (NIW).

**Status:** roadmap / high-level design only. Depends on the M365 demo tenant (Business Basic is enough for Outlook-web demos; Standard for desktop). Sequenced **after** the meeting-capture connectors (Teams / note-taker), which share the AI-extraction + match-or-create plumbing.

---

## 5. Conversational Assistant — NL command layer over the Custom-API action spine  *(research phase)*

**Problem.** Reps want to *tell* the CRM what to do instead of navigating forms: "create this prospect", "draft the need and attach it to the opportunity", "follow up on this deal". Manual navigation + data entry is the friction behind low CRM adoption (Pain #1) and weak follow-up discipline (Pain #4). Idea seeded by a peer (Aug 2026): give the rep an assistant they can instruct in natural language (text first, audio later) that does the work for them.

**Design decision — NOT a free-form chatbot, and NOT a bare command menu.** A **natural-language chat surface backed by function-calling**, where the callable tools are the **existing guard-railed Custom APIs** (`tavu_PromoteLead`, `tavu_AssociateMeeting`, the opportunity close engine, the proposal lifecycle, account/contact match-or-create, `BuildEmailDraft`, discovery consolidation, …). The model **proposes** a tool + parameters and asks for missing required fields (slot-filling); it **never writes to Dataverse directly**. A **confirmation gate** executes the deterministic Custom API **as the acting user** (UserService — the rep stays accountable), reusing the same hallucination guard (only set lookups that resolve to real records) and confidence gating as Modules 1–3. This gives both the safety of a bounded action catalog and the ease of natural language, and it is the capstone AI-first surface: the rep talks, the AI orchestrates the deterministic engines already built, human as second-line reviewer.

**Constraints / cuts (Design Mindset).**
- **No per-user model picker** (fails simplicity): provider/model is a **tenant-admin** choice via the gateway `IAIProvider` (provider-agnostic). The rep should not know or care which model runs.
- **Audio is a later input adapter** (speech → text → same pipeline, consistent with the "capture is an adapter" pattern). **Text only** for MVP; audio does not change the architecture.
- **Bounded intent catalog v1**, each intent mapping 1:1 to a proven Custom API: create lead / account / contact, draft a need → attach to an opportunity (reuse discovery consolidation + `BuildEmailDraft`), follow-up on an opportunity. Free-form org-wide RAG and autonomous multi-step agency are **out of MVP**.
- **Broker in the gateway** (`/api/assistant` runs the tool-calling loop): keeps token control and provider-agnosticism; the model is never called from the browser. Token budget per action; cache the tool schema.

**Honest competitive positioning (NIW-relevant).** D365 Copilot for Sales already offers NL chat and beats us on breadth — **we do not compete on breadth.** OpenTavu wins on (1) price-tier accessibility for SMBs, (2) provider-agnostic (bring-your-own-model), and (3) an **opinionated, guard-railed action spine tuned to the 7 pains**, not a free agent. The value is the deterministic action layer beneath the chat, not the chat itself. If that differentiator cannot be articulated, this is just a worse Copilot — that is the failure mode to avoid.

**Reuse (little is greenfield).** `Ctrl.Case.CaseConversation` (PCF chat-thread base) for the UI; the ~18 existing Custom APIs as the tool catalog; the gateway as the model/function-calling broker.

**Approach.**
1. **Research / triangulation** (like SLA & routing): agentic/NL assistants in CRM for SMB sales, function-calling / tool-use patterns and guardrails, build-vs-Copilot trade-offs. Fold in the 2–3 IEEE/ACM papers already in the backlog. Output → a `Conclusiones_Analisis_*` doc (NIW methodological evidence).
2. **Design** the intent catalog + the tool schema (map each intent to a Custom API + its required/optional params), the slot-filling loop, the confirmation-gate UX, and the `/api/assistant` broker contract.
3. **AI-first lens.** The model classifies intent + fills parameters and **proposes**; deterministic Custom APIs do the writes under the human gate. Do **not** AI-wash the plumbing.
4. **Implement** the gateway broker + a chat PCF (extending `Ctrl.Case.CaseConversation`) + the confirmation gate, wiring only the v1 intents.
5. **Document** as its own module doc; connect Pains #1 / #4 and the "unifies Modules 1–3 under one agentic interface" narrative (NIW).

**Status:** roadmap / idea captured. Research prompt to be prepared and triangulated before design. Sequenced after the current meeting-capture / intake frente.

---

## Done (moved out of backlog)

- SLA countdown bar shows **"Cumplido" (green)** when the case resolves Met — no more red "Overdue" on the response bar for a met case (July 8, 2026).
