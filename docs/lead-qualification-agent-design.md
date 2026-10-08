# Lead Qualification Agent: design

**Status:** Draft v0.9 for review · **Date:** 2026-10-08 · **Owner:** Gustavo González Villani · **Decisions:** 51 (this agent first, channel-agnostic, WhatsApp first), 52 (agent type is code, agent instance is data; generic agent layer), 47 (agentic layer, autonomy levels, principles)

**Inputs:** `1-Producto/Research/Agentic_Architecture/agentic-architecture-triangulation.md` (how Microsoft built its agent layer), `2-Negocio/Go_To_Market/expanded-avatar-voice/expanded-avatar-voice-triangulation.md` (what owners accept and reject from an AI that answers for them), the WhatsApp webhook already in the gateway (`Functions/WhatsAppWebhookFunction.cs`, commit `50c6681`).

---

## 1. What it is

An L2 agent that answers every inbound lead within seconds, on the channel the person used, asks the questions the firm configured, decides whether the lead is worth a person's time, and hands it to a human with a concrete next step (a meeting link or a callback time). It never sells, never quotes, never invents.

**In scope (v1):** WhatsApp (first adapter), then the web form and inbound email adapters; one agent per firm; propose mode and act mode; handoff to the lead owner; the conversation stored in Dataverse.

**Out of scope (v1):** outbound campaigns (TCP rules), voice, Instagram and Facebook DMs, a web chat widget (next adapter after email), booking a calendar slot directly (v1 sends the firm's booking link), multi-agent handoffs (A2A).

## 2. Design principles

1. **Configured steps, LLM inside.** Qualification is a known sequence, so the steps and criteria are data and the runtime drives them. The model understands the message, extracts values, and writes the reply. It does not decide the flow on its own. (Microsoft triangulation, conclusion 3.)
2. **Hard limits live in the runtime, not in the prompt** (Decision 47, principle 1). The reply is checked before it is sent: no prices, dates or commitments that are not in the firm's configuration; no links other than the configured ones.
3. **Nobody goes unanswered; a human is always reachable.** Turn limit, explicit "talk to a person" detection, and out-of-hours messages. (Customer voice: "they just do not want to be ignored", "customers hate AI when it blocks them or guesses".)
4. **One agent, any channel.** Channel adapters normalize the message and enforce channel rules before the model is called.
5. **Act by default, safely.** When the owner is away the agent answers on its own (no approval), because approval adds friction and the lead goes cold. What protects the firm is not a human approving each reply but the runtime check: the agent only states what the firm published (company profile, working hours, public knowledge articles); when it cannot answer from that, or the person shows real interest, it offers a session with a human. Propose mode stays available per firm (for example a calibration week) but is not the default. (Gustavo, 2026-10-06; amends the "propose mode mandatory" wording of Decision 51.)
6. **Simpler than the Microsoft equivalent.** Microsoft spreads this across Copilot Studio, Contact Center, `msdyn_ocliveworkitem`, `msdyn_transcript` and the Sales Qualification Agent tables. OpenTavu uses one conversation table, one agent table and one criteria table.
7. **Agent type is code, agent instance is data** (Decision 52). The runtime has a generic agent layer shared by every agent type (guards, model call with budget, safety flags, reply check, propose or act, named actions, trace) and an agent type behind a small contract (`IAgentType`, resolved from `tavu_agenttype`) that holds only the decision rules of one kind of agent. This document describes the first type, `LeadQualificationType`. A second type (Post-Meeting) must not require changes to the generic layer.

## 3. Architecture

```
WhatsApp / Web form / Email           Gateway (Azure Functions, .NET 8, public)                                   Dataverse
───────────────────────────           ──────────────────────────────────────────                                   ─────────
webhook or poll ──► Channel adapter ──► normalized message ──► queue ──► Agent runtime
                    (verify, map,                                         ├─ Generic agent layer ◄──────────────── agent, criteria,
                     dedupe, rules)                                       │   guards, model call with budget,       company profile,
                                                                          │   safety flags, propose or act,         conversation
                                                                          │   named actions, transcript and trace ─► conversation, lead,
                                                                          └─ Agent type (IAgentType)                 owner notification
                                                                              LeadQualificationType: decision rules
person ◄── Channel adapter ◄── SendReply (named action, reply check inside) ◄──┘
```

- **Webhook stays thin:** validate, normalize, enqueue (Azure Storage Queue), return 200. The agent runs in a queue-triggered function, so Meta never waits for the model and retries cannot cause double replies (idempotency by provider message id).
- **The runtime is the gateway's agent loop** (Decision 47 closed question: the runtime lives in the gateway, not in plugins).
- **Generic agent layer** (shared by every agent type): conversation resolution, guards, the model call with token budget and structured output, the fixed safety flags, propose or act, the named actions (section 6.1) and the transcript with its uniform trace (section 5.1).
- **Agent type** (`IAgentType`, resolved from `tavu_agenttype`): given the conversation state, the agent instance and the model's structured reading, it returns the next decision and the actions to run. It contributes its own part of the prompt and of the output schema. It never calls Dataverse or a channel directly: every effect goes through a named action.

## 4. The conversation, turn by turn

**Conversation states** (`statuscode` of `tavu_conversation`):

| State | Meaning |
| :--- | :--- |
| Active | The agent is talking to the person |
| Awaiting Approval | Propose mode: a reply is drafted and waits for a person |
| With Human | The agent stopped; a person owns the conversation |
| Qualified (inactive) | Criteria met; handed off with a next step |
| Disqualified (inactive) | A disqualifying answer; closed politely |
| Closed (inactive) | Abandoned (no reply within the configured time) or opted out |

**One turn (inbound message).** Each step is marked **[Generic]** (generic agent layer, the same for every agent type) or **[Type]** (`LeadQualificationType`).

1. **[Generic] Adapter and conversation:** resolve the conversation (open one for this channel identity, or create it), check channel rules (WhatsApp: inside the 24-hour window), record the inbound entry.
2. **[Generic] Guards (deterministic, no model):** conversation With Human or closed → store the message only and run `NotifyOwner`. Turn limit reached → hand off (`HandToHuman`, `NotifyOwner`). Outside working hours → send the configured out-of-hours message once through `SendReply`, keep collecting.
3. **[Generic] Understand (one model call, structured JSON output, within the token budget):** the output schema has a **fixed generic part** that every conversational agent type returns and no type can omit or override: detected language, `optOut`, `wantsHuman`. **[Type]** adds its own part of the prompt and of the schema: values for pending criteria with a confidence each, flags for an off-topic question and a disqualifying answer, and the buying intent.
4. **[Generic] Safety flags first, executed by the runtime:** `optOut` → `CloseConversation` (Closed, opted out). `wantsHuman` → `HandToHuman` and `NotifyOwner`. The agent type is not consulted for these two.
5. **[Type] Decide (deterministic):** update criteria answers above the confidence threshold; then, in order: disqualifier → disqualify; all required criteria answered → qualify and hand off; question the agent can answer from published sources (`ReadPublishedSources`: company profile, working hours from the business calendar, public knowledge articles, each source switchable per agent) → answer it, then ask the next missing criterion; question it cannot answer from those sources → do not improvise: offer a session with a human (booking link) and mark the conversation for follow-up; otherwise → ask the next missing criterion. The output is a decision plus the named actions to run.
6. **[Type] Compose (same model call where possible):** the reply in the person's language and the firm's tone, short, one question at a time.
7. **[Generic] Act or propose, inside `SendReply`:** every outbound text, including the out-of-hours and holding messages, goes through `SendReply`, which runs the hard-limit check (deterministic rules only: channel length, links other than the booking URL, amounts that look like prices). The firm's never-say list is guidance for the model in the prompt, not a deterministic rule. If the check fails → `HandToHuman` instead of sending. When the decision includes the booking link (next step, offer of a human session), the runtime appends `tavu_bookingurl` to the text; the link is never written into the configured messages. A send that fails for a transient reason (provider error 5xx, network) is retried from the `agent-outbound` queue with growing delays; a permanent failure, or the last failed retry, writes a `system` entry and runs `NotifyOwner`, so a person answers instead. Act mode (default) → send through the adapter. Propose mode (optional per firm) → status Awaiting Approval, reply stored as pending for the lead owner or the owner's team. In propose mode two things keep the promise of a fast answer: (a) the approver is notified on the phone at once (Teams mobile notification or an email with a direct link, configurable) and can approve, edit or reject from the model-driven app on mobile; (b) if nobody approves within `tavu_approvaltimeoutminutes` (default 5), the agent sends the configured holding message once through `SendReply` ("Thanks for writing, I will get back to you in a few minutes with the details") so the person is never left without an answer, and the drafted reply keeps waiting.
8. **[Generic] Record:** append the outbound entry with its trace fields (section 5.1), update counters and timings, tokens used.

**Handoff (a sequence of named actions):** `CreateOrUpdateLead` (source WhatsApp, captured answers in `tavu_sourcedetails`, score) and link it to the conversation **[Type decides, action is generic]**; `NotifyOwner` with a summary and a link; `SendReply` with the configured next step (booking link or "someone will call you today before 5 pm"); `CloseConversation` as Qualified. On qualification the owner is notified **without** `HandToHuman`, so the agent is not silenced; `HandToHuman` is only for the person asking for a human, the turn limit or a failed check. Lead Triage then runs as it does for every new lead (no source gate), matching against existing contacts.

## 5. Data model

Every new table and field passes the Quick Test; the answers are recorded here so the review is explicit.

### 5.1 `tavu_conversation` (new)

One record per conversation; the messages travel inside it (pattern used by Dynamics 365 `msdyn_ocliveworkitem` plus `msdyn_transcript`, and by Salesforce Messaging Session with off-core entries). Reports run on this header, never on individual messages; this also keeps database capacity (the expensive one) low.

| Column | Type | Purpose |
| :--- | :--- | :--- |
| `tavu_name` | Text | "WhatsApp, {display name}, {date}" |
| `tavu_channel` | Choice (new `tavu_conversationchannel`: WhatsApp, Web Form, Email, Web Chat) | Reporting by channel |
| `tavu_channelcontactid` | Text | The identity on that channel (WhatsApp user id or phone, email address) |
| `tavu_phone` | Text | The person's phone as the channel gave it (WhatsApp `messages[].from`). Needed to reply outside an inbound turn (holding message, retries), because WhatsApp test numbers and some accounts accept only phone numbers as recipients. Added 2026-10-08 (step 3). |
| `tavu_displayname` | Text | Name the channel gives |
| `tavu_agent` | Lookup `tavu_agent` | Which agent handled it |
| `tavu_lead`, `tavu_contact` | Lookups | Result of the handoff or of matching |
| `tavu_language` | Choice (`tavu_language`) | Detected language |
| `tavu_score` | Whole number 0 to 100 | Fit score: sum of the weights of the criteria answered and not disqualified (deterministic, explainable) |
| `tavu_intent` | Choice (High, Medium, Low) | Buying intent read by the model from the conversation (urgency, explicit requests, hesitation), kept separate from the score so the score stays auditable; used to prioritize handoffs |
| `tavu_outcomereason` | Text | Why qualified, disqualified or handed off |
| `tavu_firstinboundon`, `tavu_firstresponseon`, `tavu_lastinboundon` | Date and time | Response-time reporting; WhatsApp 24-hour window |
| `tavu_responseseconds` | Whole number | First response time, for the "nobody waits" promise |
| `tavu_messagecount`, `tavu_tokensused` | Whole number | Volume and cost per conversation |
| `tavu_answers` | Multiline text (JSON) | Captured criterion values with confidence |
| `tavu_pendingreply` | Multiline text | Propose mode: the drafted reply waiting for approval |
| `tavu_transcript` | File (JSON) | Inbound, outbound and system entries in one uniform format for every agent type (see "Transcript entry format" below): the execution trace of Decision 47 §7.3, ready for a future `tavu_agentrun` without migration (Decision 52). File capacity is far cheaper than database capacity and has no practical size limit, the same choice Dynamics 365 makes in `msdyn_transcript` |
| `tavu_lastmessagepreview` | Text (single line) | Last message, for views and for the owner's quick scan without opening the file |

Quick Test: pain = follow-up gaps and context loss (VISION); AI-first = the agent owns it, the human approves exceptions; validated by customer voice (owners want nobody ignored and full context on handoff); tenant-configurable = yes through the agent; simpler than Dynamics (one table instead of three). **Passes.**

Decided (2026-10-06): `tavu_transcript` is a File column from v1, so storage scales without a later migration. Consequences accepted: the conversation is shown through a PCF control (`Ctrl.Conversation.Transcript`, chat bubbles) that reads the file, and the gateway reads and rewrites the file on each turn (one extra call, small latency). Full-text search across conversations is not a v1 need; reports run on the header columns.

**Transcript entry format (Decision 52, constraint 3).** The file is a JSON array of entries. Every entry has the same fields whatever the agent type; agent fields are null on inbound entries.

```json
{ "seq": 7, "at": "2026-10-08T14:02:11Z", "direction": "in | out | system",
  "text": "...", "providerMessageId": "wamid....",
  "agentType": "LeadQualification", "decision": "AskNextCriterion",
  "action": "SendReply", "effectClass": "ExternalCommunication",
  "confidence": 0.86, "approval": "none | pending | approved | edited | rejected | timeout",
  "tokens": { "in": 812, "out": 64 } }
```

- `direction: system` records an effect with no message (for example `NotifyOwner` or `CloseConversation`).
- `decision` names come from the agent type; `action` and `effectClass` come from the named-action catalog (section 6.1).
- The entry is written by one component of the generic layer (`TranscriptStore`); agent types never write to the file.

### 5.2 `tavu_agent` (new)

The agent definition as data (Decision 47 §7.2; mirrors Microsoft's `bot` table). Resolves the open question of Decision 47: a dedicated table, because one agent will use several AI tasks.

| Column | Purpose |
| :--- | :--- |
| `tavu_name`, `tavu_agenttype` (Lead Qualification; Post-Meeting later) | Identity |
| `tavu_mode` (Propose, Act) | Human approval before sending, or not |
| `tavu_aitaskconfiguration` (lookup) | Model, temperature, token limits and system prompt, reusing `tavu_aitaskconfiguration` (new task key "Lead Qualification") |
| `tavu_businesscalendar` (lookup) | Working hours, reusing `tavu_businesscalendar` |
| `tavu_outofhoursmessage`, `tavu_greeting` | Configurable texts |
| `tavu_nextstepmessage`, `tavu_bookingurl` | The concrete commitment at handoff |
| `tavu_turnlimit`, `tavu_abandonafterhours` | Never loop; close stale conversations |
| `tavu_approvaltimeoutminutes`, `tavu_holdingmessage` | Propose mode: how long a draft may wait before the holding message goes out, and its text |
| `tavu_handoffuser` (lookup user), `tavu_handoffteam` (lookup team) | Who is notified (two lookups instead of one polymorphic owner, simpler for forms, flows and the Web API) |
| `tavu_tokenbudget` | Per conversation |
| `tavu_usecompanyprofile`, `tavu_useworkinghours`, `tavu_useknowledgearticles` (yes or no) | Which published sources the agent may answer from (all on by default) |

Quick Test: configuration over code (everything a firm changes lives here). **Passes.**

### 5.3 `tavu_qualificationcriterion` (new)

The questions, as data. Copies the configuration surface of Microsoft's Sales Qualification Agent (qualification criteria), simplified.

| Column | Purpose |
| :--- | :--- |
| `tavu_agent` (lookup), `tavu_order` | Which agent, in what order |
| `tavu_name` | "Project location", "Timeline", "Budget range" |
| `tavu_purpose` | What the agent needs to learn, in plain words (the model writes the actual question). Named `purpose`, not `intent`, so it is not confused with the buying intent on the conversation |
| `tavu_required` (yes or no), `tavu_weight` | Required to qualify; contribution to the score |
| `tavu_disqualifyingrule` | Plain-words rule, for example "outside Houston metro" |
| `tavu_leadfield` (optional) | Lead column to fill on handoff |

Quick Test: the heart of "qualification depends on each business" (Gustavo, 2026-10-05). **Passes.**

### 5.4 Changes to existing tables

- `tavu_companyprofile` (Decision 47: extend, do not create): `tavu_servicesdescription` (what the firm sells), `tavu_targetcustomer` (who it serves, service area), `tavu_tone`, `tavu_neversay` (hard "do not promise" list in plain words). Used by every agent.
- `tavu_knowledgearticle`: the table exists but today holds only a name. Minimal build-out so the agent (and later the service side) can use it: `tavu_body` (multiline text), `tavu_audience` (Public, Internal), and the Published state. The agent only reads Public and Published articles, so internal notes can never reach a prospect. Quick Test: pain = repeated questions and context loss; reuses an existing table instead of a new FAQ field; firm-configurable. **Passes.**
- `tavu_leadsource` choice: add **WhatsApp** and **Email**.
- `contact`: `tavu_whatsappid` to match returning people by their WhatsApp identity, since Meta's usernames mean the phone number can be missing.
- `tavu_systemsettings`: `tavu_conversationretentiondays` (empty = keep), used by a daily cleanup, consistent with the privacy policy (data kept only as long as needed).

## 6. Channel adapter contract

```
Inbound  { tenant, channel, channelContactId, phone?, email?, displayName, text, media[], receivedAt, providerMessageId }
Outbound { conversationId, text, template?, buttons? }
Capabilities { freeTextAllowed(now), maxLength, supportsButtons, isSynchronous }
```

- **WhatsApp:** freeTextAllowed only within 24 hours of the last inbound; outside it the agent does not write (v1); templates come later. Max length per Meta. **Contact identity (verified 2026-10-06 with a real message):** the payload carries the phone in `messages[].from` **and** the WhatsApp user id in `messages[].from_user_id` and `contacts[].user_id`. The adapter uses the user id as the primary identity (`tavu_channelcontactid` on the conversation, `tavu_whatsappid` on the contact), because it survives when a person hides the phone behind a username, and keeps the phone as the secondary match against `mobilephone` / `tavu_mobilephone`.
- **Web form:** asynchronous; the reply goes by email to the address in the form; Lead Triage already runs on the lead the form creates.
- **Email:** reuses the Graph mailbox intake already in the gateway, pointed at a sales mailbox.

### 6.1 Named actions (Decision 52, constraints 2 and 3)

Every effect the agent can cause goes through one named action, implemented once in the generic layer with its own whitelist check and a declared effect class. Agent types return the actions to run; they never call Dataverse or a channel directly. These actions are the first entries of the future tool registry.

| Action | Effect class | Whitelist check inside the action |
| :--- | :--- | :--- |
| `SendReply` | ExternalCommunication | Channel capabilities (WhatsApp 24-hour window, max length); hard limits (only the booking URL as a link, no amounts that look like prices); mode (propose stores the reply as pending and sets Awaiting Approval instead of sending). A failed check turns into `HandToHuman`. Addressing: the phone of the current inbound message, else `tavu_phone`, else the channel contact id. Transient send failures are retried from the `agent-outbound` Azure Storage queue (the gateway's storage account, same as `agent-inbound`; not Dataverse, to keep database capacity and polling out of it); permanent failures write a `system` entry and run `NotifyOwner`. |
| `CreateOrUpdateLead` | InternalWrite | Only the fields mapped from criteria (`tavu_leadfield`), the score and the source; never other lead columns. |
| `NotifyOwner` | InternalWrite | Recipients only the handoff user or team of the agent, or the lead owner. |
| `HandToHuman` | InternalWrite | Sets the conversation to With Human; the agent stops replying. Distinct from notifying: on qualification the owner is notified without silencing the agent. |
| `CloseConversation` | InternalWrite | Only the allowed outcomes: Qualified, Disqualified, Closed, with an outcome reason. |
| `ReadPublishedSources` | Read | Only the sources whose switch is on for the agent; knowledge articles only when Public and Published. |

The effect class (Read, InternalWrite, ExternalCommunication) is not used by the runtime in v1. It is the basis for autonomy per tool and for the guardrail that keeps user-built agents away from external communication (Decision 52, the ladder).

## 7. Safety and cost

- The person's text is data, never instructions: the system prompt states it, and the runtime checks the reply (section 4, step 6) so a prompt injection cannot make the agent promise anything.
- Logs keep the masked sender only; full text lives in Dataverse under the firm's security.
- One structured model call per turn when possible; per-conversation token budget; model chosen per task in `tavu_aitaskconfiguration`. Meta charges service replies per message from October 2026 (verify on Meta's pricing page), so the turn limit is also a cost control.

## 8. Build plan (for Claude Code briefs)

0. **Prove a real inbound message** from a phone reaches the webhook (subscribe the app to the WhatsApp Business Account if needed). Nothing is built before this passes. **Done 2026-10-06:** the app was not subscribed to the WABA (only Meta's own test app was); after `POST /{waba-id}/subscribed_apps` a message typed on Gustavo's phone reached the gateway through the persistent dev tunnel `opentavu-gw`. Setup note for every firm (model A): subscribing the app to the WABA is a required step and belongs in the installation guide.
1. **Schema** in the dev environment: the three tables, the profile fields, the knowledge article build-out, choices and settings; sync to `src/Solution`. **Done 2026-10-06** (commit `6a673ec`): applied in dev, synced to `src/Solution` and described in `architecture.md`. Two platform constraints found: whole-number columns cannot carry a default, so the runtime treats an empty `tavu_turnlimit`, `tavu_abandonafterhours` and `tavu_approvaltimeoutminutes` as 12, 48 and 5; and the quick find query of the new tables cannot be changed through the Web API, so the Conversation quick find was set in the maker portal.
2. **Gateway plumbing (generic layer only):** queue, queue-triggered processor calling a generic `AgentRuntime`, idempotency by provider message id, conversation resolution, `TranscriptStore` writing the uniform entry format from the first inbound message, the named-action catalog with effect classes, and the WhatsApp send API implemented only as the adapter's outbound path and reachable only through `SendReply`. No agent type yet: the step is proven by sending a fixed text through `SendReply`. **Done 2026-10-08** (gateway commit `b8a5e29`): a message from Gustavo's phone created the conversation in dev, the second one went to the same conversation, the fixed reply arrived through `SendReply` (Meta `sent` and `delivered`), a replayed payload was skipped by the idempotency table, and queue processing was serialized. Findings carried to step 3: (a) WhatsApp test numbers accept only phone numbers, so `SendReply` addresses by the phone of the current inbound message and falls back to the user id; replies sent outside an inbound turn (holding message) have no phone because the conversation does not store it, to be solved in step 3; (b) when a send fails after the inbound entry is recorded, the reply is not retried, which conflicts with "nobody goes unanswered" and must be handled in step 3; (c) the link-filter test of `SendReply` (a reply with a link that is not the booking URL is blocked) is deferred to step 3; (d) the booking URL is appended by the agent to the next-step message, not written into the message text.
3. **Runtime:** schema addition `tavu_phone`; generic guards; the model call with token budget and the fixed safety flags (`optOut`, `wantsHuman`) handled by the runtime; the `IAgentType` contract resolved from `tavu_agenttype`; `LeadQualificationType` with the decision rules and its part of the prompt and schema; the full hard-limit check inside `SendReply` and the booking URL appended by the runtime; the `agent-outbound` retry queue; propose mode with the holding message and the abandon timer; `HandToHuman` and `CloseConversation` implemented. `NotifyOwner` writes a `system` entry in step 3 and gets its real channel (email or Teams) in step 5. OpenTavu's own configuration (company profile and criteria) is entered in dev for the tests.
4. **Conversation UI:** the PCF transcript viewer, and the Approve, Edit, Reject buttons for firms that turn on propose mode (pattern of Decisions 39 and 40).
5. **Handoff:** lead create or update, owner notification, next-step message.
6. **Calibration, tests and dogfooding:** OpenTavu is the first firm, qualifying the leads of the YouTube channel.
   - **Pre-launch calibration:** about 15 scripted conversations played by Gustavo from his phone before the number is public: price question, "I want to talk to a person", out of hours, English, a tire kicker, outside the service area, a prompt-injection attempt ("ignore your instructions"), someone who never answers the questions. Every failure is fixed in configuration (profile, criteria, never-say list), not in code. The set is kept as the agent's **regression test set** and re-run after every change.
   - **First live week in propose mode,** with fast approval from the phone and the holding-message fallback.
   - **Then act mode,** with a daily end-of-day review of every conversation during the first live weeks, and the mode switch as a kill switch (back to propose in one click, no code).
7. **Definition of done:** sync, master Decision entry and module status, README, VISION and site.

## 9. Open questions

1. Resolved 2026-10-06: score by weights (deterministic) plus a separate intent field (High, Medium, Low).
2. Resolved 2026-10-06: act mode is the default; propose mode is optional per firm, and when on, the lead owner or the owner's team approves.
3. Resolved 2026-10-06: transcript as a File column plus a PCF viewer.
4. Resolved 2026-10-06: the agent answers from the company profile, the working hours of the business calendar and public published knowledge articles, each switchable per agent; anything else becomes an offer to talk to a human.
5. Resolved 2026-10-06 (Gustavo): OpenTavu's own number goes live after a pre-launch calibration (section 8, step 6) and then runs **one week in propose mode**, with Gustavo approving quickly, before switching to act mode.
6. Verify on Microsoft Learn the single-source claims listed in the Microsoft triangulation before quoting them publicly.

## Document control

| Version | Date | Changes |
| :--- | :--- | :--- |
| 0.1 | 2026-10-06 | First draft from Decisions 47 and 51, the Microsoft agentic-architecture triangulation and the customer-voice triangulation. Choices made with Gustavo: configured steps with the LLM inside; one record per conversation with the messages inside it (Dynamics and Salesforce pattern). |
| 0.2 | 2026-10-06 | Gustavo's answers: act mode by default with the runtime check as the safeguard (propose optional); fit score by weights plus a separate intent field; answers only from published sources (profile, working hours, public knowledge articles), otherwise offer a human; transcript as a File column with a PCF viewer. Found that `tavu_knowledgearticle` holds only a name and added its minimal build-out. |
| 0.3 | 2026-10-06 | OpenTavu's own number: pre-launch calibration with a 15-conversation regression set, one live week in propose mode with fast phone approval and a holding-message fallback after a timeout, then act mode with daily review and a kill switch. |
| 0.4 | 2026-10-06 | Step 0 done (real message received after subscribing the app to the WABA). Verified that real payloads carry both the phone and the WhatsApp user id; the user id becomes the primary contact identity. |
| 0.5 | 2026-10-06 | Review of the schema change list: handoff as two lookups (user, team); criterion column renamed `tavu_purpose` to avoid two different `tavu_intent` columns; header version fixed. |
| 0.6 | 2026-10-06 | Step 1 done: schema applied in dev and synced (commit `6a673ec`); whole-number defaults handled by the runtime; Conversation quick find set in the maker portal. |
| 0.7 | 2026-10-07 | Decision 52 applied (no schema change, no build reorder): runtime split into a generic agent layer and agent types behind `IAgentType`; each turn step marked Generic or Type; `optOut` and `wantsHuman` are a fixed part of the output schema handled by the runtime and cannot be omitted or overridden by a type; every effect goes through a named action with its whitelist check and effect class (new section 6.1, including `HandToHuman` as its own action, distinct from `NotifyOwner`); the hard-limit check lives inside `SendReply`, so out-of-hours and holding messages pass the same filter; uniform transcript entry format; build steps 2 and 3 reworded. Approved by Gustavo in Cowork. |
| 0.8 | 2026-10-08 | Step 2 done (gateway commit `b8a5e29`), with the findings carried to step 3: phone addressing for test numbers, no phone for out-of-turn replies, no retry of a failed reply, link-filter test deferred, booking URL appended by the agent. |
| 0.9 | 2026-10-08 | Decisions for step 3 (Gustavo): new column `tavu_phone` on the conversation for replies outside an inbound turn; transient send failures retried from an `agent-outbound` Azure Storage queue, permanent ones become a `system` entry plus `NotifyOwner`; the hard-limit check is deterministic only (length, links, price-like amounts) and the never-say list is model guidance; the runtime appends the booking URL; OpenTavu's own criteria and company profile defined in `agent-briefs/opentavu-agent-configuration.md`. |
