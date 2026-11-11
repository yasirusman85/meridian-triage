---
type: epic
title: "Tenants are answered and emergencies reach a person at any hour"
parent: initiative-meridian-triage
covers: [R16, R17, R18, R19]
sources: []
companions: []
after: []
refined: false
risk: medium
---

# Tenants are answered and emergencies reach a person at any hour

## Description

Every message gets a fixed-template acknowledgement with its job reference, gas reports get the fixed gas safety message, and emergencies notify Rachel and then the backup after 30 minutes; notifications are simulated and visible in the product.

## Outcome

No tenant goes unanswered and no emergency waits for someone to open the inbox.

## Done when

- Each of the six messages shows its acknowledgement, the gas message where it applies.
- A simulated unacknowledged emergency reaches the backup after 30 minutes, and an unacknowledged one is marked escalation-failed.
- Every notification is in the job's event history.

## Boundaries

- Out: AI-written tenant text; real SMS and email sending (simulated).
- Must not change: approval rules from epic-triage-core.

## References

- parent — _bmad-output/initiative-meridian-triage/initiative-meridian-triage.md, Requirements

## Notes

- Decision: 2026-11-11, user approved the three-epic split (triage core, out-of-hours safety, attendance and numbers).
