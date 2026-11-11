---
title: "Meridian maintenance triage"
template: brief
version: 1.1.0
status: draft
created: "2026-11-11"
updated: "2026-11-11"
---

# Meridian maintenance triage

## Table of Contents

## Executive summary

## Problem

Every maintenance request Meridian receives, 300–400 a month and more in winter, lands in one inbox and is read by one person. Rachel, the maintenance coordinator, works out what each message is, how urgent it is and which contractor to send, then raises the job. She is a single point of failure: when she is off sick nothing moves, and when she is on holiday a stand-in who does not know the buildings makes a mess of it. Meridian loses about two days every time she is away.

The cost shows up out of hours. A tenant writes in on a Friday night, Rachel is not there, and the process fails. The forwarded messages show what that looks like:

- **A broken promise.** The tenancy agreement promises a response within 24 hours on emergencies. A Friday 19:15 email reporting no heating or hot water with a baby in the house may not be read until Monday. This is the biggest cost: it is a contractual commitment, and missing it is a legal and reputational exposure, not just an inconvenience.
- **Tenant safety.** A family with a 7-month-old went a night without heat or hot water.
- **Repeat work.** The same fault arrived as three messages from two email addresses within 75 minutes, because nobody answered.
- **Missed risk.** Mould in a child's bedroom, where the child has asthma, was reported in Polish and could easily sit unread or be misunderstood.

Much of the volume is "the same five things over and over", so the bottleneck is spending scarce human attention on routine reading rather than on the cases that need judgement.

## Solution

A triage service that reads every incoming maintenance message the moment it arrives, prepares the job, and puts it in front of a person to approve. The AI prepares; a human approves everything. Nothing reaches a contractor until Rachel, or the backup covering for her, clicks approve.

What changes:

- **Every message is prepared, not just received.** Each one arrives in a review queue already sorted: what the problem is (category), how urgent it is, which property, and a suggested contractor. One email that reports two problems becomes two jobs; repeat messages about the same fault are grouped into one.
- **Emergencies reach a person straight away, day or night.** Fixed rules flag the obvious emergencies first (gas, water through a ceiling, no heating in winter), and the AI can only ever raise a message's urgency, never lower it. An urgent message goes to Rachel's phone first; if she has not acknowledged it within 30 minutes [ASSUMPTION: window to confirm], it goes to a named backup such as Dawn.
- **Routine work waits safely.** Non-urgent jobs sit prepared in the queue until a person is back, inside the five-working-day promise.
- **Rachel's job changes from doing everything to reviewing and deciding.** On Monday morning the weekend's messages are already sorted. She checks the AI's work, approves or corrects it, and spends her time on the exceptions and the difficult cases. Her corrections show where the system is getting things wrong.
- **A stand-in can do the job.** Because each job arrives prepared and explained, someone who does not know the buildings can approve with confidence.

## What makes it different

## Who it serves

## Success criteria

## Scope
