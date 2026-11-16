---
title: 'OpenRouter prepares messages when a key is set'
type: feature
ticket: '9'
created: '2026-11-16'
status: built
baseline_revision: '49d6df494467a276c5d2bbd5b9fd61244e828201'
route: full
route_source: auto
risk: high
review: quick
review_source: pinned
lenses_ran: [inline]
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md'
  - '{project-root}/AI-DESIGN.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Only the recorded stub can prepare messages, so the product cannot be measured against a real model.

**Approach:** Add `OpenRouterTriageModel` behind the existing `ITriageModel` port: it sends the versioned system prompt and the message as quoted data to OpenRouter's OpenAI-compatible chat completions API, asks for a JSON object, and returns the raw text with model id, latency and token counts. It is selected only when `OPENROUTER_API_KEY` is set (or `Triage:Provider` is `openrouter`); otherwise the stub stays the default.

## Boundaries & Constraints

**Always:** Only `Meridian.Infrastructure` changes, plus configuration and tests. The adapter returns raw output unvalidated; validation, retry, timeout and fall-back stay in `PrepareJob` (do not duplicate them). The request contains only the system prompt from `Prompts/triage.v1.md` and a user turn that wraps the message text and received time as quoted data (AD-10), nothing about the sender. Use `HttpClient` via `IHttpClientFactory` and `System.Text.Json`; no new NuGet packages. Model id from config `Triage:Model`, default `anthropic/claude-haiku-5.5`; base URL from `Triage:OpenRouterBaseUrl`, default `https://openrouter.ai/api/v1`. The key is read from the `OPENROUTER_API_KEY` environment variable only, never from appsettings or logs. A non-2xx response, network error or empty choice returns `TriageModelResult.Failure("unavailable", ...)`; a refusal or moderation error is also `unavailable`.

**Never:** No key value in the repo, tests, logs or events. No live network call in the default `dotnet test` run.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| No key | `OPENROUTER_API_KEY` unset | Stub registered as `ITriageModel` | No error expected |
| Key set | `OPENROUTER_API_KEY` set | OpenRouter adapter registered; model id = `Triage:Model` | No error expected |
| 200 with JSON | Recorded HTTP response | `Output(raw content, model, triage.v1, latency, prompt/completion tokens)` | No error expected |
| 200 with JSON in code fences | Content wrapped in ```json fences | Fences stripped before returning raw | No error expected |
| 429 / 5xx | Recorded error response | `Failure("unavailable")` | No exception |
| Network error | Handler throws `HttpRequestException` | `Failure("unavailable")` | No exception |
| Cancelled | Token cancelled | `OperationCanceledException` propagates (PrepareJob maps it) | As PrepareJob does today |
| Request shape | Any message | Body has `model`, system + user messages, `response_format: {type: json_object}`, `temperature: 0`; user content contains the text inside a delimited block and no sender | Asserted in a test |

</frozen-after-approval>

## Code Map

- `src/Meridian.Application/Triage/ITriageModel.cs` -- the port and `TriageModelResult` factory methods; reuse, do not change
- `src/Meridian.Application/Triage/PrepareJob.cs` -- owns timeout, retry, validation; do not change
- `src/Meridian.Infrastructure/Triage/StubTriageModel.cs` -- reference for model id, prompt version and file loading conventions
- `src/Meridian.Infrastructure/Prompts/triage.v1.md` -- system prompt to send (already copied to output)
- `src/Meridian.Infrastructure/Triage/OpenRouterTriageModel.cs` (new) -- the adapter
- `src/Meridian.Infrastructure/Triage/TriageOptions.cs` (new) -- `Provider`, `Model`, `OpenRouterBaseUrl`
- `src/Meridian.Infrastructure/DependencyInjection.cs` -- choose stub or OpenRouter at startup; register the named `HttpClient` with `Authorization: Bearer <key>`, `HTTP-Referer` and `X-Title: meridian-triage` headers
- `src/Meridian.Web/appsettings.json` -- `Triage` section with defaults (no key)
- `src/Meridian.Tests/Infrastructure/OpenRouterTriageModelTests.cs` (new) -- a fake `HttpMessageHandler` returning recorded responses per matrix row; a DI test for both selection rows
- `src/Meridian.Tests/Infrastructure/Recorded/openrouter-*.json` (new) -- recorded HTTP bodies used by the tests

## Tasks & Acceptance

**Execution:**
- [x] `src/Meridian.Infrastructure/Triage/` -- adapter and options -- AD-1, AD-10
- [x] `src/Meridian.Infrastructure/DependencyInjection.cs`, `src/Meridian.Web/appsettings.json` -- selection and config -- RULE-12
- [x] `src/Meridian.Tests/Infrastructure/` -- matrix tests with recorded HTTP -- I/O matrix

**Acceptance Criteria:**
- Given no key, when the app starts, then preparation events show model id `stub`.
- Given `OPENROUTER_API_KEY=proxy-injected` in this cloud container (the network proxy injects the real key), when one Walsh message is prepared by a small console smoke run or a skipped-by-default test, then a `triage.v1` answer comes back and the event shows model id `anthropic/claude-haiku-5.5` with token counts.

## Implementation Notes

- `Meridian.Infrastructure` gains `<FrameworkReference Include="Microsoft.AspNetCore.App" />` for `IHttpClientFactory` and logging; this is the shared framework, not a NuGet package.
- Selection: `Triage:Provider` empty means OpenRouter when `OPENROUTER_API_KEY` is set, else the stub; `stub` forces the stub even with a key; `openrouter` without a key throws `InvalidOperationException` naming the variable when `ITriageModel` is first resolved; any other value throws.
- `AddMeridianInfrastructure` takes an optional `Func<string?>` key source (default: the environment variable) so the DI tests need not mutate process environment. The key only goes into the named client's `Authorization` header; logged headers are redacted.
- The adapter takes `IHttpClientFactory` and creates the named client per call (no long-lived handler in the singleton).
- User turn: `Received: <ISO-8601 UTC>` then the text between `<message>` and `</message>`; a `</message` inside the text is rewritten to `&lt;/message` so it cannot close the quoted block.
- The request also carries `max_tokens: 1024`: without it OpenRouter reserves credit for the model's full 65,536-token output and the live call answered 402 on the test key's balance.
- HttpClient's own timeout (cancellation without the caller's token) maps to `unavailable`; caller cancellation propagates. 200 bodies with an `error` object, `finish_reason` `content_filter`/`error`, a non-empty `refusal`, empty content, no choices, or non-JSON bodies are all `unavailable`. Logs carry the status code or a short reason only, never the body.
- `MessagesApiTests` pins `Triage:Provider=stub` so the default run never calls a model even if a developer has the key set.
- The live test (`OpenRouterLiveTests`, `Category=Live`) uses a `LiveFact` that skips unless `OPENROUTER_LIVE=1` and the key are set; it posts the Walsh message through the API and checks the `ModelPrepared` event.

## Plan Change Log

- 2026-11-16: added `max_tokens` to the request body (see Implementation Notes); the Request-shape row's listed fields are all still present.

## Review Triage Log

## Verification

**Commands:**
- `cd src && dotnet build -warnaserror` -- expected: build succeeds
- `cd src && dotnet test` -- expected: all tests pass, no network used
- `cd src && OPENROUTER_API_KEY=proxy-injected OPENROUTER_LIVE=1 dotnet test --filter Category=Live` -- expected: the one live smoke test passes (cloud container only)

**Results (2026-11-16):** build 0 warnings, 0 errors; `dotnet test` 123 passed, 1 skipped (the live test), no network; live smoke test passed against `anthropic/claude-haiku-5.5` (event `ModelPrepared`, token counts present).
