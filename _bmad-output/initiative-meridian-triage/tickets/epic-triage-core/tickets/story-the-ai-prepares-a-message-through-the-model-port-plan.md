---
title: 'The AI prepares a message through the model port'
type: feature
ticket: '2'
created: '2026-11-16'
status: in-progress
baseline_revision: 'd065634c6019cfde17533aabe3ed6312332d5f3b'
route: full
route_source: auto
risk: high
review: ''
review_source: ''
lenses_ran: []
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md'
  - '{project-root}/_bmad-output/initiative-meridian-triage/tickets/epic-triage-core/tickets/story-a-pasted-message-reaches-the-queue-plan.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Jobs are created as placeholders (Unsure, Routine) with nothing prepared; the product's AI component does not exist yet, and nothing defines what the code may trust from a model.

**Approach:** Add the `ITriageModel` port (AD-1) with a recorded-response stub as the default adapter, a single `triage.v1` validator (AD-2) with one retry, a 20-second timeout, and a fall-back to Unsure (RULE-11, R10). After the message and job are stored, the pipeline asks the model, validates, applies the first issue's preparation to the job through domain methods, and records the outcome and model metadata as an event. Splitting into several jobs, emergency rules and follow-ups are later stories.

## Boundaries & Constraints

**Always:** Only `Meridian.Infrastructure` contains model adapters; Application depends on the port only. Model output is applied through `Job` methods that append events; urgency changes only via `Job.RaiseUrgency` (never lowers, AD-4). The message and job are saved before the model is called, so a model failure never loses a message. The event payload records model id, prompt version, latency, raw output (or the failure reason), attempt count and validation errors. The model receives only message text and received time (AD-10). The validator rejects any category or urgency outside the enums, empty `issues`, and missing required fields.

**Never:** No OpenRouter adapter (story 9). No emergency rules, splitting into several jobs, property resolution or contractor lookup. No AI-written text reaches a tenant. No new NuGet packages beyond the Stack table (use `System.Text.Json`).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Valid recorded response | Stub has a `triage.v1` answer for the text | Job takes the first issue's category, explanation, English summary, language; urgency raised to the proposal; `ModelPrepared` event with metadata | No error expected |
| Invalid JSON, then valid | First attempt unparseable, second valid | Prepared from the second; event shows 2 attempts | Retry once |
| Invalid twice | Both attempts fail validation (e.g. category "Roofing") | Job stays Unsure, urgency unchanged; `ModelPreparationFailed` event with errors | Message and job still queued |
| Timeout | Model takes over 20 s | Unsure; failure event with reason `timeout` | Request returns normally |
| Model throws / unavailable | Adapter exception | Unsure; failure event with reason `unavailable` | No 500 to the caller |
| No recorded answer | Stub has no answer for the text | Unsure; failure event with reason `no-recorded-response` | Treated as unavailable |
| Low confidence | Valid answer with `confidence: low` | Category set to Unsure; explanation kept; urgency still raised | No error expected |
| Proposal lower than current | Job urgency already higher, model says Routine | Urgency unchanged | No error expected |

</frozen-after-approval>

## Code Map

- `src/Meridian.Domain/Jobs/Job.cs` -- add `Explanation`, `SummaryEn`, `Language` properties; `ApplyModelPreparation(category, urgency, explanation, summaryEn, language, payloadJson, nowUtc)` appends `ModelPrepared` and calls `RaiseUrgency`; `RecordModelFailure(payloadJson, nowUtc)` appends `ModelPreparationFailed`; `RaiseUrgency(level, reason, nowUtc)` ignores lower values and appends `UrgencyRaised` when it changes. Keep `CreateFromMessage` as it is.
- `src/Meridian.Domain/Jobs/EventType.cs` -- add `ModelPrepared`, `ModelPreparationFailed`, `UrgencyRaised`
- `src/Meridian.Application/Triage/` (new) -- `ITriageModel`, `TriageRequest(text, receivedAtUtc)`, `TriageModelResult` (raw output, model id, prompt version, latency ms, token counts nullable, success flag, failure reason), `TriageContract` (typed `triage.v1` DTOs), `TriageValidator` (parse + validate, returns parsed result or error list), `PrepareJob` use case (call with 20 s linked timeout, validate, one retry, apply to job, save)
- `src/Meridian.Application/Messages/SubmitMessage.cs` -- after the existing transaction, call `PrepareJob` for the new job; keep validation and return shape
- `src/Meridian.Infrastructure/Triage/StubTriageModel.cs` (new) -- loads `recorded/*.json` from `src/Meridian.Infrastructure/Triage/Recorded/` (copied to output) keyed by SHA-256 of the whitespace-trimmed text; returns the recorded raw JSON string, or failure `no-recorded-response`. Model id `stub`, prompt version `triage.v1`.
- `src/Meridian.Infrastructure/Triage/Recorded/` -- recorded answers for the six forwarded messages (from `docs/input/client-input.md`), written by hand to `triage.v1`; each file has `text` and `response`. Mark in a README.md in that folder that they are hand-written.
- `src/Meridian.Infrastructure/Prompts/triage.v1.md` (new) -- the system prompt the real adapter will use; lists the allowed categories and urgencies and the JSON shape; copied to output.
- `src/Meridian.Infrastructure/DependencyInjection.cs` -- register `StubTriageModel` as `ITriageModel`
- `src/Meridian.Infrastructure/Persistence/Configurations/JobConfiguration.cs` + new migration -- map the new Job columns
- `src/Meridian.Web/Api/Dtos.cs`, `ApiEndpoints.cs` -- queue DTO adds explanation and summaryEn
- `src/client/src/` -- queue table shows explanation (short) and English summary when present
- `src/Meridian.Tests/` -- tests per matrix row, using a fake `ITriageModel` for retry/timeout/throw cases and the real stub for the recorded case

`triage.v1` shape:

```json
{"language":"pl","issues":[{"summary_en":"...","category":"General","urgency":"Urgent","property_text":"Flat 9, Beechwood House","explanation":"...","vulnerability_mentioned":true,"confidence":"medium"}]}
```

Categories: `Plumbing, Electrical, Heating, Appliances, LocksAndDoors, Pest, General, Unsure`. Urgencies: `Routine, Urgent, Emergency`.

## Tasks & Acceptance

**Execution:**
- [ ] `src/Meridian.Domain/Jobs/` -- new properties, methods and event types -- AD-4, AD-5
- [ ] `src/Meridian.Application/Triage/` -- port, contract, validator, `PrepareJob` -- AD-1, AD-2, RULE-11
- [ ] `src/Meridian.Application/Messages/SubmitMessage.cs` -- call `PrepareJob` after save -- pipeline order AD-3
- [ ] `src/Meridian.Infrastructure/Triage/`, `Prompts/` -- stub adapter, recorded answers for the six messages, prompt file -- AD-1, AD-10
- [ ] `src/Meridian.Infrastructure/` -- EF mapping and migration -- persistence
- [ ] `src/Meridian.Web/`, `src/client/` -- show explanation and summary in the queue -- R7 visible
- [ ] `src/Meridian.Tests/` -- one test per matrix row plus validator unit tests for each rejected field -- I/O matrix

**Acceptance Criteria:**
- Given the app started with `./run.sh`, when the Nowak message is pasted, then its queue row shows an English summary and an explanation.
- Given any job, when its events are read, then exactly one of `ModelPrepared` or `ModelPreparationFailed` follows `JobPrepared`, carrying model id `stub` and prompt version `triage.v1`.

## Implementation Notes

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror` -- expected: build succeeds
- `cd src && dotnet test` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: build succeeds
