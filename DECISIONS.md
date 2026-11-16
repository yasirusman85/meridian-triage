# Decisions and assumptions

Every point where the client input did not say what to do. Quotes are from `docs/input/client-input.md`. Each decision is also recorded where it was made: the brief's, PRD's, initiative's and architecture's `.memlog.md` files under `_bmad-output/`.

---

### D1. "Automated" against "someone checks it first"

- **The gap:** "What I want is for this to be automated" against "nothing goes out to a contractor without someone checking it first".
- **What I decided:** The AI prepares every job and a person approves every job. Nothing reaches a contractor without approval, at any urgency (PRD RULE-1).
- **The alternative:** Let the AI dispatch low-risk routine jobs (a dripping tap, a bulb) on its own and keep human approval for the rest.
- **Why this one:** Dawn was explicit about the check, and the five-working-day promise leaves time for a person to approve routine work on the next working day. What is automated is the reading and preparing, which is where Rachel's time goes.
- **What would change my mind:** Evidence that approvals pile up past the five-day promise, or a month of routine jobs approved unchanged above about 95%.
- **Blast radius:** The review use case and the job state machine (AD-6): an auto-approve path would be a new transition with its own rule and event.

### D2. Who gets an emergency out of hours

- **The gap:** "Emergencies have to be dealt with straight away", but Rachel is the only coordinator and "She's off sick, nothing moves".
- **What I decided:** Rachel is notified first. If she has not acknowledged within 30 minutes, a named backup (Dawn in the seed data) is notified. An emergency neither acknowledges is marked escalation-failed and held at the top of the queue (R18, R19).
- **The alternative:** A rota-based on-call role rather than Rachel by name.
- **Why this one:** It keeps Rachel in charge, as Dawn describes, with a safety net that removes the single point of failure.
- **What would change my mind:** Meridian already running an out-of-hours rota, or Rachel not wanting to be woken.
- **Blast radius:** Escalation service and seed data only; the recipients are configuration.

### D3. What "dealt with" in 24 hours means

- **The gap:** "We promise 24 hours on emergencies" does not say whether that means answered, approved or fixed.
- **What I decided:** For emergencies the promise is measured from the tenant's first message to a contractor attending, with an alert to send a fallback contractor when the time is running out (R20, R21). The contractor marks attendance through a single-job link (AD-11).
- **The alternative:** Measure to approval, which the system fully controls.
- **Why this one:** The promise is about the tenant, and a job approved but not attended has not kept it.
- **What would change my mind:** The tenancy agreement wording, which we do not have, defining it as a response rather than attendance.
- **Blast radius:** The attendance epic and the numbers page; approval-time metrics already exist either way.

### D4. Three urgency levels, not two

- **The gap:** Dawn gives two promises (24 hours, five working days), but message 6 (banging boiler, still working) is neither an emergency nor ordinary.
- **What I decided:** Emergency, Urgent, Routine. Urgent jobs go to the top of the next working day's queue without out-of-hours escalation. Patel's boiler is Urgent.
- **The alternative:** Two levels, with Patel as Routine.
- **Why this one:** A new symptom on a gas appliance should not sit behind dripping taps for five days, but it does not justify waking someone.
- **What would change my mind:** Meridian's contractors offering only two response times.
- **Blast radius:** The urgency enum and queue ordering; small.

### D5. A category for "not sure"

- **The gap:** Dawn's seven categories have no place for damp and mould (message 5), and "general bucket" invites forcing a guess.
- **What I decided:** An eighth category, Unsure (needs a person). Mould goes there, as does anything the AI is unsure about or that fails validation (R10, RULE-3).
- **The alternative:** Add Damp and mould as its own category.
- **Why this one:** It turns every edge case, not only mould, into a human decision rather than a confident wrong bucket.
- **What would change my mind:** Mould becoming frequent enough that Meridian has a dedicated contractor for it.
- **Blast radius:** The category enum, the validator and the contractor lookup.

### D6. No heating is an emergency only in winter

- **The gap:** "No heating in winter is an emergency" does not define winter or say what happens with a baby in summer.
- **What I decided:** Winter is October to April. Outside it, no heating is not a rule-based emergency, though the AI may raise it to Urgent and name the vulnerability.
- **The alternative:** No heating is an emergency all year when the household is vulnerable.
- **Why this one:** It follows Dawn's wording and keeps the rule deterministic.
- **What would change my mind:** A complaint or a duty that treats vulnerable households differently year round.
- **Blast radius:** One emergency rule and its tests.

### D7. Gas gets a fixed safety message

- **The gap:** "Anything gas is an emergency", but Meridian cannot attend a gas leak fast enough to make the tenant safe.
- **What I decided:** A message mentioning gas gets a fixed, pre-written reply: leave the property, do not use switches, call the National Gas Emergency line on 0800 111 999. It is a template, never AI text.
- **The alternative:** Treat gas like any other emergency, with only the usual acknowledgement.
- **Why this one:** The gas network's emergency service can respond faster than any contractor, and telling the tenant at once is the one thing the system can do immediately.
- **What would change my mind:** Meridian's legal or safety advice on the wording.
- **Blast radius:** One template and one rule.

### D8. Acknowledge every tenant automatically

- **The gap:** Messages 2–4 show the Walsh family writing three times because "no one has responded"; nothing in the brief asks for replies.
- **What I decided:** Every message gets an automatic acknowledgement with a job reference, from a fixed template.
- **The alternative:** No tenant contact until a person has looked.
- **Why this one:** It contacts the tenant but decides nothing, and would have stopped two of the four Walsh messages.
- **What would change my mind:** Tenants reading an acknowledgement as a promise of action.
- **Blast radius:** Templates and the outbox.

### D9. One email, two problems, becomes two jobs

- **The gap:** Message 1 reports a tap and a bulb; the input does not say whether that is one job or two.
- **What I decided:** One job per issue. The AI lists the issues; deterministic code creates the jobs (R6).
- **The alternative:** One job per email, with the second problem in its notes.
- **Why this one:** The two problems need different contractors, and one job would lose one of them.
- **What would change my mind:** Meridian's job system charging or tracking per visit rather than per problem.
- **Blast radius:** Job creation in the intake pipeline.

### D10. Repeat messages are follow-ups, not new jobs

- **The gap:** Messages 2, 3 and 4 are one fault from two email addresses.
- **What I decided:** A message from the same property, same category (or Unsure), while the job is open and within 7 days, attaches to that job as a follow-up and can raise its urgency. Matching is fixed code (AD-13).
- **The alternative:** Let the AI decide which messages belong together.
- **Why this one:** Grouping wrongly could hide a new problem, so the rule must be predictable and testable.
- **What would change my mind:** Eval cases where the fixed rule groups two genuinely different faults.
- **Blast radius:** `FollowUpMatcher` and its tests.

### D11. What the AI does and what stays deterministic

- **The gap:** The task leaves it to us what the AI does with incoming messages.
- **What I decided:** The AI only reads free text and proposes issues, category, urgency, property text, an English summary and an explanation. Fixed code decides emergency rules, the final urgency, grouping, property matching, contractor choice, escalation and every text a tenant sees. The AI can only raise urgency (RULE-2, RULE-4).
- **The alternative:** Let the AI also choose the contractor and write tenant replies.
- **Why this one:** Everything a rule can do reliably should not depend on a probabilistic model, and it keeps the AI's mistakes catchable.
- **What would change my mind:** Eval results showing the rules miss many emergencies the AI catches; the AI would still only raise, never lower.
- **Blast radius:** AI-DESIGN.md and the pipeline order (AD-3).

### D12. The map lives in the product

- **The gap:** "We put someone else on it who doesn't know the buildings and it's a disaster."
- **What I decided:** Each job shows its building, the usual contractor for its category there, and the rules that applied. A one-page guide covers only the process.
- **The alternative:** A separate guidance document for the backup.
- **Why this one:** A document goes out of date when a contractor changes; data in the product does not.
- **What would change my mind:** Backup users still calling Rachel to ask about buildings.
- **Blast radius:** Seed data and the job view.

### D13. WhatsApp is out of the first version

- **The gap:** "Some of them send WhatsApps as well now, which we don't really have a process for."
- **What I decided:** Email only, with the intake built so another channel can be added (R3).
- **The alternative:** Build WhatsApp intake now.
- **Why this one:** Meridian has no process for it, and it means a business account and an integration, none of which proves the triage works.
- **What would change my mind:** WhatsApp turning out to be a large share of emergencies.
- **Blast radius:** A new intake adapter; the pipeline does not change.

### D14. Photos are kept, not read

- **The gap:** Message 5 has two photos attached.
- **What I decided:** Attachments are stored and shown to the person; the system does not analyse them.
- **The alternative:** Send photos to a vision model.
- **Why this one:** The text alone already makes Mrs Nowak's case serious, and image analysis adds cost and a new failure mode for little gain.
- **What would change my mind:** Eval cases where the text alone misleads and a photo would not.
- **Blast radius:** A second model call in the preparation step.

### D15. The MD's numbers stay small

- **The gap:** "The MD wants to see numbers out of this... Don't go mad on it."
- **What I decided:** One page: jobs by category and urgency, time to first human look, time to approval, time to attendance, and how often each promise was met, all computed from the event log (AD-5).
- **The alternative:** A dashboard with charts, filters and exports.
- **Why this one:** Dawn said not to go mad, and computing from events means the numbers cannot disagree with the job history.
- **What would change my mind:** The MD asking for a specific report.
- **Blast radius:** One query and one page.

### D16. Model behind our own interface, stub by default

- **The gap:** The task lets us choose the model and allows a stub.
- **What I decided:** One `ITriageModel` port with a stub of hand-written recorded responses as the default and OpenRouter (OpenAI-compatible) when a key is set. The model id is configuration, chosen against the eval.
- **The alternative:** Call one provider's SDK directly.
- **Why this one:** Reviewers can run it without a key, the eval can run against any model, and swapping provider touches one adapter.
- **What would change my mind:** Needing provider-specific features such as structured output modes that OpenRouter does not pass through.
- **Blast radius:** Infrastructure adapters only.

### D17. Every step is recorded

- **The gap:** Nothing in the input asks for an audit trail, but "Rachel does the exceptions" means someone must see what the system did.
- **What I decided:** Every step of every job is an append-only event, including the model's raw output, model id and prompt version (R22, AD-5).
- **The alternative:** Store only the current state of each job.
- **Why this one:** It explains every AI decision to the person approving, feeds the metrics, and is what production monitoring reads.
- **What would change my mind:** Storage or privacy rules that limit keeping raw messages (open question OQ-3).
- **Blast radius:** The event table; every feature writes to it.

### D18. React for the front end

- **The gap:** "A front end sufficient to demonstrate the workflow."
- **What I decided:** React with Vite, built into the ASP.NET Core app's static files so one process serves both.
- **The alternative:** Razor Pages, which is simpler and needs no Node.
- **Why this one:** My choice of tooling. The cost is that the run needs Node 20.19+ or 22.12+ as well as .NET 8, which the run script checks.
- **What would change my mind:** Reviewers unable to install Node.
- **Blast radius:** The `client/` folder and the run script.

### D19. .NET 8 rather than 9

- **The gap:** ".NET 8 or .NET 9".
- **What I decided:** .NET 8, packages pinned to their 8.0 lines (EF Core 8.0.34).
- **The alternative:** .NET 9.
- **Why this one:** The .NET 8 SDK was available in the build environment from the OS package source; the Microsoft download was blocked. Both versions reach the end of Microsoft support around November 2026 (from Microsoft's published policy, not checked again here), so neither is a long-term choice.
- **What would change my mind:** Running this for real, where it should move to a supported version.
- **Blast radius:** Target framework and package versions in each project file.

### D20. No login in the first version

- **The gap:** The input does not mention users or access.
- **What I decided:** No authentication. The UI has an "acting as" switch (Rachel or the backup) that is recorded as the actor on each event. Contractor links are single-job tokens.
- **The alternative:** Build login and roles.
- **Why this one:** It is a demo of the workflow, and login adds setup for reviewers without testing the AI.
- **What would change my mind:** Any live use, where it is required.
- **Blast radius:** A middleware and the actor on events.

### D21. Which BMAD version to follow

- **The gap:** The masterclass video shows BMAD v4; the task says to follow the current version.
- **What I decided:** The current docs at docs.bmad-method.org, which describe V7 (installed version reports 6.13.0-next): skills such as `bmad-agent-analyst`, `bmad-agent-pm`, `bmad-agent-po` and `bmad-build`, stories in `tickets.toml`, output in `_bmad-output/`. The video is used only for its principles. Notes in `docs/bmad-research/bmad-notes.md`.
- **The alternative:** The stable v6.12 release, or v4 as in the video.
- **Why this one:** The task asks for the current version, and the docs site is the primary source; v6.12 was the fallback if V7 broke.
- **What would change my mind:** V7 changing incompatibly mid-task.
- **Blast radius:** Only where the BMAD outputs live and how they are named.
