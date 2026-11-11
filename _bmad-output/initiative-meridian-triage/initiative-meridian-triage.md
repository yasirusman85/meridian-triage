---
type: initiative
title: "Meridian maintenance triage"
parent: none
sources: [_bmad-output/initiative-meridian-triage/prd-meridian-triage/prd-meridian-triage.md, _bmad-output/brief-meridian-triage/brief-meridian-triage.md]
companions: []
after: []
risk: high
---

# Meridian maintenance triage

## Description

Every maintenance message Meridian receives is read the moment it arrives and prepared as a job (category, urgency, property, contractor, explanation) for a person to approve, so the work no longer stops when Rachel is away and emergencies are seen at any hour. Built from the PRD and brief listed in `sources:`.

## Outcome

Rachel and any backup who does not know the buildings can run maintenance from a prepared, explained queue, with no emergency missed and the 24-hour promise measured to contractor attendance (prd SM-1 to SM-7).

## Requirements

- R1 (prd FR-1): A received message is stored with its sender, received time, text and attachments unchanged.
- R2 (prd FR-2): A message in any language is held with its original text kept beside any translation.
- R3 (prd FR-3): A second intake channel can be added without changing how jobs are prepared.
- R4 (prd FR-4): An issue matching a fixed emergency rule (gas or gas smell, water through a ceiling, no heating October to April) is flagged emergency.
- R5 (prd FR-5): A rule-flagged emergency cannot be lowered by the AI, only by a person.
- R6 (prd FR-6): A message with several distinct issues becomes one job per issue.
- R7 (prd FR-7): Each issue gets a proposed category, urgency, property and explanation, plus an English summary when the message is not in English.
- R8 (prd FR-8): A message from the same property about the same open fault is attached to that job as a follow-up, matched by fixed code.
- R9 (prd FR-9): A job's urgency is the higher of the rule result and the AI proposal; follow-ups can raise it.
- R10 (prd FR-10): An unsure or invalid AI result sends the issue to the Unsure category with urgency no lower than the rules gave.
- R11 (prd FR-11): Each job shows its building, the usual contractor for its category there, and the emergency rules that applied.
- R12 (prd FR-12): The suggested contractor comes from a fixed lookup by category and building.
- R13 (prd FR-13): The on-duty person sees all prepared jobs ordered by urgency then age, each with its explanation and original messages.
- R14 (prd FR-14): The on-duty person can approve, edit (category, urgency, contractor, property) or reject a job, and each edit is recorded before and after.
- R15 (prd FR-15): No job reaches a contractor until a person has approved it.
- R16 (prd FR-16): Every message gets an acknowledgement with a job reference within 5 minutes; a gas message gets the fixed gas safety message instead.
- R17 (prd FR-17): Every text a tenant receives comes from a fixed template.
- R18 (prd FR-18): An emergency notifies Rachel at once and the backup if she has not acknowledged within 30 minutes.
- R19 (prd FR-19): An emergency acknowledged by neither is marked escalation-failed and held at the top of the queue; notifications are visible in the product.
- R20 (prd FR-20): An approved job reaches its contractor with a link the contractor uses to mark it attended.
- R21 (prd FR-21): An emergency not marked attended within its remaining 24 hours alerts the on-duty person to send the fallback contractor.
- R22 (prd FR-22): Every step of every job is recorded as an event, including the AI's model, prompt version and raw output.
- R23 (prd FR-23): A job's full history can be viewed in time order.
- R24 (prd FR-24): A numbers page shows jobs by category and urgency, time to first human look, time to approval, time to attendance, and how often each promise was met.

## Constraints

- prd RULE-1: A person approves every job; nothing reaches a contractor without approval.
- prd RULE-2: The AI may only raise urgency; rule-flagged emergencies are lowered only by a person.
- prd RULE-3: Anything the system is unsure about goes to the Unsure category.
- prd RULE-4: Deterministic code decides everything it can (rules, follow-up matching, contractor lookup, escalation, tenant messages); the AI reads free text and proposes only.
- prd RULE-5: The AI writes no text a tenant or contractor receives.
- prd RULE-6: Gas reports get the fixed gas safety message (leave, no switches, National Gas Emergency 0800 111 999).
- prd RULE-8: Emergency: contractor attends within 24 hours of the first message; routine: within five working days.
- prd RULE-9: Original message text is never changed.
- prd RULE-11: With the model slow (over 20 s), unavailable or unusable, the message is still stored, acknowledged, run through the rules and queued as Unsure.
- prd RULE-12: One documented command runs the product with seed data and the stub provider, no key or account needed.
- prd RULE-13: Model cost stays under a few pounds a month at 400 messages.
- prd RULE-14: Only what a job needs is stored or sent to the model provider.
- Task constraint (docs/input/client-input.md): .NET 8 or 9, C#, ASP.NET Core, a relational database; the model sits behind the product's own interface.

## Done when

- The six forwarded messages run end to end: acknowledged, prepared, emergencies escalated, approved, attended, all visible in each job's history.
- The eval set meets prd SM-1 (100% emergency recall) and SM-7 (85% category accuracy) on the provider in use, with results recorded.
- The product starts with one documented command, stub provider by default, OpenRouter when a key is set.
- The numbers page shows the prd SM measures from recorded events.

## Boundaries

- Out: WhatsApp intake, photo reading, live inbox and job-system integration, real SMS and email sending, automatic learning from corrections (prd Non-goals, MVP scope).
- Out: autonomous dispatch at any urgency; tenant-facing AI text.
- Touch point: model provider (OpenRouter) — consumed behind the product's interface; owner: epic-triage-core.
- Tracer path: a pasted message is acknowledged, prepared and queued (epic 1), escalated if an emergency (epic 2), approved, sent and marked attended, and counted (epic 3).

## References

- prd — _bmad-output/initiative-meridian-triage/prd-meridian-triage/prd-meridian-triage.md, Features and requirements, Rules, AI behavior and evaluation
- brief — _bmad-output/brief-meridian-triage/brief-meridian-triage.md, Scope

## Notes

- Open question: data protection obligations and message retention (prd OQ-3); the user and Meridian; needed before live use.
- Open question: which model on OpenRouter (prd OQ-5); the user; needed before the final eval run.
