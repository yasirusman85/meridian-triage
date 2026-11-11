---
type: epic
title: "Every message is prepared and approved"
parent: initiative-meridian-triage
covers: [R1, R2, R3, R4, R5, R6, R7, R8, R9, R10, R11, R12, R13, R14, R15, R22, R23]
sources: []
companions: [_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md]
after: []
refined: true
risk: high
---

# Every message is prepared and approved

## Description

Stands up the product: messages are pasted or seeded, run through the emergency rules, prepared by the AI behind the product's own interface (stub by default, OpenRouter when a key is set), split and grouped, and shown in a review queue where a person approves, edits or rejects each job. Every step is recorded.

## Outcome

Rachel or a backup can work the six forwarded messages from a prepared, explained queue, and the eval set reports emergency recall and category accuracy.

## Requirements

- R1 (parent R1): A received message is stored with its sender, received time, text and attachments unchanged.
- R2 (parent R2): A message in any language is held with its original text kept beside any translation.
- R3 (parent R3): A second intake channel can be added without changing how jobs are prepared.
- R4 (parent R4): An issue matching a fixed emergency rule (gas or gas smell, water through a ceiling, no heating October to April) is flagged emergency.
- R5 (parent R5): A rule-flagged emergency cannot be lowered by the AI, only by a person.
- R6 (parent R6): A message with several distinct issues becomes one job per issue.
- R7 (parent R7): Each issue gets a proposed category, urgency, property and explanation, plus an English summary when the message is not in English.
- R8 (parent R8): A message from the same property about the same open fault is attached to that job as a follow-up, matched by fixed code.
- R9 (parent R9): A job's urgency is the higher of the rule result and the AI proposal; follow-ups can raise it.
- R10 (parent R10): An unsure or invalid AI result sends the issue to the Unsure category with urgency no lower than the rules gave.
- R11 (parent R11): Each job shows its building, the usual contractor for its category there, and the emergency rules that applied.
- R12 (parent R12): The suggested contractor comes from a fixed lookup by category and building.
- R13 (parent R13): The on-duty person sees all prepared jobs ordered by urgency then age, each with its explanation and original messages.
- R14 (parent R14): The on-duty person can approve, edit (category, urgency, contractor, property) or reject a job, and each edit is recorded before and after.
- R15 (parent R15): No job reaches a contractor until a person has approved it.
- R16 (parent R22): Every step of every job is recorded as an event, including the AI's model, prompt version and raw output.
- R17 (parent R23): A job's full history can be viewed in time order.

## Constraints

- Parent constraints prd RULE-1 to RULE-5, RULE-9, RULE-11, RULE-12, RULE-14 and the task stack constraint bind this epic.
- Architecture AD-1 to AD-13 bind this epic.

## Done when

- The six forwarded messages produce the expected jobs in the queue, Joy's as two, the Walshes' as one with follow-ups.
- The eval set runs from one command and records results against prd SM-1 and SM-7.
- One documented command starts the product with seed data and the stub provider.
- Each job's event history shows rules, AI output, edits and approval.

## Boundaries

- Out: tenant acknowledgements and escalation (epic-out-of-hours-safety); contractor link and numbers page (epic-attendance-and-numbers).
- Touch point: model provider (OpenRouter), consumed behind the product interface.

## References

- parent — _bmad-output/initiative-meridian-triage/initiative-meridian-triage.md, Requirements
- architecture — _bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md, AD-1 to AD-13, Structural Seed

## Notes

- Decision: 2026-11-11, user approved the three-epic split (triage core, out-of-hours safety, attendance and numbers).
- Decision: 2026-11-11, epic refined from the parent's lines alone at the user's request for speed.
- Decision: 2026-11-11, tracer bullet is story 1: a pasted message travels API to SQLite to the React queue, started by run.sh.
- Decision: 2026-11-11, closing refactor sweep included (default).
