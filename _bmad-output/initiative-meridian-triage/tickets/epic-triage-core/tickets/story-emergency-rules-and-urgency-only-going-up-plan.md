---
title: 'Emergency rules and urgency only going up'
type: feature
ticket: '3'
created: '2026-11-16'
status: built
baseline_revision: '8e7f11a796d767e3fb07a287c799ef86ec9964e2'
route: full
route_source: auto
risk: high
review: quick
review_source: pinned
lenses_ran: [inline]
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
- [x] `src/Meridian.Domain/Rules/` -- rules and phrase data -- R4
- [x] `src/Meridian.Domain/Jobs/` -- `ApplyRules`, `SetUrgencyByPerson`, events, `RuleIds` -- R5, R9, AD-4
- [x] `src/Meridian.Application/` -- rules before model in the pipeline -- AD-3
- [x] `src/Meridian.Infrastructure/` -- mapping and migration -- persistence
- [x] `src/Meridian.Web/`, `src/client/` -- show fired rules -- R11 seed
- [x] `src/Meridian.Tests/` -- matrix rows and eval-case theory -- I/O matrix

**Acceptance Criteria:**
- Given the Walsh message received on a Friday in January, when it is pasted, then its job is Emergency with rule `no-heating-winter` shown in the queue, whatever the stub answers.
- Given any job, when its events are read, then `RulesApplied` comes before the model outcome event.

## Implementation Notes

- Rules are regular-expression phrase lists in `Rules/EmergencyPhrases.cs`, matched case-insensitively on the text as received, so `RuleHit.MatchedPhrase` is the tenant's own span. Proximity patterns (water near ceiling, heating near "not/stopped/cold") do not cross `.`, `!`, `?` or a new line, which keeps "we've got hot water. Didn't do this before." (real-06) from firing. All quantifiers are bounded; a 70 000-character message evaluates in milliseconds.
- Rule ids: `gas`, `carbon-monoxide`, `water-through-ceiling`, `no-heating-winter` (`EmergencyRuleIds`). At most one hit per rule, in that order. "No hot water" alone also fires the winter rule (Intent: "no heating or hot water"). A light fitting counts as a ceiling ("dripping from the light fitting upstairs").
- Urdu gas is matched in Urdu script (گیس, Arabic or Farsi kaf/yeh) and in Latin script (`gas`, `gais`); the eval set has only the script form (var-05).
- Winter window: `TimeZoneInfo` `Europe/London` (or `GMT Standard Time`); if neither exists, a built-in UK BST rule. Tests cover the BST edges on 30 September and 30 April.
- `Job.ApplyRules` always appends `RulesApplied` (`{"rules":[{"id","matchedPhrase"}]}`), merges fired ids into `RuleIds` (comma-separated column `RuleIds`, backed by field `_ruleIds`, default `''`), then calls `RaiseUrgency(Emergency, "rule:<first id>")`. It may be called again for later messages (follow-ups, story 4); ids are de-duplicated. Unknown rule ids are refused, so a comma can never enter the column.
- `Job.SetUrgencyByPerson(level, actor, reason, nowUtc)` sets any level, up or down, and appends `UrgencySetByPerson` with the person as event actor and `{from,to,reason}`. Reason and actor are required; actor may not be `system` and is at most 64 characters (`JobEvent.MaxActorLength`, now also used by the EF mapping). Fired `RuleIds` stay on the job after a person lowers it. No caller yet (review story).
- `SubmitMessage` evaluates the rules inside the first unit of work, right after `Job.CreateFromMessage`, so the rule result is committed before `PrepareJob` calls the model; `PrepareJob` is unchanged.
- Event order: in the job's event list `RulesApplied` always precedes the model outcome. In the database the two are in different saves, so with the real clock they get different timestamps; same-save ties remain the deferred sequence-number item (story 1.7).
- The eval theory runs every `eval/cases/*.json` with an expected Emergency except the AI-only ones (`var-15`, `var-30`, `var-31`, `var-38`, and `real-04`, whose notes say a keyword rule may not fire): 17 cases, all hit. A second theory asserts no hit on the 30 cases with no expected Emergency (no over-escalation by rule on the set).
- Migration `EmergencyRules` generated with `dotnet-ef` 8.0.34.

## Plan Change Log

## Review Triage Log

**Pass 1 (inline, 2026-11-16).** high 0, medium 0, low 1. Build, 232 tests and client build pass; the eval-set theory finds a rule hit on all 17 rule-catchable emergencies and none on 30 non-emergencies.

| Verdict | Route | Finding | Evidence / action |
|---|---|---|---|
| low | defer | Non-English gas phrases beyond the eval cases are unverified by a native speaker | Recorded for AI-DESIGN/README limits; over-escalation is tolerable, and the AI can still raise. |

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror` -- expected: build succeeds
- `cd src && dotnet test` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: build succeeds
