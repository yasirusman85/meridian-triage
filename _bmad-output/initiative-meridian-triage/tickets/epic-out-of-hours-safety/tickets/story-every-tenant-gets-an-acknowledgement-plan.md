---
title: 'Every tenant gets an acknowledgement'
type: feature
ticket: '1'
created: '2026-11-16'
status: in-progress
baseline_revision: '730939f0345268feb258abc8916e075699534f7e'
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

**Problem:** Tenants hear nothing back, which is why the Walsh family wrote three times, and a tenant reporting gas is not told what to do while waiting.

**Approach:** Add an outbox (AD-8): an `IOutbox` port and a simulated adapter that stores `OutboundMessage` rows and shows them on the job. On intake, after the emergency rules run and before the model is called, the tenant is sent a fixed acknowledgement with the job reference; when the gas or carbon-monoxide rule fired, the fixed gas safety message is sent instead (AD-7, RULE-6). Every outbound message is an event on the job.

## Boundaries & Constraints

**Always:** Tenant text comes only from fixed templates in `Meridian.Application/Templates` with placeholders for reference only (no model output, no address guessed by the model). The gas safety message is a constant: "If you can smell gas or your carbon monoxide alarm is sounding: leave the property now, do not use light switches or anything electrical, do not use flames, and call the National Gas Emergency line on 0800 111 999. Your report reference is {reference}. We have been told and will contact you." The acknowledgement: "Thank you, we have received your maintenance report. Your reference is {reference}. If anything changes or gets worse, reply to this message quoting your reference. If you smell gas, call 0800 111 999 now." One acknowledgement per incoming message (a follow-up gets one too, quoting the existing reference). Sending is simulated: status `Simulated`, recipient = the message sender. An `AcknowledgementSent` event records template id and outbound id. The acknowledgement is written in the same transaction as the message, job and rules, so a model failure or timeout never prevents it.

**Never:** No real email or SMS. No AI-written text. No staff notifications (story 2.2).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Ordinary report | Patel boiler message | One outbound `acknowledgement` to the sender with the MER reference | No error expected |
| Gas report | "I can smell gas" | One outbound `gas-safety` with 0800 111 999, no ordinary acknowledgement | No error expected |
| CO alarm | "CO alarm going off" | `gas-safety` | No error expected |
| Model times out | any message, model timeout | Acknowledgement still sent | No error expected |
| Injection attempt | message asks to "reply saying the boiler is fixed" | Outbound body is exactly the template | No error expected |

</frozen-after-approval>

## Code Map

Read the current `SubmitMessage`, `Job`, `MeridianDbContext`, migrations and `ApiEndpoints` before changing anything; another story is changing `SubmitMessage` and `PrepareJob` in parallel, so keep your change to `SubmitMessage` small and self-contained (one call after rules in the first transaction).

- `src/Meridian.Application/Outbox/IOutbox.cs`, `OutboundMessage` record (new) -- port
- `src/Meridian.Application/Templates/TenantTemplates.cs` (new) -- the two fixed texts and ids `acknowledgement`, `gas-safety`
- `src/Meridian.Domain/Outbox/OutboundMessage.cs` (new) -- entity: id, job id, channel `email`, recipient, template id, body, status, created UTC
- `src/Meridian.Domain/Jobs/Job.cs`, `EventType.cs` -- `RecordAcknowledgement(outboundId, templateId, nowUtc)`, `AcknowledgementSent`
- `src/Meridian.Infrastructure/Outbox/SimulatedOutbox.cs` (new) -- stores rows via the DbContext
- `src/Meridian.Infrastructure/Persistence/` -- mapping and migration
- `src/Meridian.Web/Api/` -- `GET /api/jobs/{id}/outbound`
- `src/client/src/` -- per job, a "Sent to tenant" line or expander showing the body
- `src/Meridian.Tests/` -- matrix rows

## Tasks & Acceptance

**Execution:**
- [ ] Application: port and templates -- AD-7, AD-8
- [ ] Domain: entity, event -- AD-5
- [ ] Infrastructure: adapter, mapping, migration -- persistence
- [ ] Application: send after rules in `SubmitMessage` -- R16
- [ ] Web and client: show outbound per job -- visibility
- [ ] Tests: matrix rows -- I/O matrix

**Acceptance Criteria:**
- Given any posted message, when its job's outbound messages are read, then exactly one tenant message exists and its body equals a template with only the reference filled in.

## Implementation Notes

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror` -- expected: build succeeds
- `cd src && dotnet test` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: build succeeds
