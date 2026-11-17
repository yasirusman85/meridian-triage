# Epics and stories

Generated from the BMAD ticket tree in `_bmad-output/initiative-meridian-triage/` (Sofia, the product owner). Each story's build plan, with its review log, is beside its epic's `tickets.toml`.

## Initiative requirements

- R1 (prd FR-1): A received message is stored with its sender, received time, text and attachments unchanged.
- R2 (prd FR-2): A message in any language is held with its original text kept beside any translation.
- R3 (prd FR-3): A second intake channel can be added without changing how jobs are prepared.
- R4 (prd FR-4): An issue matching a fixed emergency rule (gas or gas smell, water through a ceiling, no heating October to April) is flagged emergency.
- R5 (prd FR-5): A rule-flagged emergency cannot be lowered by the AI, only by a person.
- R6 (prd FR-6): A message with several distinct issues becomes one job per issue.
- R7 (prd FR-7): Each issue gets a proposed category, urgency, property and explanation, plus an English summary when the message is not in English.
- R8 (prd FR-8): A message from the same property about the same open fault is attached to that job as a follow-up, matched by fixed code.
- R9 (prd FR-9): A job's urgency is the higher of the rule result and the AI proposal; follow-ups can raise it.
- R10 (prd FR-10): An unsure or invalid AI result sends the issue to the Unsure category with urgency no lower than the rules gave.
- R11 (prd FR-11): Each job shows its building, the usual contractor for its category there, and the emergency rules that applied.
- R12 (prd FR-12): The suggested contractor comes from a fixed lookup by category and building.
- R13 (prd FR-13): The on-duty person sees all prepared jobs ordered by urgency then age, each with its explanation and original messages.
- R14 (prd FR-14): The on-duty person can approve, edit (category, urgency, contractor, property) or reject a job, and each edit is recorded before and after.
- R15 (prd FR-15): No job reaches a contractor until a person has approved it.
- R16 (prd FR-16): Every message gets an acknowledgement with a job reference within 5 minutes; a gas message gets the fixed gas safety message instead.
- R17 (prd FR-17): Every text a tenant receives comes from a fixed template.
- R18 (prd FR-18): An emergency notifies Rachel at once and the backup if she has not acknowledged within 30 minutes.
- R19 (prd FR-19): An emergency acknowledged by neither is marked escalation-failed and held at the top of the queue; notifications are visible in the product.
- R20 (prd FR-20): An approved job reaches its contractor with a link the contractor uses to mark it attended.
- R21 (prd FR-21): An emergency not marked attended within its remaining 24 hours alerts the on-duty person to send the fallback contractor.
- R22 (prd FR-22): Every step of every job is recorded as an event, including the AI's model, prompt version and raw output.
- R23 (prd FR-23): A job's full history can be viewed in time order.
- R24 (prd FR-24): A numbers page shows jobs by category and urgency, time to first human look, time to approval, time to attendance, and how often each promise was met.

## Epic 1: Every message is prepared and approved

A pasted message is stored, run through the emergency rules, prepared by the AI behind the product's interface (stub by default), split and grouped, shown in the review queue with its map and explanation, approved or edited by a person, and every step recorded; the eval set runs against it.

Covers: R1, R2, R3, R4, R5, R6, R7, R8, R9, R10, R11, R12, R13, R14, R15, R22, R23

| Story | Title | What is true when done | Verify | Covers |
|---|---|---|---|---|
| 1.1 | A pasted message reaches the queue | Tracer bullet: solution scaffold per the architecture seed, SQLite with migrations, Message and Job entities with the event table, POST /api/messages stores a message unchanged and creates a placeholder job, and the React queue page lists it; run.sh builds the client and starts everything. | After ./run.sh, pasting a message in the browser shows it in the queue, and the database holds the message text unchanged and a MessageReceived event. | R1, R2, R16 |
| 1.2 | The AI prepares a message through the model port | ITriageModel port, StubTriageModel with recorded responses, the triage.v1 validator with one retry, the 20-second timeout and the fall-back to Unsure; preparation output and its model metadata are recorded as an event. | Unit tests: a recorded valid response yields issues; invalid JSON retries once then yields Unsure; a timeout yields Unsure with the message still queued. | R7, R10, R16 |
| 1.3 | Emergency rules and urgency only going up | Domain EmergencyRules (gas, water through a ceiling, no heating October to April) run before the model; Job.RaiseUrgency and SetUrgencyByPerson per AD-4; merged urgency is the higher of rule and AI. | Unit tests: each rule fires on its phrases and not on near misses; an AI proposal of routine never lowers a rule emergency; no heating in July is not an emergency. | R4, R5, R9 |
| 1.4 | Messages become the right jobs | One job per issue, PropertyResolver against seeded properties with unresolved going to Unsure, and FollowUpMatcher attaching repeat messages to an open job per AD-13. | With the stub, Joy's message makes two jobs and the three Walsh messages make one job with two follow-ups; an unknown address makes an Unsure job. | R3, R6, R8 |
| 1.5 | Each job shows its building and usual contractor | Seed data for buildings, properties, contractors and per-building category assignments; ContractorLookup suggests the contractor by fixed lookup; the job shows building, contractor and the rules that applied. | Every seeded job shows a building and a suggested contractor that matches the seed table for its category; changing the seed changes the suggestion. | R11, R12 |
| 1.6 | A person approves, edits or rejects each job | Review queue ordered by urgency then age with explanation, English summary and original messages; approve, edit (category, urgency, contractor, property) and reject through the state machine, each recorded before and after; acting-as switch for Rachel or the backup. | In the browser, editing a job's category and approving it moves it to Approved and its history shows both values; a Prepared job cannot be sent to a contractor. | R13, R14, R15 |
| 1.7 | A job's full history is visible | Job detail page showing every event in time order, including rules applied, the model id, prompt version and raw output, edits and approval. | Opening any seeded job lists its events in time order with the raw model output visible. | R16, R17 |
| 1.8 | The eval set runs through the real pipeline | Labelled cases in eval/cases (the six forwarded messages plus written variations: every category and urgency, other languages, multi-issue, follow-ups, vague and angry, non-maintenance, instruction injection), Meridian.Eval runner per AD-12, harm-weighted scoring and a Markdown summary. | One command runs the eval with the stub and writes eval/results with emergency recall, category accuracy and every failure listed by harm class. | R4, R6, R7, R9, R10 |
| 1.9 | OpenRouter prepares messages when a key is set | OpenRouterTriageModel behind the same port: OpenAI-compatible chat completions with the versioned prompt, the message wrapped as data, JSON response, token and latency capture; selected only when OPENROUTER_API_KEY is set. | Without a key the app uses the stub; with a key the eval runs against OpenRouter and records the model id in its results. | R7, R10 |
| 1.10 | Refactor sweep | Cleanup only, scoped at its start from the plans and review findings the other stories deferred. | All tests pass, the eval result is unchanged, and the deferred findings list is empty or each item is recorded as a new story. |  |

## Epic 2: Tenants are answered and emergencies reach a person at any hour

Every message is acknowledged from a fixed template, gas reports get the gas safety message, and emergencies notify Rachel then the backup after 30 minutes, with escalation failures held at the top of the queue.

Covers: R16, R17, R18, R19

| Story | Title | What is true when done | Verify | Covers |
|---|---|---|---|---|
| 2.1 | Every tenant gets an acknowledgement | Outbox port and simulated adapter storing OutboundMessage rows; fixed acknowledgement template with job reference sent on intake; the gas safety message replaces it when a gas rule fired; outbound messages listed on the job. | Pasting the Walsh message shows an acknowledgement with its MER reference on the job; a gas message shows the gas safety text with 0800 111 999 instead; no outbound body contains model output. | R1, R2 |
| 2.2 | Emergencies reach Rachel, then the backup | Background worker every minute: notify Rachel at once for emergency jobs, the backup after 30 minutes unacknowledged, escalation-failed after a further 30 minutes; staff acknowledge from the queue; every step an event; notifications panel in the UI. | With a fake clock, an unacknowledged emergency notifies Rachel, then Dawn at +30 min, then is marked escalation-failed at +60 min; acknowledging stops escalation; all steps appear in the job history. | R3, R4 |

## Epic 3: Approved jobs are tracked to attendance and counted

An approved job reaches its contractor with an attended link, an emergency running out of time alerts for the fallback contractor, and the MD's numbers page shows the promise measures from recorded events.

Covers: R20, R21, R24

| Story | Title | What is true when done | Verify | Covers |
|---|---|---|---|---|
| 3.1 | Contractors mark jobs attended | Approving and sending a job writes a contractor outbound message with a single-job token link; the link page lets the contractor mark it attended; emergencies past their 24-hour window raise a fallback-contractor alert. | Sending an approved job shows its contractor link; opening it marks only that job Attended; an emergency not attended within 24 hours shows the fallback alert, with a fake clock. | R1, R2 |
| 3.2 | The MD sees how many jobs and how fast | Numbers page computed from events only: jobs by category and urgency, time to first human look, time to approval, time to attendance, share of 24-hour and five-day promises met. | After running the six seeded messages through to attendance, the numbers page counts and times match the job histories. | R3 |
