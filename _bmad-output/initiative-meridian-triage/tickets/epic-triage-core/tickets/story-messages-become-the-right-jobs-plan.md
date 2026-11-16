---
title: 'Messages become the right jobs'
type: feature
ticket: '4'
created: '2026-11-16'
status: built
baseline_revision: '730939f0345268feb258abc8916e075699534f7e'
route: full
route_source: auto
risk: high
review: quick
review_source: pinned
lenses_ran: [inline]
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
- [x] Domain: properties, job–property link, follow-ups, many-to-many messages -- R6, R8
- [x] Application: resolver, matcher, pipeline restructure -- AD-3, AD-13
- [x] Infrastructure: mappings, migration, seed, recorded answers -- persistence
- [x] Web and client: references list, property and follow-ups in queue -- R13 seed
- [x] Tests: matrix rows and Walsh API sequence -- I/O matrix

**Acceptance Criteria:**
- Given a fresh database, when the six client messages are posted in order, then the queue shows five jobs: two for Joy, one Walsh job with two follow-ups, one for Nowak, one for Patel.
- Given any job, when its history is read, then rule and model events exist for it and follow-ups appear as `FollowUpAttached`.

## Implementation Notes

- **Pipeline order (AD-3).** `SubmitMessage` now: (1) stores the message alone in the first unit of work, (2) evaluates `EmergencyRules`, (3) asks the model (`PrepareJob.AskAsync`, unchanged retry/timeout logic, now returns a `ModelPreparation`), (4) in a second unit of work (saved with `CancellationToken.None`) resolves properties, matches follow-ups and creates one job per remaining issue. Jobs are created after preparation because their number is unknown before it; the placeholder job that stories 1–3 created before the model call is gone. Consequence: a rule emergency is committed with its job after the model step (at most the 20 s budget), not before it, and a crash during the model call leaves a stored message with no job. The story-1/3 tests that asserted "job exists when the model is called" now assert "message exists when the model is called".
- **Jobs.** Every new job gets `MessageReceived`, `JobPrepared` (now with `propertyId`), `RulesApplied` (same rule result on every job of the message) and exactly one model outcome. A model failure makes one Unsure job with no property. A valid answer with empty `issues` makes one Unsure job via `Job.ApplyNotMaintenance` (`ModelPrepared` with `applied.notMaintenance = true`, explanation `Job.NotMaintenanceExplanation`). The validator now accepts empty `issues` and the prompt says to return it for non-maintenance (story 2 had rejected it). Note: `eval/README.md` still says a non-maintenance case is correct only when no job is created; this story's plan says one Unsure job, so story 8 must score that row accordingly.
- **Follow-ups.** `FollowUpMatcher.Match(jobs, propertyId, category, nowUtc)`: resolved property only (null never matches), same category or either Unsure, `IsOpen` (not Attended/Rejected), `CreatedAtUtc` within 7 days inclusive of the message's received time; ties → same category, newest, lowest reference. Candidates are jobs that existed before the message (`IJobRepository.ListOpenAtPropertiesAsync`), so the model's split within one message stands. Several issues matching one job attach the message once with the highest proposed urgency. `Job.AttachFollowUp` links the message, appends `FollowUpAttached {messageId, reference, proposedUrgency}` and calls `RaiseUrgency(..., "follow-up")`; then `ApplyRules` runs on the job, so a follow-up mentioning gas escalates it.
- **Category.** Applied category is Unsure when confidence is low or the property is not resolved; `applied` in `ModelPrepared` records `proposedCategory`, `issueIndex`, `propertyText`, `propertyResolution` (`Resolved|NoAddress|Unresolved|Ambiguous`) and `propertyId`.
- **PropertyResolver.** Normalises (lower case, accents stripped, punctuation of any script to spaces, `st/rd/hse/ave/ln/ct/gr/pl/cl` expanded, unit words Flat/Apt/Mieszkanie/Apartamentul/فلیٹ dropped, `flat4` split). A building matches when its name words appear in order, plus its leading house number just before them when it has one. Left-over number tokens choose the flat. Ambiguous: a block named without a flat, two flats, or two buildings in one text. Unresolved: no building, an unknown flat, or a flat number on a house. All 53 non-null eval-case addresses resolve.
- **Persistence.** `Messages.JobId` replaced by join table `JobMessages (JobId, MessageId)`; `Jobs.PropertyId` nullable FK; tables `Buildings`, `Properties`. Migration `PropertiesAndFollowUps` (dotnet-ef 8.0.34) was hand-ordered to copy existing links into `JobMessages` before dropping the column, and its Down restores one job per message; checked up and down on a database holding a row. `SeedData` (44 buildings, flats or whole houses, ids derived from names) is loaded by `MigrateMeridianDatabaseAsync` when `Buildings` is empty. Alder Court has flats 14a, 14b, 15a, 15b, 16.
- **API and client.** `POST /api/messages` returns `{messageId, jobs: [{jobId, reference, outcome: created|followUp}]}`. `GET /api/jobs` adds `propertyId`, `propertyName` and `followUpCount`. The queue shows a Property column ("unresolved" when null) with an "N follow-ups" label, and the paste form reports created and attached references.
- **Not done here.** No `Reject` or `Attend` domain method exists yet, so tests set `Status` by reflection (`TestJobs.SetStatus`). The acknowledgement story (2.1) plans to write its acknowledgement in the first transaction "with the job"; with this restructure there is no job until after the model, so that story must send in the second unit of work (or per job/follow-up there).

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror` -- expected: build succeeds
- `cd src && dotnet test` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: build succeeds
