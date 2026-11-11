---
type: epic
title: "Approved jobs are tracked to attendance and counted"
parent: initiative-meridian-triage
covers: [R20, R21, R24]
sources: []
companions: []
after: [{ epic = "epic-triage-core", needs = "approval and the event log" }]
refined: false
risk: medium
---

# Approved jobs are tracked to attendance and counted

## Description

An approved job reaches its contractor with a link to mark it attended, an emergency running out of its 24 hours raises a fallback-contractor alert, and the MD's numbers page shows the promise measures from recorded events.

## Outcome

Meridian can show the MD how many jobs, how fast, and how often each promise was met.

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
