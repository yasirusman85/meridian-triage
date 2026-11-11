---
type: epic
title: "Every message is prepared and approved"
parent: initiative-meridian-triage
covers: [R1, R2, R3, R4, R5, R6, R7, R8, R9, R10, R11, R12, R13, R14, R15, R22, R23]
sources: []
companions: []
after: []
refined: false
risk: high
---

# Every message is prepared and approved

## Description

Stands up the product: messages are pasted or seeded, run through the emergency rules, prepared by the AI behind the product's own interface (stub by default, OpenRouter when a key is set), split and grouped, and shown in a review queue where a person approves, edits or rejects each job. Every step is recorded.

## Outcome

Rachel or a backup can work the six forwarded messages from a prepared, explained queue, and the eval set reports emergency recall and category accuracy.

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

## Notes

- Decision: 2026-11-11, user approved the three-epic split (triage core, out-of-hours safety, attendance and numbers).
