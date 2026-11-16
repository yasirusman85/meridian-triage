---
title: 'Emergencies reach Rachel, then the backup'
type: feature
ticket: '2'
created: '2026-11-16'
status: in-progress
baseline_revision: 'c70a9c3f77cbbf37035e0c9fcb2152bcfa019927'
route: full
route_source: auto
risk: high
review: ''
review_source: ''
lenses_ran: []
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md'
  - '{project-root}/_bmad-output/initiative-meridian-triage/tickets/epic-out-of-hours-safety/tickets/story-every-tenant-gets-an-acknowledgement-plan.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** An emergency on a Friday night sits in the queue until someone opens it, which is exactly the failure Dawn described.

**Approach:** A background worker (AD-9) checks every minute: an Emergency job not yet acknowledged notifies Rachel at once, then Dawn (the backup) when Rachel has not acknowledged within 30 minutes, then marks it escalation-failed when Dawn has not acknowledged within a further 30 minutes. Staff acknowledge from the queue. Notifications go through the existing outbox (AD-8) as simulated staff messages and are visible in a notifications panel. The worker also runs `RecoverOrphanMessages` with its default minimum age.

## Boundaries & Constraints

**Always:** `EscalationState` on the job: `None → NotifiedRachel → NotifiedBackup → Failed`, and `Acknowledged` from either notified state (AD-6: a separate field, not a lifecycle state). Each step is an event (`EscalationNotified` with recipient, `EscalationAcknowledged` with actor, `EscalationFailed`) and, for notifications, an `OutboundMessage` with channel `sms`, recipient the staff member, a fixed template (reference, address, category, rules) and status `Simulated`. Time only from `IClock`; the worker is idempotent (running twice in a minute sends nothing twice). A job raised to Emergency later (follow-up) starts escalation then. Staff, phone numbers and the 30-minute window come from config (`Escalation:Primary`, `Escalation:Backup`, `Escalation:WindowMinutes`), seeded as Rachel and Dawn with fictional numbers. Acknowledging is `POST /api/jobs/{id}/acknowledge` with `X-Acting-As` (rachel or dawn; same header the review story uses). Escalation-failed jobs sort to the top of the queue, then Emergency, then by age.

**Never:** No real SMS. No escalation for Urgent or Routine. No AI involvement.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| New emergency | Emergency job, state None | Rachel notified at next tick | No error expected |
| Rachel silent | +30 min, NotifiedRachel | Dawn notified | No error expected |
| Both silent | +60 min, NotifiedBackup | Failed; top of queue | No error expected |
| Rachel acknowledges | NotifiedRachel, ack by rachel | Acknowledged; no Dawn message | No error expected |
| Dawn acknowledges | NotifiedBackup, ack by dawn | Acknowledged | No error expected |
| Tick twice | same minute | No duplicate notification | No error expected |
| Urgent job | Urgent | Never escalated | No error expected |
| Follow-up raises | Urgent job raised to Emergency | Escalation starts | No error expected |
| Ack non-emergency | Routine job | 409 | Nothing changes |

</frozen-after-approval>

## Code Map

Read current `Job`, `IOutbox`, `SimulatedOutbox`, `OutboundMessage`, `RecoverOrphanMessages`, `ListQueue`, `Program.cs`, DTOs and `QueuePage.tsx` first. Another agent adds review actions (approve/edit/reject, acting-as switch, job detail) in parallel; keep Job changes to new members, add your own staff template text in `Meridian.Application/Templates`, and put your migration in its own file.

- `src/Meridian.Domain/Jobs/` -- `EscalationState`, methods `NotifyEscalation(recipient, now)`, `AcknowledgeEscalation(actor, now)`, `FailEscalation(now)`, timestamps for each step
- `src/Meridian.Application/Escalation/EscalationService.cs` (new) -- one `TickAsync` that does every due step
- `src/Meridian.Application/Templates/StaffTemplates.cs` (new)
- `src/Meridian.Web/Workers/EscalationWorker.cs` (new) -- `BackgroundService`, every 60 s calls `EscalationService.TickAsync` and `RecoverOrphanMessages`
- `src/Meridian.Infrastructure/Persistence/` -- mapping and migration
- `src/Meridian.Web/Api/` -- acknowledge endpoint; `GET /api/notifications` (staff outbound, newest first)
- `src/client/src/` -- notifications panel; escalation badge and acknowledge button in the queue
- `src/Meridian.Tests/` -- matrix rows with a fake clock

## Tasks & Acceptance

**Execution:**
- [ ] Domain escalation state and events -- AD-6
- [ ] Application escalation service and staff templates -- R18, R19
- [ ] Web worker, endpoints -- AD-9
- [ ] Infrastructure mapping, migration, config defaults -- persistence
- [ ] Client panel, badge, acknowledge -- visibility
- [ ] Tests -- I/O matrix

**Acceptance Criteria:**
- Given the Walsh emergency with a fake clock, when ticks run at +0, +30 and +60 minutes with no acknowledgement, then Rachel then Dawn are notified and the job is marked escalation-failed, each step in its history.

## Implementation Notes

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror -nodeReuse:false` -- expected: build succeeds
- `cd src && dotnet test -nodeReuse:false` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: build succeeds
