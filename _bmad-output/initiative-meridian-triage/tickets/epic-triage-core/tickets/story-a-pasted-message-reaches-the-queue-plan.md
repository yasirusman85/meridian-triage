---
title: 'A pasted message reaches the queue'
type: feature
ticket: '1'
created: '2026-11-11'
status: draft
baseline_revision: ''
route: full
route_source: auto
risk: medium
review: ''
review_source: ''
lenses_ran: []
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Nothing exists yet; every later story needs the solution skeleton, the database, the event log and a running UI to build on.

**Approach:** Tracer bullet through every layer: scaffold the projects from the architecture seed, store a pasted message unchanged in SQLite with a placeholder job and a `MessageReceived` event, and list jobs in a React queue page served by ASP.NET Core, all started by `./run.sh`.

## Boundaries & Constraints

**Always:** Projects and dependency directions exactly as the architecture's Design Paradigm (Domain → nothing; Application → Domain; Infrastructure → Application, Domain; Web → all). Package versions from the Stack table, pinned. Message text stored byte-for-byte unchanged (RULE-9). Job state changes only through `Job` domain methods that append a `JobEvent`, saved in one transaction (AD-5). Times from `IClock`, stored UTC (AD-9). Ids are Guids; job reference `MER-` + 6-digit sequence. API under `/api`, camelCase JSON, string enums, DTOs in Web only.

**Never:** No model call, emergency rules, splitting, contractor data or approval in this story. No login. No Docker. No packages beyond the Stack table plus `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` for tests.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Paste a message | sender, text (any language, incl. Polish diacritics) | 201 with job reference; job in queue with status Prepared, category Unsure, urgency Routine; message text identical in DB | No error expected |
| Empty text | text blank or whitespace | 400 problem details | Nothing stored |
| Missing sender | sender blank | 400 problem details | Nothing stored |
| Very long text | 20,000 characters | Stored unchanged | No error expected |
| Restart | app restarted | Jobs still listed | No error expected |

</frozen-after-approval>

## Code Map

Greenfield: the repo holds only `docs/`, `_bmad*/`, `.claude/`, `scripts/`, `conversations/`, `eval/` (empty), `src/` (empty). Create everything below. Do not touch `_bmad*/`, `.claude/`, `docs/`.

- `src/Meridian.sln` -- solution with all six projects
- `src/Meridian.Domain/` -- `Message`, `Job`, `JobEvent`, `JobStatus`, `Urgency`, `Category`, `EventType`
- `src/Meridian.Application/` -- `IClock`, `IJobRepository` (or `IUnitOfWork`), `SubmitMessage` use case, `ListQueue` query
- `src/Meridian.Infrastructure/` -- `MeridianDbContext`, EF migrations, repository, `SystemClock`, `DependencyInjection` extension
- `src/Meridian.Web/` -- `Program.cs` composition, `/api/messages` POST, `/api/jobs` GET, static files + SPA fallback from `wwwroot`, migrate on startup
- `src/Meridian.Eval/` -- empty console project (filled in story 8)
- `src/Meridian.Tests/` -- xUnit tests
- `src/client/` -- Vite + React + TypeScript app; build output to `../Meridian.Web/wwwroot`; dev proxy `/api` to `http://localhost:5080`
- `run.sh`, `run.ps1` -- one command to build client and start the web app on port 5080
- `.gitignore` -- add `src/Meridian.Web/wwwroot/`, `src/client/node_modules/`, `*.db`

## Tasks & Acceptance

**Execution:**
- [ ] `src/` -- create solution and six projects targeting `net8.0` with project references per the paradigm; add EF Core Sqlite 8.0.34, EF Design 8.0.34, Mvc.Testing 8.0.34, xunit 2.9.3 -- skeleton every story builds on
- [ ] `src/Meridian.Domain/` -- entities and enums; `Job.CreateFromMessage(message, reference, clock)` appends `MessageReceived` and `JobPrepared` events -- AD-5 write path from day one
- [ ] `src/Meridian.Application/` -- `SubmitMessage` validates, creates message and job, saves in one transaction; `ListQueue` returns jobs ordered by urgency desc then received asc -- R1, R13 ordering seed
- [ ] `src/Meridian.Infrastructure/` -- DbContext (enums stored as strings, UTC conversion), initial migration, repository, reference sequence -- persistence
- [ ] `src/Meridian.Web/` -- endpoints, problem details, migrate on startup, serve SPA -- one process
- [ ] `src/client/` -- queue page: paste form (sender, text) and job list (reference, received, status, urgency, category, first 120 chars) -- R13 tracer
- [ ] `run.sh`, `run.ps1` -- `npm ci` (or install) and build client, `dotnet run --project src/Meridian.Web --urls http://localhost:5080` -- RULE-12
- [ ] `src/Meridian.Tests/` -- unit tests for `Job.CreateFromMessage` events and validation; one `WebApplicationFactory` test posting a Polish message and reading it back unchanged with its event -- I/O matrix

**Acceptance Criteria:**
- Given a fresh clone with .NET 8 and Node 22, when `./run.sh` runs, then the app serves the queue page at http://localhost:5080 within the first run.
- Given a stored job, when its history is read from the database, then it has a `MessageReceived` event with the actor `system` and a UTC timestamp.

## Implementation Notes

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror` -- expected: build succeeds
- `cd src && dotnet test` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: files written to `src/Meridian.Web/wwwroot`

**Manual checks (if no CLI):**
- Start `./run.sh`, paste the Nowak message (Polish), see it listed; restart and see it still listed.
