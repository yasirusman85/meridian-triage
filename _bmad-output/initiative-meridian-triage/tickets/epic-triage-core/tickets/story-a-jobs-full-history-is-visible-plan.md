---
title: "A job's full history is visible"
type: feature
ticket: '7'
created: '2026-11-16'
status: built
baseline_revision: '90389426d595b69766c83362d950005eef98f9bf'
route: full
route_source: auto
risk: low
review: quick
review_source: pinned
lenses_ran: [inline]
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md'
  - '{project-root}/_bmad-output/initiative-meridian-triage/deferred-work.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Events written in one save share a timestamp, so a job's history has no fixed order, and the detail panel shows only a readable line per event, not the model's raw output, model id or prompt version that explain an AI decision.

**Approach:** Give every `JobEvent` a per-job sequence number assigned when the event is appended, order history by it everywhere, and show a full history in the job detail panel: time (Europe/London), actor, event type, readable summary, and an expandable raw payload, with the model's raw output, model id, prompt version, latency and tokens shown for model events.

## Boundaries & Constraints

**Always:** Sequence is 1, 2, 3… per job in append order, unique per job (unique index on job id + sequence), assigned in the domain when the event is appended, never changed. The migration backfills existing rows in (timestamp, id) order. All history reads order by sequence. Raw payloads are shown as formatted JSON; message text in payloads is already the tenant's own text and is shown as text, never as HTML.

**Never:** No editing or deleting of events. No new event types.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Same timestamp | Two events appended in one save | Sequence 1 then 2; history in append order | No error expected |
| Existing data | Database with events before the migration | Backfilled sequences in timestamp order | No error expected |
| Model event | `ModelPrepared` | Shows model id, prompt version, latency, tokens and raw output | No error expected |
| Failure event | `ModelPreparationFailed` | Shows reason and attempts | No error expected |
| Script in text | message text contains `<script>` | Rendered as text | No error expected |

</frozen-after-approval>

## Code Map

Read `JobEvent`, `Job` (how events are appended), `GetJob`, the event configuration, existing migrations, and the job detail panel in `src/client/src/` first. Another agent is adding escalation events in parallel; do not reorder existing members, and put the migration in its own file.

- `src/Meridian.Domain/Jobs/JobEvent.cs`, `Job.cs` -- `Sequence` property, set on append
- `src/Meridian.Infrastructure/Persistence/Configurations/JobEventConfiguration.cs` + migration with backfill SQL
- `src/Meridian.Application/Review/GetJob.cs` and any history reads -- order by sequence; include raw payload and model fields
- `src/Meridian.Web/Api/` -- detail DTO adds sequence and payload
- `src/client/src/` -- history list with expandable raw payload
- `src/Meridian.Tests/` -- matrix rows; deferred-work item closed

## Tasks & Acceptance

**Execution:**
- [ ] Domain sequence -- AD-5
- [ ] Infrastructure mapping and backfill migration -- persistence
- [ ] Application and API history with payloads -- R17
- [ ] Client history view -- R16, R17
- [ ] Tests -- I/O matrix
- [ ] Mark the deferred-work entry about event ordering as resolved (append a line under it; do not delete it)

**Acceptance Criteria:**
- Given any seeded job, when its detail panel is opened, then every event is listed in sequence order and the model event shows its raw output.

## Implementation Notes

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror -nodeReuse:false` -- expected: build succeeds
- `cd src && dotnet test -nodeReuse:false` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: build succeeds
