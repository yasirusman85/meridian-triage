# Meridian maintenance triage

A system for Meridian Property Services that reads every maintenance message as it arrives, prepares the job (what it is, how urgent, which property, which contractor, and why), and puts it in front of a person to approve. Fixed rules catch the clear emergencies, the AI reads what rules cannot and may only ever raise urgency, and nothing reaches a contractor until a person has approved it.

## Run it

**You need:** .NET 8 SDK and Node.js 20.19+ or 22.12+. No API key, account or database server.

```bash
./run.sh
```

On Windows:

```powershell
./run.ps1
```

Then open http://localhost:5080. The script checks the Node and .NET versions, builds the React client into the web app, applies the database migrations (SQLite, `src/Meridian.Web/meridian.db`) and starts the app. Paste a message (sender and text) into the form to see it prepared and queued.

**Tests:**

```bash
cd src && dotnet test
```

**With a real model (optional):** set an OpenRouter key and the app uses OpenRouter instead of the stub. The model defaults to `anthropic/claude-haiku-5.5`; change it with `Triage__Model`.

```bash
OPENROUTER_API_KEY=sk-or-... ./run.sh
```

## Model access

The AI sits behind one interface, `ITriageModel` (`src/Meridian.Application/Triage/ITriageModel.cs`), with two implementations:

- **Stub (default):** `StubTriageModel` returns recorded answers for known messages. **The recorded answers were written by hand** in the `triage.v1` format (`src/Meridian.Infrastructure/Triage/Recorded/`). A message with no recorded answer is treated as "model unavailable" and goes to the Unsure category for a person, which is also how the real system behaves when the model is down.
- **OpenRouter:** `OpenRouterTriageModel`, used when `OPENROUTER_API_KEY` is set. Tested against OpenRouter during development with `anthropic/claude-haiku-5.5`.

Everything the code trusts from a model goes through one validator; invalid output gets one retry, then the job goes to Unsure. See [AI-DESIGN.md](AI-DESIGN.md).

## What is done

- **Intake:** a pasted message is stored unchanged and gets a `MER-` job reference.
- **Emergency rules:** gas, carbon monoxide, water through a ceiling, and no heating from October to April (UK time) make a job an emergency before the model is called.
- **AI preparation:** category, urgency, English summary and explanation, through the model interface, with a 20-second timeout, one retry and a fall-back to Unsure.
- **Splitting and follow-ups:** one email with several problems becomes several jobs, each matched to a known property; a repeat message about an open job at the same property is attached to it as a follow-up.
- **Urgency only goes up** unless a person lowers it, with the reason recorded.
- **Tenant acknowledgement** for every message, quoting the reference of each job it made or joined, and a fixed gas safety message (National Gas Emergency, 0800 111 999) for gas reports. Sending is simulated and visible on the job. A message stored without a job (the app stopped mid-intake) still gets one, with its acknowledgement, at the next start.
- **Event log:** every step of every job, including the model's raw output, model id and prompt version.
- **Review queue page** in React, ordered by urgency then age.
- **Eval set:** 52 labelled cases in [`eval/`](eval/README.md).

*This list is updated as the remaining stories land; see "Still in progress" below.*

## Still in progress

- The built-in contractor map.
- Approve, edit and reject in the queue.
- A job history page.
- The eval runner and its recorded results.
- Escalation to Rachel, then the backup after 30 minutes.
- Contractor attendance links and the MD's numbers page.

## What I cut, and why

- **WhatsApp intake:** Meridian has no process for it yet; the intake is built so another channel can be added.
- **Reading photos:** the text already marks the serious cases; image analysis adds cost and risk for little gain.
- **Live email inbox, Meridian's job system, real SMS and email:** integration work, simulated here so it runs anywhere.
- **Login and roles:** an "acting as" switch stands in, recorded as the actor on each event.
- **The system learning from corrections by itself:** corrections are recorded for people to review; nothing changes the AI automatically.

Every decision the input left open, and why, is in [DECISIONS.md](DECISIONS.md).

## Where things are

| Path | What it holds |
|---|---|
| `src/` | The application: `Meridian.Domain`, `Meridian.Application`, `Meridian.Infrastructure`, `Meridian.Web`, `Meridian.Eval`, `Meridian.Tests`, and `client/` (React) |
| `docs/` | BMAD artifacts: `bmad/brief.md`, `bmad/prd.md`, `bmad/architecture.md`, the client input, and BMAD research notes |
| `_bmad-output/` | The full BMAD run: brief, PRD, initiative, architecture, epics, stories (`tickets.toml`) and build plans, each with its decision log (`.memlog.md`) |
| `eval/` | Labelled eval cases, how correctness is defined, and results |
| `conversations/` | Every AI conversation, exported unedited (email redacted) |
| `AI-DESIGN.md` | Design of the AI component, cost and latency arithmetic |
| `DECISIONS.md` | Assumptions and decisions log |

## How it was built

- **Method:** BMAD Method, current version (the V7 preview line; installed version reports 6.13.0-next), following docs.bmad-method.org rather than the older masterclass video. Notes on what changed since the video are in [`docs/bmad-research/bmad-notes.md`](docs/bmad-research/bmad-notes.md). The flow was: Mary (analyst) for the brief, John (PM) for the PRD, Sofia (product owner) for requirements, epics and stories, the architecture skill for the architecture, and `bmad-build` for each story.
- **AI tools:** Claude Code only, for the planning conversations, writing the code, reviewing it, and drafting these documents. The full conversations are in `conversations/`.
- **Time spent:** about 4–5 hours of my own working time.
