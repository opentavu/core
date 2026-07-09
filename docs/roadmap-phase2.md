# OpenTavu — Phase 2 Roadmap (Case module)

Backlog of enhancements after the core case + email + SLA module (Phase 1, complete). Each item follows the OpenTavu method: **research the state of the art → implement best practices → apply the AI-first lens (AI replaces the human step where it genuinely adds value, human as second-line reviewer) → document.**

**Last updated:** July 8, 2026.

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

## Done (moved out of backlog)

- SLA countdown bar shows **"Cumplido" (green)** when the case resolves Met — no more red "Overdue" on the response bar for a met case (July 8, 2026).
