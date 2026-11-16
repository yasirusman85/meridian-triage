---
title: 'The eval set runs through the real pipeline'
type: feature
ticket: '8'
created: '2026-11-16'
status: in-progress
baseline_revision: 'a81d0b389ff398fe7a59b11165a0b066c0af49a5'
route: full
route_source: auto
risk: medium
review: ''
review_source: ''
lenses_ran: []
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md'
  - '{project-root}/eval/README.md'
  - '{project-root}/AI-DESIGN.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** There are 52 labelled cases but no way to run them, so nobody can say how the AI component scores or where it fails.

**Approach:** `Meridian.Eval` (AD-12) runs every case in `eval/cases/*.json` through the same Application intake pipeline the product uses (rules, model, validator, merge, splitting), against a throwaway SQLite database, with the configured `ITriageModel` (stub by default, OpenRouter when `OPENROUTER_API_KEY` is set). It scores each case against its expected labels, classes errors by harm, and writes machine and human results.

## Boundaries & Constraints

**Always:** Same pipeline code as the app, not a re-implementation; a fresh database per run with the seed loaded; cases processed in id order so follow-up cases (`follow_up_of`) see their earlier case. Scoring, per `eval/README.md`: emergency recall (every expected Emergency issue ends Emergency), category accuracy over expected maintenance issues, urgency exact-match and "under" count (under = critical when expected Emergency), issue-count match, property match per the README's rule, vulnerability flag. Non-maintenance cases (`is_maintenance: false`) are correct when they produce exactly one Unsure job and no Emergency (the pipeline never drops a message; the README is updated to say this). Each failure is listed with case id, field, expected, actual and harm class (critical, tolerable, minor). Output: `eval/results/<yyyy-MM-dd>-<model-id-sanitised>.json` and `.md` (summary table, thresholds pass/fail: emergency recall 100%, category accuracy ≥85%, then failures grouped by harm). Exit code 0 when thresholds pass, 1 otherwise. A per-case latency and token total are included when the model reports them; a cost estimate uses the per-token prices passed in config (defaults from AI-DESIGN.md: $0.10 input, $0.50 output per million).

**Never:** No change to product behaviour to make the eval pass. No network in `dotnet test`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Stub run | no key | Results file named `...-stub`; cases with no recorded answer score as Unsure via the real fallback | No error expected |
| OpenRouter run | key set | Results file named after the model id | Model failures recorded per case |
| Missed emergency | expected Emergency, got Urgent | Listed under critical; recall < 100%; exit 1 | No crash |
| Non-maintenance | spam case | Correct if one Unsure job, not Emergency | No error expected |
| Bad case file | JSON missing `expected` | Run stops with the file name | Exit 2 |

</frozen-after-approval>

## Code Map

Read `SubmitMessage`, the DI setup in `Meridian.Infrastructure/DependencyInjection.cs`, `Meridian.Web/Program.cs` (for how services and migrations are wired), `eval/README.md` and a few `eval/cases/*.json` first. Do not change product code except to expose what the runner needs (prefer none).

- `src/Meridian.Eval/Program.cs` -- CLI: `dotnet run --project src/Meridian.Eval -- [--cases eval/cases] [--out eval/results]`
- `src/Meridian.Eval/` -- case loader, runner (builds a service provider with the Infrastructure DI and a temp SQLite file, migrates, seeds), scorer, report writers
- `eval/README.md` -- non-maintenance scoring line; how to run
- `src/Meridian.Tests/Eval/` -- scorer unit tests per matrix row using hand-built results; one test that runs the runner on 3 cases with the stub

## Tasks & Acceptance

**Execution:**
- [x] Runner over the real pipeline -- AD-12
- [x] Scorer and harm classes -- eval README
- [x] JSON and Markdown reports, exit codes -- results
- [x] README update -- docs
- [x] Tests -- I/O matrix
- [x] Run it once with the stub and commit the results files

**Acceptance Criteria:**
- Given the repo, when `dotnet run --project src/Meridian.Eval` runs, then `eval/results/<date>-stub.md` shows emergency recall, category accuracy, and every failure with its harm class.

## Implementation Notes

- No product code changed. The runner composes `AddMeridianInfrastructure` plus the same use cases `Meridian.Web/Program.cs` registers, replaces only `IClock` (set to each case's `received_at`) and wraps the configured `ITriageModel` in a pass-through recorder (latency, tokens, the validated answer for `vulnerability_mentioned` and `property_text`, which jobs do not store).
- Follow-up issues have no job of their own; they are scored as the message read alone (README rule): `PrepareJob.CategoryFor` on the issue and max(rules, model urgency). When several issues attach to one job the result collapses them, so they are assigned to follow-up jobs in order.
- Property scoring reuses `PropertyResolver.Tokenize` (unit words dropped, abbreviations expanded); documented in the README.
- Harm for fields AD-12 does not name: lower non-emergency urgency, vulnerability, property, issue count and follow-up are minor; a non-maintenance message at Emergency is tolerable. Injection failures are not reported separately (README says so).
- All cases share one database, so unrelated cases at the same property within 7 days can group, as in the product.

## Plan Change Log

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror` -- expected: build succeeds
- `cd src && dotnet test` -- expected: all tests pass
- `dotnet run --project src/Meridian.Eval` -- expected: results written (exit code may be 1 with the stub; record it)

**Results (2026-11-16):** build -warnaserror clean; `dotnet test` 420 passed, 1 skipped (live OpenRouter); stub eval exit code 1: emergency recall 77.3% (17/22), category accuracy 18.5% (10/54), 109 failures (5 critical, 1 tolerable, 103 minor), 46 of 52 cases without a recorded answer.
