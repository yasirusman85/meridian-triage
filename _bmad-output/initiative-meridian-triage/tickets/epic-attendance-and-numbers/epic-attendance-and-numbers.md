---
type: epic
title: "Approved jobs are tracked to attendance and counted"
parent: initiative-meridian-triage
covers: [R20, R21, R24]
sources: []
companions: [_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md]
after: []
refined: true
risk: medium
---

# Approved jobs are tracked to attendance and counted

## Description

An approved job reaches its contractor with a link to mark it attended, an emergency running out of its 24 hours raises a fallback-contractor alert, and the MD's numbers page shows the promise measures from recorded events.

## Outcome

Meridian can show the MD how many jobs, how fast, and how often each promise was met.

## Requirements

- R1 (parent R20): An approved job reaches its contractor with a link the contractor uses to mark it attended.
- R2 (parent R21): An emergency not marked attended within its remaining 24 hours alerts the on-duty person to send the fallback contractor.
- R3 (parent R24): A numbers page shows jobs by category and urgency, time to first human look, time to approval, time to attendance, and how often each promise was met.

## Constraints

- prd RULE-1, RULE-8, RULE-10; architecture AD-5, AD-6, AD-9, AD-11 bind this epic.

## Done when

- A contractor marks a job attended through its link and the event is recorded.
- An emergency past its window raises the fallback alert.
- The numbers page shows counts and times computed from events.

## Boundaries

- Out: a full analytics suite; integration with Meridian's job system.
- Must not change: event recording from epic-triage-core.

## References

- parent — _bmad-output/initiative-meridian-triage/initiative-meridian-triage.md, Requirements

## Notes

- Decision: 2026-11-11, user approved the three-epic split (triage core, out-of-hours safety, attendance and numbers).
- Decision: 2026-11-16, refined from the parent's lines alone (user's defaults: no stops).
