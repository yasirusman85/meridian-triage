---
title: 'A person approves, edits or rejects each job'
type: feature
ticket: '6'
created: '2026-11-16'
status: built
baseline_revision: 'c70a9c3f77cbbf37035e0c9fcb2152bcfa019927'
route: full
route_source: auto
risk: medium
review: quick
review_source: pinned
lenses_ran: [inline]
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Jobs are prepared and queued, but no person can approve, correct or reject them, so nothing can move towards a contractor and RULE-1 cannot be shown working.

**Approach:** Review actions through the job state machine (AD-6): approve, edit (category, urgency, contractor, property) and reject, each recorded with the acting person and the value before and after. An "acting as" switch (Rachel, Dawn) in the UI sets the actor (no login). A job detail panel shows the explanation, English summary, original messages, rules that applied, the suggested contractor and the actions.

## Boundaries & Constraints

**Always:** Transitions only `Prepared → Approved` and `Prepared → Rejected` here; illegal transitions return 409 problem details. Editing is allowed while Prepared; each changed field is one `JobEdited` event with field, before, after, actor; urgency changes use `SetUrgencyByPerson` (a reason is required to lower it); a category change re-runs `ContractorLookup` (and clears the suggestion when set to Unsure), unless the person also picked a contractor. Reject needs a reason. Approve requires a category other than Unsure and a contractor; otherwise 409 with what is missing. Actor is sent in an `X-Acting-As` header with values `rachel` or `dawn` (staff list in config/seed); a missing or unknown actor is 400. Every action is one transaction with its events (AD-5).

**Never:** No sending to contractors (story 3.1). No login. No automatic approval.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Approve | Prepared, Heating, contractor set | Approved; `JobApproved` with actor | No error expected |
| Approve Unsure | Prepared, Unsure | 409 "set a category" | Nothing changes |
| Edit category | Unsure → Plumbing | `JobEdited` category; contractor re-suggested | No error expected |
| Lower urgency without reason | Emergency → Urgent, no reason | 400 | Nothing changes |
| Lower urgency with reason | Emergency → Urgent, reason | `UrgencySetByPerson` | No error expected |
| Reject | Prepared, reason "duplicate" | Rejected; `JobRejected` with reason | No error expected |
| Approve twice | Approved | 409 | Nothing changes |
| Edit after approve | Approved | 409 | Nothing changes |
| No actor | header missing | 400 | Nothing changes |

</frozen-after-approval>

## Code Map

Read current `Job`, `JobStatus`, `ContractorLookup`, `ListQueue`, `ApiEndpoints`, DTOs and `QueuePage.tsx` first. Another agent adds escalation in parallel (new background worker, `EscalationState` on Job, notifications); keep your Job changes to new methods and avoid reordering existing members.

- `src/Meridian.Domain/Jobs/Job.cs`, `EventType.cs` -- `Approve(actor, now)`, `Reject(actor, reason, now)`, `Edit...` methods; events `JobApproved`, `JobRejected`, `JobEdited`
- `src/Meridian.Application/Review/` (new) -- `ReviewJob` use case (approve, reject, edit), `GetJob` query (detail incl. messages, rules, contractor, events summary)
- `src/Meridian.Web/Api/` -- `GET /api/jobs/{id}`, `POST /api/jobs/{id}/approve`, `/reject`, `PATCH /api/jobs/{id}`; staff list `GET /api/staff`
- `src/client/src/` -- acting-as switch (persisted per viewer), job detail panel with actions and edit form
- `src/Meridian.Tests/` -- matrix rows (domain + API)

## Tasks & Acceptance

**Execution:**
- [x] Domain transitions and events -- AD-6, R14, R15
- [x] Application review use case and detail query -- R13, R14
- [x] Web endpoints and staff list -- API
- [x] Client switch, detail panel, actions -- R13
- [x] Tests -- I/O matrix

**Acceptance Criteria:**
- Given the Nowak job (Unsure), when Rachel sets it to General with a contractor and approves it, then it is Approved and its events show the edits and approval with actor rachel.

## Implementation Notes

- The job's contractor stays in `SuggestedContractorId`/`SuggestedContractorRule` (no migration): a person's choice gets the new `ContractorRule.Person`. Approve requires that contractor to be set.
- Domain: new `Job` methods only (`MissingForApproval`, `Approve`, `Reject`, `EditCategory`, `EditProperty`, `ChooseContractor`, `ClearContractor`, `EditUrgency`), inserted before the private helpers; existing members untouched. Illegal transitions and missing approval fields throw `JobStateException` (carries `Missing`). Event types `JobApproved`, `JobRejected`, `JobEdited` appended to `EventType`.
- Urgency edits go through `EditUrgency` → `SetUrgencyByPerson` (event `UrgencySetByPerson`, not `JobEdited`); raising without a reason records "raised in review".
- Contractor re-suggestion: a category change re-runs `ContractorLookup` (system `ContractorSuggested`); when the lookup gives none (e.g. Unsure) the contractor is cleared with a `JobEdited` (field contractor, `cause`) by the person. A property change also re-runs it unless the current contractor was a person's choice. A contractor in the same edit always wins.
- `ReviewJob` validates everything before touching the job; domain exceptions propagate out of the unit of work so nothing is saved. Order of checks: actor (400) → job exists (404) → Prepared (409) → fields (400).
- Staff list: config section `Staff` in `appsettings.json` (Rachel, Dawn), with the same default in `StaffDirectory`; header value is matched case-insensitively and recorded as the lowercase id.
- Added `GET /api/contractors` and `GET /api/properties` (not in the code map) so the edit form can offer choices.
- Client: acting-as switch remembered in `localStorage` (`meridian.actingAs`); clicking a reference opens the detail panel with approve, edit and reject.

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror -nodeReuse:false` -- expected: build succeeds
- `cd src && dotnet test -nodeReuse:false` -- expected: all tests pass
- `cd src/client && npm run build` -- expected: build succeeds
