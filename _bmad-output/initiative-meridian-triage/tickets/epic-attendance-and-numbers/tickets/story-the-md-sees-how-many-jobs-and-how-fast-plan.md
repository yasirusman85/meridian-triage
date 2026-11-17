---
title: 'The MD sees how many jobs and how fast'
type: feature
ticket: '2'
created: '2026-11-17'
status: in-progress
baseline_revision: '8df848f1738a9830f80134ab994d42dfc7e98101'
route: full
route_source: auto
risk: low
review: ''
review_source: ''
lenses_ran: []
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The MD wants "how many jobs, how fast, that sort of thing", and Dawn said not to go mad on it.

**Approach:** One numbers page computed only from job events (AD-5), for the last 30 days and for all time, with no charts (user decision 2026-11-17).

## Boundaries & Constraints

**Always:** A `MetricsQuery` in Application reads events (and job ids/urgency/category only to group). Measures, each for "last 30 days" (jobs whose first event is within 30 days of now, from `IClock`) and "all time": jobs by category and by urgency (counts); time to first human look (first `JobPrepared` → first event with a non-system actor: approve, edit, reject, escalation acknowledge), median and 90th percentile; time to approval (`MessageReceived` → `JobApproved`), median and 90th percentile; time to attendance (`MessageReceived` → `JobAttended`), median and 90th percentile; 24-hour promise met (Emergency jobs attended within 24 h of first message, of Emergency jobs either attended or past 24 h); five-working-day promise met (non-emergency jobs attended within 5 working days Mon–Fri, same denominator rule); open jobs by status. Durations shown as hours and minutes. Measures with no data show "no data yet", never 0%. If the `JobAttended` event type does not exist yet, compute attendance measures as "no data yet" and keep the code ready for it (the attendance story is built in parallel and adds `JobAttended` and `SentToContractor` at the end of `EventType`).

**Never:** No charts, filters or exports. No numbers computed from current job fields instead of events (except grouping by category and urgency).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Empty database | no jobs | Every measure "no data yet" | No error expected |
| Counts | 5 jobs, 2 Heating, 1 Emergency | By-category and by-urgency counts match | No error expected |
| First human look | Prepared at 10:00, edited by rachel at 10:30 | 30 min | No error expected |
| Old job | first event 40 days ago | In all time, not last 30 days | No error expected |
| Promise unknown yet | Emergency 3 h old, not attended | Excluded from the 24 h denominator | No error expected |
| Promise broken | Emergency 30 h old, not attended | Counted as not met | No error expected |

</frozen-after-approval>

## Code Map

Read `JobEvent`, `EventType`, `IJobRepository`, an existing query like `ListQueue`/`ListNotifications`, `ApiEndpoints.cs`, `QueuePage.tsx`, `main.tsx` and `api.ts` first. Another story runs in parallel and edits `EventType.cs` (adds members at the end), `Job.cs` partials and the queue page; keep your changes to new files plus small additions.

- `src/Meridian.Application/Metrics/MetricsQuery.cs` (new) and a repository method to read the events and job group fields it needs
- `src/Meridian.Web/Api/MetricsEndpoints.cs` (new) -- `GET /api/metrics`
- `src/client/src/NumbersPage.tsx` (new) -- two columns (last 30 days, all time), simple tables; a link "Numbers" in the page header
- `src/Meridian.Tests/` -- matrix rows with hand-built events and a fake clock

## Tasks & Acceptance

**Execution:**
- [x] Application query and repository read
- [x] Web endpoint
- [x] Client page and header link
- [x] Tests for every matrix row

**Acceptance Criteria:**
- Given the six client messages posted and one job approved by Rachel, when the numbers page opens, then counts by category and urgency match the queue and time to first human look shows a value.

## Implementation Notes

- `MetricsQuery` (Application/Metrics) reads `IJobRepository.ListWithEventsAsync` and computes everything from events. Only category and urgency are read from current job fields, for grouping. Open jobs by status are derived from the status-changing events.
- Attendance is not in `EventType` yet, so `JobAttended` and `SentToContractor` are matched by name (`MetricsQuery.AttendedEventName`). `MetricsQuery.AttendanceSupported` is false until the enum member exists: attendance measures and both promises then show "no data yet". The constructor takes the flag, so tests can exercise the promise rules.
- First human look starts at the first `JobPrepared` and ends at the first `JobApproved`, `JobRejected`, `JobEdited`, `UrgencySetByPerson` or `EscalationAcknowledged` whose actor is not `system`. Urgency changes by a person count as edits.
- Median is the mean of the middle pair for an even count. The 90th percentile is nearest-rank. Working days are Monday to Friday in UTC with no bank holidays, and the five-day deadline keeps the time of day.
- The page is at `/numbers`, served by the existing SPA fallback. `main.tsx` picks the page from the path, and the queue header links to it.

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror -nodeReuse:false` -- expected: build succeeds
- `cd src && dotnet test -nodeReuse:false` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: build succeeds
