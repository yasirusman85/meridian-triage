---
title: 'Each job shows its building and usual contractor'
type: feature
ticket: '5'
created: '2026-11-16'
status: in-progress
baseline_revision: 'a81d0b389ff398fe7a59b11165a0b066c0af49a5'
route: full
route_source: auto
risk: low
review: ''
review_source: ''
lenses_ran: []
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md'
  - '{project-root}/_bmad-output/initiative-meridian-triage/tickets/epic-triage-core/tickets/story-messages-become-the-right-jobs-plan.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A backup who does not know the buildings cannot tell which contractor to send or why a job was flagged.

**Approach:** Seed contractors and a per-building assignment of the usual contractor for each category, and have fixed code (`ContractorLookup`, AD-3) suggest the contractor for each job from its category and building. Each job shows its building, the suggested contractor and the emergency rules that applied, so the map lives in the product (DECISIONS D12).

## Boundaries & Constraints

**Always:** Lookup is plain C# by (building, category), falling back to a company-wide default contractor for the category when the building has no assignment; Unsure category or unresolved property gives no suggestion ("needs a person"). The suggestion is set when the job is created or its category changes, recorded as a `ContractorSuggested` event (contractor id, rule: building or default). Seed: one fictional contractor per category (plus a second heating and plumbing contractor as fallbacks for later stories), each with name, trade and phone; assignments for the seeded buildings. Seeding is idempotent and runs with the existing seed.

**Never:** No AI involvement in contractor choice. No sending to contractors (story 3.1).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Assigned | Heating job at Alder Court | Alder Court's heating contractor; rule `building` | No error expected |
| Default | Pest job at a building with no pest assignment | Default pest contractor; rule `default` | No error expected |
| Unsure | Unsure job | No suggestion; shown as "needs a person" | No error expected |
| No property | Plumbing job, property unresolved | Default plumbing contractor; rule `default` | No error expected |
| Seed changed | Assignment changed in seed and reseeded fresh | New suggestion | No error expected |

</frozen-after-approval>

## Code Map

Read the current domain (Job, Building, Property), `SubmitMessage`, `SeedData`, `ListQueue`, DTOs and `QueuePage.tsx` first.

- `src/Meridian.Domain/Contractors/` (new) -- `Contractor`, `ContractorAssignment`
- `src/Meridian.Domain/Jobs/Job.cs`, `EventType.cs` -- `SuggestedContractorId`, `SuggestContractor(...)`, `ContractorSuggested`
- `src/Meridian.Application/Contractors/ContractorLookup.cs` (new) -- AD-3, AD-13 style plain code
- `src/Meridian.Application/Messages/SubmitMessage.cs` -- call lookup when jobs are created (small change)
- `src/Meridian.Infrastructure/Persistence/` -- mappings, one new migration, seed contractors and assignments
- `src/Meridian.Web/Api/` -- queue DTO adds building name, contractor name and phone, rule ids already there
- `src/client/src/QueuePage.tsx` -- show "Building · Contractor (phone)" or "needs a person"
- `src/Meridian.Tests/` -- matrix rows; API test that the six client messages each show a contractor except the Unsure ones

## Tasks & Acceptance

**Execution:**
- [ ] Domain entities and job method -- R11, R12
- [ ] Application lookup and pipeline call -- R12
- [ ] Infrastructure mapping, migration, seed -- persistence
- [ ] Web and client display -- R11
- [ ] Tests -- I/O matrix

**Acceptance Criteria:**
- Given the six client messages posted, when the queue is shown, then every job with a resolved category shows a building and a contractor, and the Nowak (Unsure) job shows "needs a person".

## Implementation Notes

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror` -- expected: build succeeds
- `cd src && dotnet test` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: build succeeds
