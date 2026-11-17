---
title: 'Contractors mark jobs attended'
type: feature
ticket: '1'
created: '2026-11-17'
status: in-progress
baseline_revision: '8df848f1738a9830f80134ab994d42dfc7e98101'
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

**Problem:** An approved job never reaches a contractor and nobody can tell when one has attended, so the 24-hour emergency promise (measured to attendance, DECISIONS D3) cannot be checked.

**Approach:** Approving a job also sends it to its contractor (user decision 2026-11-17): a simulated outbound message with a single-job link (AD-11). Opening the link shows the job and a "Mark attended" button that moves it to Attended. An emergency not attended within 24 hours of its first message shows an alert at the top of the queue suggesting the fallback contractor; a person picks one and re-sends.

## Boundaries & Constraints

**Always:** `Approved → SentToContractor` happens in the same transaction as approval, through a new `Job.SendToContractor(contractorId, token, actor, now)` that appends `SentToContractor` (contractor id, outbound id). The token is 32 random bytes, URL-safe base64, stored hashed (SHA-256) on the job; the raw token appears only in the outbound message body and the link. The link is `/c/<token>`; `GET /api/contractor-jobs/<token>` returns only that job's reference, address, category, summary and contractor name; `POST /api/contractor-jobs/<token>/attended` moves `SentToContractor → Attended`, appends `JobAttended` (actor `contractor:<id>`), and invalidates the token. Wrong, used or expired tokens give 404 (no detail). The contractor message uses a fixed template in `Meridian.Application/Templates/ContractorTemplates.cs` (reference, address, category, English summary, link); the English summary is model output, so it is shown only to the contractor as job information, never to a tenant (RULE-5 is about tenant text). Late check: the existing escalation worker tick also finds Emergency jobs in `SentToContractor` whose first message is more than 24 hours old and not yet flagged, appends `AttendanceOverdue` and sets a flag; the queue shows these first with "Overdue — send fallback". `POST /api/jobs/{id}/resend` (with `X-Acting-As`) takes a contractor id, issues a new token (old one invalid), sends a new message, appends `SentToContractor` again, and clears the overdue flag. Time only from `IClock`.

**Never:** No real email/SMS. No contractor accounts or login. No automatic choice of fallback contractor.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Approve sends | Prepared job with contractor, approved | Status SentToContractor; contractor outbound with link; events JobApproved then SentToContractor | No error expected |
| Contractor opens link | valid token | Job summary shown, no other jobs | No error expected |
| Mark attended | valid token | Attended; JobAttended; token invalid | No error expected |
| Used token | already attended | 404 | Nothing changes |
| Wrong token | random string | 404 | Nothing changes |
| Overdue emergency | Emergency, SentToContractor, first message 25 h ago | AttendanceOverdue once; queue alert | Tick twice → one event |
| Not overdue | Urgent job 30 h old | No alert | No error expected |
| Resend | Overdue job, person picks fallback contractor | New link, old link 404, flag cleared | No error expected |

</frozen-after-approval>

## Code Map

Read `Job.cs` (Approve at line ~459, `Append` path), `Job.Escalation.cs`, `JobStatus`, `ReviewJob.cs`, `EscalationService.cs`, `EscalationWorker.cs`, `IOutbox`/`SimulatedOutbox`, `ReviewEndpoints.cs`, `QueuePage.tsx`, `JobDetailPanel.tsx`, `api.ts`, `main.tsx` first.

- `src/Meridian.Domain/Jobs/Job.Attendance.cs` (new, partial) -- token hash, overdue flag, `SendToContractor`, `MarkAttended`, `FlagAttendanceOverdue`; events added at the end of `EventType.cs`: `SentToContractor`, `JobAttended`, `AttendanceOverdue`
- `src/Meridian.Application/Review/ReviewJob.cs` -- approve then send in one transaction
- `src/Meridian.Application/Attendance/` (new) -- `ContractorJobLink` (token create/hash), `GetContractorJob`, `MarkAttended`, `ResendToContractor`, `CheckOverdueAttendance`
- `src/Meridian.Application/Templates/ContractorTemplates.cs` (new)
- `src/Meridian.Application/Escalation/EscalationService.cs` or the worker -- call `CheckOverdueAttendance` each tick
- `src/Meridian.Infrastructure/Persistence/` -- mapping, one migration
- `src/Meridian.Web/Api/AttendanceEndpoints.cs` (new) -- the three endpoints; SPA route `/c/:token` served by the existing fallback
- `src/client/src/ContractorPage.tsx` (new) -- shown when the path starts with `/c/`; detail panel shows the contractor link for staff; queue shows overdue alert and resend
- `src/Meridian.Tests/` -- matrix rows (domain, application with fake clock, API)

## Tasks & Acceptance

**Execution:**
- [ ] Domain attendance members and events
- [ ] Application send-on-approve, link, attended, resend, overdue check
- [ ] Infrastructure mapping and migration
- [ ] Web endpoints and worker hook
- [ ] Client contractor page, staff link display, overdue alert and resend
- [ ] Tests for every matrix row

**Acceptance Criteria:**
- Given the Walsh emergency approved by Rachel, when the contractor opens the link and marks it attended, then the job is Attended and its history shows JobApproved, SentToContractor and JobAttended in order.

## Implementation Notes

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror -nodeReuse:false` -- expected: build succeeds
- `cd src && dotnet test -nodeReuse:false` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: build succeeds
