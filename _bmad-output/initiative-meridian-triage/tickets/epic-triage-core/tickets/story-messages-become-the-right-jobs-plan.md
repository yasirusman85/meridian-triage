---
title: 'Messages become the right jobs'
type: feature
ticket: '4'
created: '2026-11-16'
status: in-progress
baseline_revision: '730939f0345268feb258abc8916e075699534f7e'
route: full
route_source: auto
risk: high
review: ''
review_source: ''
lenses_ran: []
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md'
  - '{project-root}/_bmad-output/initiative-meridian-triage/tickets/epic-triage-core/tickets/story-emergency-rules-and-urgency-only-going-up-plan.md'
  - '{project-root}/eval/README.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Every message makes exactly one job and only the model's first issue is used, so Joy's tap and bulb become one plumbing job, the three Walsh messages become three jobs, and nothing links a job to a known property.

**Approach:** After the model prepares a message, deterministic code turns its issue list into jobs (AD-3, AD-13): `PropertyResolver` matches each issue's `property_text` against seeded properties (unresolved or ambiguous → Unsure category, property unset); `FollowUpMatcher` attaches an issue to an existing open job at the same property and category (or either Unsure) opened in the last 7 days, as a follow-up that can raise urgency; every other issue becomes its own job. Emergency rules still apply to every job made from the message.

## Boundaries & Constraints

**Always:** The model only proposes; splitting, property matching and follow-up matching are plain C# with unit tests. The original message is stored once and linked to every job it produced (many jobs ↔ one message, one job ↔ many messages). Every job gets its own `RulesApplied`, model outcome and `JobPrepared` events; an attached follow-up appends `FollowUpAttached` (message id, matched job reference) to the existing job and raises its urgency via `RaiseUrgency` only. Seed data: buildings and properties for the six client messages and the eval set's addresses (22 Pemberton Road, Alder Court, Beechwood House, 8 Carlton Street and those used in `eval/cases`), loaded at startup if empty. When the model fails, the message still makes one Unsure job (as now). A message whose model output says it is not maintenance (empty `issues`) makes one Unsure job flagged for a person, never zero jobs.

**Never:** No AI call in matching. No contractor lookup (story 5). No new NuGet packages.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Two issues | Joy's message: tap + bulb, Flat 4, 22 Pemberton Road | Two jobs, Plumbing and Electrical, same property, same message linked to both | No error expected |
| Follow-up, same sender | Walsh 19:15 then "Still nothing" 19:51 (Unsure) | One job; second message attached as follow-up | No error expected |
| Follow-up, other sender | s.walsh 20:30 about heating at 14b Alder Court | Attached to the same job | No error expected |
| Different category, same property | Open Heating job, new Plumbing issue at same flat | New job | No error expected |
| Old job | Matching job opened 8 days ago | New job | No error expected |
| Closed job | Matching job Rejected | New job | No error expected |
| Unknown address | "Flat 3, Nowhere Street" | Job with property unset, category Unsure | No error expected |
| Ambiguous address | "Alder Court" with several flats | Property unset, Unsure | No error expected |
| No address | text names no property | Property unset, Unsure | No error expected |
| Translated address | "Mieszkanie 9, Beechwood House" | Resolves to Flat 9, Beechwood House | No error expected |
| Follow-up raises urgency | Open Urgent job, follow-up proposal Emergency | Job becomes Emergency | No error expected |
| Not maintenance | model returns empty issues | One Unsure job | No error expected |

</frozen-after-approval>

## Code Map

Read the current `Job`, `Message`, `SubmitMessage`, `PrepareJob`, `ListQueue`, `MeridianDbContext` and the existing migrations before changing anything; keep event and rule behaviour from stories 1–3 intact.

- `src/Meridian.Domain/Properties/` (new) -- `Building`, `Property` entities
- `src/Meridian.Domain/Jobs/Job.cs` -- property reference; `AttachFollowUp(message, proposal, nowUtc)`; many-to-many with messages
- `src/Meridian.Application/Triage/PropertyResolver.cs`, `FollowUpMatcher.cs` (new) -- AD-13
- `src/Meridian.Application/Messages/SubmitMessage.cs`, `Triage/PrepareJob.cs` -- restructure so one message yields N jobs or follow-ups after preparation; the API returns all job references created or matched
- `src/Meridian.Infrastructure/Persistence/` -- mappings, migration, `SeedData` (buildings, properties)
- `src/Meridian.Infrastructure/Triage/Recorded/01-*.json` -- ensure Joy's recorded answer has two issues; Walsh follow-ups have recorded answers
- `src/Meridian.Web/`, `src/client/` -- POST returns references list; queue shows property (or "unresolved") and follow-up count
- `src/Meridian.Tests/` -- one test per matrix row; an API test posting the three Walsh messages in order yielding one job

## Tasks & Acceptance

**Execution:**
- [ ] Domain: properties, job–property link, follow-ups, many-to-many messages -- R6, R8
- [ ] Application: resolver, matcher, pipeline restructure -- AD-3, AD-13
- [ ] Infrastructure: mappings, migration, seed, recorded answers -- persistence
- [ ] Web and client: references list, property and follow-ups in queue -- R13 seed
- [ ] Tests: matrix rows and Walsh API sequence -- I/O matrix

**Acceptance Criteria:**
- Given a fresh database, when the six client messages are posted in order, then the queue shows five jobs: two for Joy, one Walsh job with two follow-ups, one for Nowak, one for Patel.
- Given any job, when its history is read, then rule and model events exist for it and follow-ups appear as `FollowUpAttached`.

## Implementation Notes

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror` -- expected: build succeeds
- `cd src && dotnet test` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: build succeeds
