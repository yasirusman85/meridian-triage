---
title: 'Emergency rules and urgency only going up'
type: feature
ticket: '3'
created: '2026-11-16'
status: in-progress
baseline_revision: '8e7f11a796d767e3fb07a287c799ef86ec9964e2'
route: full
route_source: auto
risk: high
review: ''
review_source: ''
lenses_ran: []
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md'
  - '{project-root}/_bmad-output/initiative-meridian-triage/tickets/epic-triage-core/tickets/story-the-ai-prepares-a-message-through-the-model-port-plan.md'
  - '{project-root}/eval/README.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Only the model can currently raise urgency, so a gas report or a ceiling leak depends on a probabilistic answer, and nothing stops a person-free path from lowering urgency.

**Approach:** Deterministic `EmergencyRules` in Domain run on every message before the model (AD-3): gas or a gas smell, water coming through a ceiling, and no heating or hot water from October to April (UK local month). A rule hit raises the job to Emergency and records which rule fired. The model's proposal can only raise further (already built); a person can lower urgency only through `Job.SetUrgencyByPerson`, which records who and why.

## Boundaries & Constraints

**Always:** Rules are plain C# with unit tests and no model call. Rules run before the model step, so they still apply when the model fails (RULE-11). A rule hit appends a `RulesApplied` event listing rule ids and the matched phrase; no hit appends `RulesApplied` with an empty list. The winter window uses the received time converted to Europe/London. Matching is case-insensitive, tolerant of common misspellings and punctuation, and covers the eval set's phrasings (English and the Polish, Urdu-in-Latin and Romanian gas phrasings in `eval/cases`). False positives are acceptable over misses (over-escalation is tolerable; a missed gas report is critical).

**Never:** No lowering of urgency anywhere except `SetUrgencyByPerson`. No AI call in the rules. No UI for lowering yet (review story).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Gas smell | "I can smell gas in the kitchen" | Emergency; rule `gas` | No error expected |
| Gas phrased oddly | "smells like rotten eggs near the boiler", "CO alarm going off", eval non-English gas cases | Emergency; rule `gas` or `carbon-monoxide` | No error expected |
| Ceiling leak | "water coming through the ceiling" / "dripping from the light fitting upstairs" | Emergency; rule `water-through-ceiling` | No error expected |
| Drip under sink | "tap dripping", "leak under the sink" | No rule hit | No error expected |
| No heating in January | received 2026-01-14, "no heating" | Emergency; rule `no-heating-winter` | No error expected |
| No heating in July | received 2026-07-14, "no heating" | No rule hit | No error expected |
| Winter edge | received 2026-10-01 08:10 Europe/London (07:10 UTC) | In window | No error expected |
| Model says Routine after rule hit | rule Emergency, model Routine | Stays Emergency | No error expected |
| Model fails after rule hit | rule Emergency, model times out | Emergency, Unsure category | No error expected |
| Person lowers | Emergency job, person sets Urgent with reason | Urgent; `UrgencySetByPerson` event with actor and reason | Reason required |

</frozen-after-approval>

## Code Map

- `src/Meridian.Domain/Rules/EmergencyRules.cs` (new) -- `Evaluate(string text, DateTime receivedAtUtc) → IReadOnlyList<RuleHit>` (rule id, matched phrase); phrase lists as static data in the same folder
- `src/Meridian.Domain/Jobs/Job.cs` -- `ApplyRules(hits, nowUtc)` appends `RulesApplied` and calls the existing `RaiseUrgency` with reason `rule:<id>`; add `SetUrgencyByPerson(level, actor, reason, nowUtc)`; persist the fired rule ids on the job (`RuleIds` string column) for the job view
- `src/Meridian.Domain/Jobs/EventType.cs` -- add `RulesApplied`, `UrgencySetByPerson`
- `src/Meridian.Application/Messages/SubmitMessage.cs` / `Triage/PrepareJob.cs` -- run rules after the job is created and before the model call, in the first transaction
- `src/Meridian.Infrastructure/` -- EF mapping + migration for `RuleIds`
- `src/Meridian.Web/Api/Dtos.cs`, `src/client/src/` -- queue shows fired rules (e.g. "rule: gas")
- `src/Meridian.Tests/Domain/EmergencyRulesTests.cs` (new) -- one test per matrix row, plus a theory that loads every `eval/cases/*.json` whose expected issues include a gas or ceiling or winter no-heating emergency that a rule should catch and asserts a hit (skip cases the eval README marks as AI-only emergencies, e.g. sparking socket)

## Tasks & Acceptance

**Execution:**
- [ ] `src/Meridian.Domain/Rules/` -- rules and phrase data -- R4
- [ ] `src/Meridian.Domain/Jobs/` -- `ApplyRules`, `SetUrgencyByPerson`, events, `RuleIds` -- R5, R9, AD-4
- [ ] `src/Meridian.Application/` -- rules before model in the pipeline -- AD-3
- [ ] `src/Meridian.Infrastructure/` -- mapping and migration -- persistence
- [ ] `src/Meridian.Web/`, `src/client/` -- show fired rules -- R11 seed
- [ ] `src/Meridian.Tests/` -- matrix rows and eval-case theory -- I/O matrix

**Acceptance Criteria:**
- Given the Walsh message received on a Friday in January, when it is pasted, then its job is Emergency with rule `no-heating-winter` shown in the queue, whatever the stub answers.
- Given any job, when its events are read, then `RulesApplied` comes before the model outcome event.

## Implementation Notes

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror` -- expected: build succeeds
- `cd src && dotnet test` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: build succeeds
