---
type: epic
title: "Tenants are answered and emergencies reach a person at any hour"
parent: initiative-meridian-triage
covers: [R16, R17, R18, R19]
sources: []
companions: [_bmad-output/initiative-meridian-triage/architecture-meridian-triage/architecture-meridian-triage.md]
after: []
refined: true
risk: medium
---

# Tenants are answered and emergencies reach a person at any hour

## Description

Every message gets a fixed-template acknowledgement with its job reference, gas reports get the fixed gas safety message, and emergencies notify Rachel and then the backup after 30 minutes; notifications are simulated and visible in the product.

## Outcome

No tenant goes unanswered and no emergency waits for someone to open the inbox.

## Requirements

- R1 (parent R16): Every message gets an acknowledgement with a job reference within 5 minutes; a gas message gets the fixed gas safety message instead.
- R2 (parent R17): Every text a tenant receives comes from a fixed template.
- R3 (parent R18): An emergency notifies Rachel at once and the backup if she has not acknowledged within 30 minutes.
- R4 (parent R19): An emergency acknowledged by neither is marked escalation-failed and held at the top of the queue; notifications are visible in the product.

## Constraints

- prd RULE-5, RULE-6, RULE-8; architecture AD-6, AD-7, AD-8, AD-9 bind this epic.

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
- Decision: 2026-11-16, refined from the parent's lines alone (user's defaults: no stops).
