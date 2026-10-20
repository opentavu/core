# Lead Qualification Agent: design

**Status:** Draft v0.11 for review · **Date:** 2026-10-20 · **Owner:** Gustavo González Villani · **Decisions:** 51 (this agent first, channel-agnostic, WhatsApp first), 52 (agent type is code, agent instance is data; generic agent layer), 47 (agentic layer, autonomy levels, principles)

**Inputs:** `1-Producto/Research/Agentic_Architecture/agentic-architecture-triangulation.md` (how Microsoft built its agent layer), `2-Negocio/Go_To_Market/expanded-avatar-voice/expanded-avatar-voice-triangulation.md` (what owners accept and reject from an AI that answers for them), the WhatsApp webhook already in the gateway (`Functions/WhatsAppWebhookFunction.cs`, commit `50c6681`).

---

## 1. What it is

An L2 agent that answers every inbound lead within seconds, on the channel the person used, asks the questions the firm configured, decides whether the lead is worth a person's time, and hands it to a human with a concrete next step (a meeting link or a callback time). It never sells, never quotes, never invents.

**In scope (v1):** WhatsApp (first adapter), then the web form and inbound email adapters; one agent per firm; propose mode and act mode; handoff to the lead owner; the conversation stored in Dataverse.

**Out of scope (v1):** outbound campaigns (TCP rules), voice, Instagram and Facebook DMs, a web chat widget (next adapter after email), multi-agent handoffs (A2A).

**Added after v0.9:** booking the first meeting in the chat through Microsoft Bookings (optional per agent; without it the agent sends the firm's booking link), section 4, "Handoff".

**Current state (2026-10-16):** steps 0 to 3 of the build plan are done and running on OpenTavu's own number in dev, including in-chat booking; step 5 is done except the real owner notification channel; step 4 (the conversation UI) is not started. Section 8 has the details.

**Added in v0.11 (2026-10-20):** the Decision 56 and 57 redesign on the gateway branch `d57-redesign` (not merged or deployed yet): booking goes through a neutral scheduling provider contract with Microsoft Bookings as its first adapter, and after qualification the model chooses one allowed action per turn, which the runtime checks and carries out, instead of fixed booking and follow-up stages (section 4, "After qualifying"). Measured with the conversation simulator: the whole set passes on both fixtures (section 8).

## 2. Design principles

1. **Configured steps, LLM inside.** Qualification is a known sequence, so the steps and criteria are data and the runtime drives them. The model understands the message, extracts values, and writes the reply. It does not decide the flow on its own. (Microsoft triangulation, conclusion 3.) After qualification (Decision 57, 2026-10-20) the model chooses the next action, but only among the actions the runtime allows at that moment, with a confidence the runtime checks; the runtime still carries out every action and writes every text with a date or a time.
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
| Closed (inactive) | Abandoned (no reply within the configured time), opted out, or off-topic (three off-topic messages in a row); the outcome reason says which |

A conversation closed as off-topic, opted out or Disqualified is not permanent: for the agent's `tavu_reopenafterhours` (empty = 24 hours, 0 = no window), counted from its close, new messages from the person go to it and are only recorded; after that, the next message starts a new conversation. A Qualified conversation never reopens; an abandoned one lets the next message start a new conversation at once.

**One turn (inbound message).** Each step is marked **[Generic]** (generic agent layer, the same for every agent type) or **[Type]** (`LeadQualificationType`).

1. **[Generic] Adapter and conversation:** resolve the conversation (the latest one for this channel identity, or a new one when that one was abandoned or its reopen window has passed), check channel rules (WhatsApp: inside the 24-hour window), record the inbound entry.
2. **[Generic] Guards (deterministic, no model):** conversation With Human or closed → store the message only and run `NotifyOwner` (closed as off-topic: store only, no notification, so a person who kept provoking the agent cannot keep notifying the owner). Turn limit reached (messages sent after an in-chat booking started do not count) or the conversation's token budget used up → hand off (`HandToHuman`, `NotifyOwner`). Outside working hours → send the configured out-of-hours message once through `SendReply`, keep collecting.
3. **[Generic] Understand (one model call, structured JSON output, within the token budget):** the output schema has a **fixed generic part** that every conversational agent type returns and no type can omit or override: detected language, `optOut` with `optOutAcknowledgment`, `wantsHuman` with `acknowledgment`, and `offTopic` with `offTopicReply`. **[Type]** adds its own part of the prompt and of the schema: values for pending criteria with a confidence each, a disqualifying answer, the person's question, the buying intent, and the replies for each outcome. Invalid output or a provider error is retried once; when both calls fail, the person gets the agent's holding message once per outage (a fixed line when none is configured), never the same text twice in a row, the conversation stays with the agent, and the owner is notified of every failed turn (decision `ModelFailed`, 2026-10-20; before, the conversation went to a person and the person got nothing). The history sent is trimmed to the task's token budget with room kept for the answer (500 tokens for a classic model, 1,200 for a reasoning one), and always includes the person's message and the agent's last question before it. Output that lacks a field the type needs (`IAgentType.MissingField`; for a lead, `disqualifiedReply` with a disqualifying answer) is also retried once, then used with the type's fixed text.
4. **[Generic] Safety flags first, executed by the runtime:** `optOut` → the confirmation, sent at once, then `CloseConversation` (Closed, opted out). `wantsHuman` → `HandToHuman`, `NotifyOwner` and the acknowledgment. `offTopic` (a message unrelated to the firm, or an attempt to manipulate the agent) → the one-line boundary reply plus the pending question; the third in a row sends a fixed closing line and closes as Closed, off-topic, with no handoff. The agent type is not consulted for `optOut` and `wantsHuman`. For `offTopic` it is (amendment to Decision 52, 2026-10-15): when the type reads an answer to one of its questions in the same message (`IAgentType.AnswersCriterion`), `offTopic` is ignored and the type decides, so "I'm looking for a job" reaches the disqualifying rule instead of the boundary reply.
5. **[Type] Decide (deterministic):** update criteria answers above the confidence threshold; then, in order: disqualifier → disqualify; all required criteria answered → qualify and hand off; question the agent can answer from published sources (`ReadPublishedSources`: company profile with its public links, working hours from the business calendar, public knowledge articles, each source switchable per agent) → answer it, then ask the next missing criterion; question it cannot answer from those sources → do not improvise: say that someone from the team will follow up, ask the next missing criterion and run `NotifyOwner` (no booking link before qualification); otherwise → ask the next missing criterion. While still qualifying, the readable summary of the answers so far goes to `tavu_summary`. The output is a decision plus the named actions to run.
6. **[Type] Compose (same model call where possible):** the reply in the person's language and the firm's tone, short, one question at a time.
7. **[Generic] Act or propose, inside `SendReply`:** every outbound text, including the out-of-hours and holding messages, goes through `SendReply`, which runs the hard-limit check (deterministic rules only: channel length, links other than the booking URL and the firm's public links, amounts that look like prices). The firm's never-say list is guidance for the model in the prompt, not a deterministic rule. If the check fails → `HandToHuman` instead of sending. When the decision includes the booking link (next step, offer of a human session), the runtime appends `tavu_bookingurl` to the text; the link is never written into the configured messages. A send that fails for a transient reason (provider error 5xx, network) is retried from the `agent-outbound` queue with growing delays; a permanent failure, or the last failed retry, writes a `system` entry and runs `NotifyOwner`, so a person answers instead. Act mode (default) → send through the adapter. Propose mode (optional per firm) → status Awaiting Approval, reply stored as pending for the lead owner or the owner's team. In propose mode two things keep the promise of a fast answer: (a) the approver is notified on the phone at once (Teams mobile notification or an email with a direct link, configurable) and can approve, edit or reject from the model-driven app on mobile; (b) if nobody approves within `tavu_approvaltimeoutminutes` (default 5), the agent sends the configured holding message once through `SendReply` ("Thanks for writing, I will get back to you in a few minutes with the details") so the person is never left without an answer, and the drafted reply keeps waiting.
8. **[Generic] Record:** append the outbound entry with its trace fields (section 5.1), update counters and timings, tokens used.

**Handoff (a sequence of named actions):** `NotifyOwner` with a summary; `CreateOrUpdateLead` and link it to the conversation **[Type decides, action is generic]**; then either `SendReply` with the next step (the model's qualified reply, else the configured `tavu_nextstepmessage`, with the booking link appended) and `CloseConversation` as Qualified, or, with in-chat booking on, the booking below. On qualification the owner is notified **without** `HandToHuman`, so the agent is not silenced; `HandToHuman` is only for the person asking for a human, the turn limit, the token budget or a failed check. Lead Triage then runs as it does for every new lead (no source gate), matching against existing contacts.

**CreateOrUpdateLead.** Runs before the booking starts, so the lead exists even if the booking does not finish. It updates the conversation's linked lead, else an open lead with the same email, else one with the same mobile phone (compared in E.164), else creates one whose id is derived from the conversation id, so a retried turn cannot create a second lead. It writes only an allowlist: name (one word goes to the first name, two words are split), mobile phone, email, company name and subject through criteria with that `tavu_leadfield`, source WhatsApp, language, and the summary in `tavu_sourcedetails` (one block per conversation, newest last; at 2000 characters the oldest agent blocks are dropped first and another source's text is kept). It never writes the AI and triage columns, and on an existing lead an empty value never replaces a filled one. The owner of a new lead is the agent's handoff user, else its team. The same summary goes to the conversation's `tavu_summary`. A disqualified conversation creates a lead only when the agent's `tavu_createleadfordisqualified` is Yes; opted-out, off-topic, abandoned-before-qualifying and With Human conversations never do. A Dataverse error never stops the conversation: it is traced and the owner is notified with the summary.

**In-chat booking (optional).** With the agent's `tavu_bookingsbusiness` and `tavu_bookingsserviceid` set, a qualified person books the first meeting in the chat. Microsoft Bookings is the source of truth for the service (duration, slot interval, lead time, advance window, hours, time zone) and for staff availability. The runtime (`ReadAvailability`) offers 3 times in the person's zone (from the phone's country code, or the city when the country has several zones); the texts with dates and times are the runtime's own, never the model's, which only returns the index of the chosen time. The email is asked only when no criterion captured it. `BookMeeting` re-checks the time, creates the appointment as a Teams meeting with the qualification summary in its internal notes, stores its id in `tavu_appointmentid`, confirms and closes as Qualified, "meeting booked". Two "none" answers, no free time or a Bookings failure send the booking link instead and close as Qualified, "qualified, booking not completed", with the owner notified. The booking state lives in `tavu_booking`. The summary's booking line shows the meeting in the business's and the person's local time.

**Scheduling provider (Decision 56, 2026-10-20).** The booking code talks only to a neutral contract (`ISchedulingProvider`: service rules, availability, book, find, get, move, cancel) with four capabilities (`BookInChat`, `Move`, `Cancel`, `OnlineMeeting`); Microsoft Bookings is its first adapter, picked by key through a registry. Until the schema part of `scheduling-provider.md` lands, `tavu_bookingsbusiness` and `tavu_bookingsserviceid` map to the Bookings adapter. The booking state records its provider (a state written before reads as Microsoft Bookings), so a meeting is moved or cancelled through the provider that booked it. A missing capability changes the allowed actions: no `BookInChat` sends the booking link; no `Move` or `Cancel` passes the request to the team.

**After qualifying: one action per turn (Decision 57, 2026-10-20).** From the turn after the first offer of times, while booking and during the follow-up window, each turn's model output is one action from the allowed ones plus a confidence (component 1), and text only for the two actions that need it (component 2): Answer (from the published sources and the meeting's facts) and Clarify (one short question). The other actions send the runtime's text and ignore any model text: Greet, Acknowledge, OfferTimes (a new meeting or new times for the booked one), ChooseSlot, DeclineTimes, GiveEmail, GiveCity, Confirm and Deny (the one pending move or cancellation), KeepMeeting, CancelMeeting, StateMeeting (when it is and how to join) and PassToTeam. The runtime guards the choice:

- Allowed actions come from what exists (a meeting, times on offer, a pending change, a question the system asked) and the provider's capabilities, never from a stage, so keeping or cancelling works while new times are on offer. A refused or unknown action becomes Clarify.
- An action that changes state (choosing a time, confirming, keeping, cancelling, starting a move) needs the task's confidence threshold; below it the agent asks. Opt-out is a safety flag handled before any action and is never thresholded (Decision 53).
- Word checks (a "no", "other times", "keep it") may only downgrade an action to Clarify, never force one.
- The model's text may state no date and no time, and a duration only when it is the meeting's own; a failing or empty text is retried once, then the runtime's text is used, so a reply is never empty.
- The model is given the meeting's facts (date and time, duration, online or not, status), the question the system asked, and the purpose of each criterion, so "how long is the call?" and "why do you ask?" are answered instead of passed to the team.
- After a first pass to the team, each later one gets a short new line, never silence; an owner email held back by the 30-minute limit is folded into the next one sent.
- A confirmed cancellation clears `tavu_appointmentid` (the old id stays in the state's history), so booking again creates a new appointment, and the conversation stays open for at least `tavu_abandonafterhours` after the person's last message, whatever the reopen window.
- Runtime texts use the conversation's `tavu_language` unless the message is clearly in another language.

Qualification turns (criteria, score, disqualify) keep the configured steps of principle 1.

**Disqualify.** `CreateOrUpdateLead` (it only writes when the agent's column is Yes), the model's `disqualifiedReply` (never the next-question reply; when it is still missing after the retry, a fixed closing line in the conversation's language), then `CloseConversation` as Disqualified.

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
| `tavu_summary` | Multiline text (4000) | The readable summary a business user reads on the form: answers so far, score, intent, outcome, booking result. The same text the lead gets in `tavu_sourcedetails`. Added 2026-10-15, because the JSON columns are not readable; those moved to a Technical tab |
| `tavu_booking` | Multiline text (JSON) | State of an in-chat booking (stage, offered times, person's zone and place, business zone). Added 2026-10-13 |
| `tavu_appointmentid` | Text | The Bookings appointment the agent created. Added 2026-10-13 |

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
| `tavu_reopenafterhours` | How long after an off-topic, opted-out or disqualified close the person's messages are only recorded (empty = 24, 0 = no window). Added 2026-10-16 |
| `tavu_bookingsbusiness`, `tavu_bookingsserviceid` | In-chat booking: the Microsoft Bookings business and service; booking in the chat is on only when both are set. Added 2026-10-13 |
| `tavu_createleadfordisqualified` (yes or no, default no) | A disqualified conversation also creates or updates a lead; a qualified one always does. Added 2026-10-14 |
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

- `tavu_companyprofile` (Decision 47: extend, do not create): `tavu_servicesdescription` (what the firm sells), `tavu_targetcustomer` (who it serves, service area), `tavu_tone`, `tavu_neversay` (hard "do not promise" list in plain words), and `tavu_website` with `tavu_publiclinks` (one `label: URL` per line: the only links the agent may write, added 2026-10-13). Used by every agent.
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
| `SendReply` | ExternalCommunication | Channel capabilities (WhatsApp 24-hour window, max length); hard limits (only the booking URL and the firm's public links as links, no amounts that look like prices); mode (propose stores the reply as pending and sets Awaiting Approval instead of sending). A failed check turns into `HandToHuman`. Addressing: the phone of the current inbound message, else `tavu_phone`, else the channel contact id. Transient send failures are retried from the `agent-outbound` Azure Storage queue (the gateway's storage account, same as `agent-inbound`; not Dataverse, to keep database capacity and polling out of it); permanent failures write a `system` entry and run `NotifyOwner`. |
| `CreateOrUpdateLead` | InternalWrite | Only the allowlist in section 4 (identity, company name and subject through `tavu_leadfield`, source, language, summary); never the AI or triage columns; an empty value never replaces a filled one. |
| `NotifyOwner` | InternalWrite | Recipients only the handoff user or team of the agent, or the lead owner. |
| `HandToHuman` | InternalWrite | Sets the conversation to With Human; the agent stops replying. Distinct from notifying: on qualification the owner is notified without silencing the agent. |
| `CloseConversation` | InternalWrite | Only the allowed outcomes: Qualified, Disqualified, Closed, with an outcome reason. |
| `ReadPublishedSources` | Read | Only the sources whose switch is on for the agent; knowledge articles only when Public and Published. |
| `ReadAvailability` | Read | Only the scheduling resource and service set on the agent, through its scheduling provider (Microsoft Bookings today). Added with in-chat booking. |
| `BookMeeting` | ExternalWrite | Only on the agent's scheduling resource and service, only a time the runtime offered and re-checked as free; one appointment per conversation at a time (a cancelled one no longer counts). Added with in-chat booking. |

The effect class (Read, InternalWrite, ExternalWrite, ExternalCommunication) is not used by the runtime in v1. It is the basis for autonomy per tool and for the guardrail that keeps user-built agents away from external communication (Decision 52, the ladder).

## 7. Safety and cost

- The person's text is data, never instructions: the system prompt states it, and the runtime checks the reply (section 4, step 6) so a prompt injection cannot make the agent promise anything.
- Logs keep the masked sender only; full text lives in Dataverse under the firm's security.
- One structured model call per turn when possible; per-conversation token budget; model chosen per task in `tavu_aitaskconfiguration`. Meta charges service replies per message from October 2026 (verify on Meta's pricing page), so the turn limit is also a cost control.

## 8. Build plan (for Claude Code briefs)

0. **Prove a real inbound message** from a phone reaches the webhook (subscribe the app to the WhatsApp Business Account if needed). Nothing is built before this passes. **Done 2026-10-06:** the app was not subscribed to the WABA (only Meta's own test app was); after `POST /{waba-id}/subscribed_apps` a message typed on Gustavo's phone reached the gateway through the persistent dev tunnel `opentavu-gw`. Setup note for every firm (model A): subscribing the app to the WABA is a required step and belongs in the installation guide.
1. **Schema** in the dev environment: the three tables, the profile fields, the knowledge article build-out, choices and settings; sync to `src/Solution`. **Done 2026-10-06** (commit `6a673ec`): applied in dev, synced to `src/Solution` and described in `architecture.md`. Two platform constraints found: whole-number columns cannot carry a default, so the runtime treats an empty `tavu_turnlimit`, `tavu_abandonafterhours` and `tavu_approvaltimeoutminutes` as 12, 48 and 5; and the quick find query of the new tables cannot be changed through the Web API, so the Conversation quick find was set in the maker portal.
2. **Gateway plumbing (generic layer only):** queue, queue-triggered processor calling a generic `AgentRuntime`, idempotency by provider message id, conversation resolution, `TranscriptStore` writing the uniform entry format from the first inbound message, the named-action catalog with effect classes, and the WhatsApp send API implemented only as the adapter's outbound path and reachable only through `SendReply`. No agent type yet: the step is proven by sending a fixed text through `SendReply`. **Done 2026-10-08** (gateway commit `b8a5e29`): a message from Gustavo's phone created the conversation in dev, the second one went to the same conversation, the fixed reply arrived through `SendReply` (Meta `sent` and `delivered`), a replayed payload was skipped by the idempotency table, and queue processing was serialized. Findings carried to step 3: (a) WhatsApp test numbers accept only phone numbers, so `SendReply` addresses by the phone of the current inbound message and falls back to the user id; replies sent outside an inbound turn (holding message) have no phone because the conversation does not store it, to be solved in step 3; (b) when a send fails after the inbound entry is recorded, the reply is not retried, which conflicts with "nobody goes unanswered" and must be handled in step 3; (c) the link-filter test of `SendReply` (a reply with a link that is not the booking URL is blocked) is deferred to step 3; (d) the booking URL is appended by the agent to the next-step message, not written into the message text.
3. **Runtime:** schema addition `tavu_phone`; generic guards; the model call with token budget and the fixed safety flags (`optOut`, `wantsHuman`) handled by the runtime; the `IAgentType` contract resolved from `tavu_agenttype`; `LeadQualificationType` with the decision rules and its part of the prompt and schema; the full hard-limit check inside `SendReply` and the booking URL appended by the runtime; the `agent-outbound` retry queue; propose mode with the holding message and the abandon timer; `HandToHuman` and `CloseConversation` implemented. `NotifyOwner` writes a `system` entry in step 3 and gets its real channel (email or Teams) in step 5. OpenTavu's own configuration (company profile and criteria) is entered in dev for the tests. **Done 2026-10-13** (gateway commit `67a2088`, core commits `42a932e` and `d5f183d`), together with in-chat booking through Microsoft Bookings and the off-topic flag. Finding: creating an appointment on Graph v1.0 returns HTTP 500 for at least one Bookings business while beta creates it, so creates use beta by default (`Bookings__ApiVersion`).
4. **Conversation UI:** the PCF transcript viewer, and the Approve, Edit, Reject buttons for firms that turn on propose mode (pattern of Decisions 39 and 40). **Not started.** Until then the form shows `tavu_summary`, and the transcript file is read through the API.
5. **Handoff:** lead create or update, owner notification, next-step message. **Done 2026-10-16 except the owner notification channel** (gateway commit `3df2945`, core commit `aaa22b8`): `CreateOrUpdateLead` as described in section 4, the conversation summary, the reopen window, and the fixes from the phone tests of 2026-10-15 and 2026-10-16 (an answer to the agent's question is never off-topic, the closing lines, the Disqualify close, Source Details limits, the booking line in local times). `NotifyOwner` is still a `system` entry plus a warning log.
6. **Calibration, tests and dogfooding:** OpenTavu is the first firm, qualifying the leads of the YouTube channel.
   - **Pre-launch calibration:** about 15 scripted conversations played by Gustavo from his phone before the number is public: price question, "I want to talk to a person", out of hours, English, a tire kicker, outside the service area, a prompt-injection attempt ("ignore your instructions"), someone who never answers the questions. Every failure is fixed in configuration (profile, criteria, never-say list), not in code. The set is kept as the agent's **regression test set** and re-run after every change.
   - **First live week in propose mode,** with fast approval from the phone and the holding-message fallback.
   - **Then act mode,** with a daily end-of-day review of every conversation during the first live weeks, and the mode switch as a kill switch (back to propose in one click, no code).
   - **Regression set (2026-10-17 on):** the conversation simulator (`tools/ConversationSim` in the gateway) replays 21 scripted conversations (a to u) through the real runtime and model, on a neutral fixture and on a sanitized snapshot of the dev agent. **Decision 56 and 57 redesign, 2026-10-20** (gateway branch `d57-redesign`, commits `dd52e59`, `60fa36c`, `41c1ee5`, `12a680f`; not merged): 63/63 on each fixture on gpt-6-luna with `--repeat 3`, and r 3/3 with a task budget of 4000 on both, against 39/60 on the neutral fixture before (f, g, h, j, q, s and t failed). Details in the gateway's `tools/ConversationSim/BASELINE.md`. Next: Gustavo's phone test.
7. **Definition of done:** sync, master Decision entry and module status, README, VISION and site.

## 9. Open questions

1. Resolved 2026-10-06: score by weights (deterministic) plus a separate intent field (High, Medium, Low).
2. Resolved 2026-10-06: act mode is the default; propose mode is optional per firm, and when on, the lead owner or the owner's team approves.
3. Resolved 2026-10-06: transcript as a File column plus a PCF viewer.
4. Resolved 2026-10-06: the agent answers from the company profile, the working hours of the business calendar and public published knowledge articles, each switchable per agent; anything else becomes an offer to talk to a human.
5. Resolved 2026-10-06 (Gustavo): OpenTavu's own number goes live after a pre-launch calibration (section 8, step 6) and then runs **one week in propose mode**, with Gustavo approving quickly, before switching to act mode.
6. Verify on Microsoft Learn the single-source claims listed in the Microsoft triangulation before quoting them publicly.
7. Open (design next): returning and qualified people. A qualified conversation's window would last until the later of `tavu_reopenafterhours` and the end of the booked meeting; inside it the agent could reschedule or cancel the appointment it created and answer questions from published sources; after it, a new conversation would recognize the person by lead or contact and ask only what is new. An existing customer would be routed by intent (sales or support), until a Service Agent shares the number (Decision 52).
8. Open: the deterministic summary text (`tavu_summary`, the Source Details block, the Bookings notes) is in English whatever the conversation's language; to be localized from one place.
9. Open: the conversation's owner is the gateway's app user, not the handoff user.
10. Open: every disqualify close should thank the person and say goodbye, also when the model writes it. (Scenario l passes on both fixtures as of 2026-10-20; the rule is not changed by the redesign.)
12. Open (proposal of 2026-10-20, Gustavo decides): the runtime texts and word checks are code today, Spanish and English only. Proposed: word checks and the date grammar as language packs shipped with the gateway (one file per language, no Dataverse table), then the firm's voice texts as agent fields in the "es: / en:" format, with a default holding message seeded when an agent is created. Details in `agent-briefs/decision57-redesign-progress.md`, finding 3.
11. Open: Graph v1.0 appointment creation (see step 3); switch `Bookings__ApiVersion` back to v1.0 once Microsoft fixes it.

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
| 0.10 | 2026-10-16 | Current state after steps 3 and 5: in-chat booking through Microsoft Bookings (out of scope removed; `ReadAvailability` and `BookMeeting` actions; `tavu_booking`, `tavu_appointmentid`, `tavu_bookingsbusiness`, `tavu_bookingsserviceid`); public links on the company profile; `offTopic` in the generic output schema, with the amendment to Decision 52 (the agent type is consulted on off-topic messages only; optOut and wantsHuman still win); CreateOrUpdateLead and `tavu_createleadfordisqualified`; `tavu_summary`; the reopen window (`tavu_reopenafterhours`); the retry for a missing field (`IAgentType.MissingField`); an unanswerable question now promises a follow-up instead of the booking link. Build steps 3 and 5 marked done (owner notification channel pending), step 4 not started; open questions 7 to 11 added. |
| 0.11 | 2026-10-20 | The Decision 56 and 57 redesign (gateway branch `d57-redesign`, not merged): the scheduling provider contract with Microsoft Bookings as the first adapter; after qualification the model chooses one allowed action per turn, with the runtime's guards (allowed actions from what exists and the capabilities, the confidence threshold for state-changing actions, word checks that only downgrade, the text check for dates, times and durations); the meeting's facts in the model's context; history trimming with an output reserve per model family; runtime texts in the conversation's language; never silent after a pass to the team, held-back owner emails folded; a cancellation clears the appointment id and keeps the conversation open; when the model fails twice, the holding message once per outage and no handoff. Principle 1 amended for the after-qualification turns. Simulator results in section 8, step 6; open question 12 added. |
