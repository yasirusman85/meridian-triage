---
title: 'Refactor sweep'
type: refactor
ticket: '10'
created: '2026-11-17'
status: built
baseline_revision: '80edd6b182911b9e595beeebad1031318d98be97'
route: full
route_source: auto
risk: low
review: quick
review_source: pinned
lenses_ran: [inline]
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md'
  - '{project-root}/README.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The stories left two loose ends from their reviews (two separate staff lists that can drift apart; reviewers open an empty queue), and the codebase has the usual small leftovers of parallel work.

**Approach:** A short clean-up pass: one staff directory, the six client messages loaded on first start through the real pipeline (user decision 2026-11-17), and a tidy of dead code and duplicated constants, with no behaviour change beyond those two items.

## Boundaries & Constraints

**Always:** One staff configuration section (`Staff`: key, display name, phone, role primary/backup) used by both review (acting-as) and escalation; `Escalation` keeps only `WindowMinutes`. Demo seed: when `Demo:SeedClientMessages` is true (default true in `appsettings.json`) and the database has no messages, after migrations and reference seed, the six client messages from `docs/input/client-input.md` are submitted through the same `SubmitMessage` use case, in their original order, with received times set to the most recent past occurrence of the weekday and time each message shows (Europe/London), so the Friday-evening Walsh messages land in winter and stay in order. Senders are the masked addresses as given (e.g. `j.okafor@example.com`). The stub provider has recorded answers for them, so the queue fills without a key. Remove code that nothing calls, duplicated template-id constants between server and client where the client can read a flag from the API instead (e.g. an `audience` field on outbound messages: `tenant`, `staff`, `contractor`), and stale comments that mention "later stories". All tests stay green; no test is deleted unless the code it covers is deleted.

**Never:** No new features. No change to rules, prompts, contracts or eval scoring.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Fresh database, demo on | first start | Six messages processed: five jobs (Joy two, Walsh one with two follow-ups, Nowak, Patel), acknowledgements sent | No error expected |
| Existing messages | second start | Nothing seeded again | No error expected |
| Demo off | `Demo:SeedClientMessages=false` | Empty queue | No error expected |
| Staff in one place | acting-as list and escalation recipients | Same two people from `Staff` | No error expected |
| Outbound audience | tenant ack, staff SMS, contractor link | `audience` tenant/staff/contractor in API; client filters by it | No error expected |

</frozen-after-approval>

## Code Map

Read `Program.cs`, `DependencyInjection.cs` (migrate and seed), `SeedData.cs`, `SubmitMessage.cs`, `StaffDirectory.cs`, `EscalationOptions.cs`, `EscalationConfiguration.cs`, `appsettings.json`, outbound DTOs, `SentToTenant.tsx`, `NotificationsPanel.tsx`, `JobDetailPanel.tsx`, and the recorded stub answers in `src/Meridian.Infrastructure/Triage/Recorded/` first.

- `src/Meridian.Application/Staff/` (new) or reuse `Review/StaffDirectory.cs` -- one directory; update escalation to use it
- `src/Meridian.Web/appsettings.json` -- `Staff` with phone and role; `Escalation:WindowMinutes`; `Demo:SeedClientMessages`
- `src/Meridian.Infrastructure/` or `src/Meridian.Web/` -- `DemoSeed` that submits the six messages through `SubmitMessage` with the clock set per message
- Outbound DTO `audience`; client filters by it
- Tests: matrix rows; existing escalation/review tests updated for the single staff list

## Tasks & Acceptance

**Execution:**
- [ ] One staff directory
- [ ] Demo seed of the six client messages
- [ ] Outbound audience field and client filtering
- [ ] Remove dead code and stale comments
- [ ] Tests for every matrix row; whole suite green

**Acceptance Criteria:**
- Given a fresh clone, when `./run.sh` starts the app, then the queue shows the six client messages as five prepared jobs without any API key.

## Implementation Notes

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror -nodeReuse:false` -- expected: build succeeds
- `cd src && dotnet test -nodeReuse:false` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: build succeeds
