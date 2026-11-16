---
title: 'Each job shows its building and usual contractor'
type: feature
ticket: '5'
created: '2026-11-16'
status: built
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
- [x] Domain entities and job method -- R11, R12
- [x] Application lookup and pipeline call -- R12
- [x] Infrastructure mapping, migration, seed -- persistence
- [x] Web and client display -- R11
- [x] Tests -- I/O matrix

**Acceptance Criteria:**
- Given the six client messages posted, when the queue is shown, then every job with a resolved category shows a building and a contractor, and the Nowak (Unsure) job shows "needs a person".

## Implementation Notes

- **Domain.** `Contractor` (name, trade = `Category`, phone) and `ContractorAssignment` (`BuildingId` null = company-wide default for the category) in `Meridian.Domain/Contractors`, with `ContractorRule { Building, Default }` and the `ContractorSuggestion(ContractorId, Rule)` record. `Job.SuggestContractor(suggestion, nowUtc)` sets `SuggestedContractorId`/`SuggestedContractorRule` and appends `ContractorSuggested {contractorId, rule: "building"|"default"}`; the same suggestion again is a no-op (no event); it throws on an Unsure job.
- **Lookup.** `ContractorLookup(assignments).Suggest(buildingId?, category)`: building assignment, else the default, else null; Unsure → null. Duplicate assignments throw. `SubmitMessage` takes a new `IContractorRepository` and calls the lookup right after `PrepareJob.RecordIssue` for each new job (AD-3 order: jobs, then contractor lookup). Follow-ups do not change category, so they write no new suggestion. Model-failure, not-maintenance and orphan-recovery jobs are Unsure and get none.
- **"No property" row vs. "Always".** The lookup gives the default for a null building (matrix row). In the pipeline an unresolved property already forces category Unsure (story 4), so such a job gets no suggestion, which satisfies "unresolved property gives no suggestion". The default-for-no-building path will apply when a person later sets the category (review story); that story should call `ContractorLookup` + `SuggestContractor` on a category change. There is no way yet to clear a suggestion if a person sets a job back to Unsure.
- **Persistence.** Tables `Contractors`, `ContractorAssignments` (unique `(BuildingId, Category)` plus a filtered unique index on `Category` where `BuildingId IS NULL`, since SQLite treats NULLs as distinct); `Jobs.SuggestedContractorId` (nullable FK) and `SuggestedContractorRule`. Migration `Contractors` (dotnet-ef 8.0.34) checked up and down on a database holding a job row.
- **Seed.** Nine fictional contractors (phones in Ofcom's 07700 900xxx drama range): one per trade plus `Irwell Pipe and Drain` (plumbing) and `Cottonfield Boiler Services` (heating) as fallbacks that are nobody's usual contractor. Defaults for all seven trades; every seeded building has its own Heating, Plumbing, Electrical and General assignment (to the usual trade contractor), so Pest, Appliances and LocksAndDoors use the default. `EnsureSeededAsync` now seeds buildings when there are none and contractors + assignments when there are no contractors, so existing databases pick them up at next start; an overload takes other assignments (used by the "seed changed" test).
- **API and client.** `GET /api/jobs` adds `buildingName`, `suggestedContractorId`, `suggestedContractorName`, `suggestedContractorPhone`, `suggestedContractorRule`. The queue has a Contractor column: "Building · Contractor (phone)" with "usual for building" / "company default", or "Building · needs a person".
- **Interface change.** `SubmitMessage` and `ListQueue` constructors gained an `IContractorRepository` parameter (DI registered in `AddMeridianInfrastructure`); anything constructing them directly (e.g. the eval runner in story 8) must pass one.

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror` -- expected: build succeeds
- `cd src && dotnet test` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: build succeeds
