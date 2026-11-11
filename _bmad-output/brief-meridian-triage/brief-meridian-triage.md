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

- **Every message is prepared, not just received.** Each one arrives in a review queue already sorted: what the problem is (category), how urgent it is, which property, and a suggested contractor. One email that reports two problems becomes two jobs, because they may need different contractors. Repeat messages about the same fault, even from different people in the same household, are grouped into one job, with the later messages shown as follow-ups that raise its urgency.
- **Every job explains itself.** Each prepared job says why it was classified the way it was ("Category: heating. Urgent because: no heating, a baby in the house, winter"), so a stand-in who does not know the buildings can make the right call without Rachel's local knowledge.
- **Emergencies reach a person straight away, day or night.** Fixed rules flag the obvious emergencies first (gas, water through a ceiling, no heating in winter), and the AI can only ever raise a message's urgency, never lower it. An urgent message goes to Rachel's phone first; if she has not acknowledged it within 30 minutes [ASSUMPTION: window to confirm], it goes to a named backup such as Dawn.
- **Every tenant hears back at once.** The system sends an automatic acknowledgement with a reference number ("we've received your report about your heating, reference #123"). It contacts the tenant but decides nothing, and it stops people writing again and again because nobody answered. A report that mentions gas instead gets a fixed, pre-written safety message: leave the property, do not use switches, and call the National Gas Emergency line on 0800 111 999.
- **Routine work waits safely.** Non-urgent jobs sit prepared in the queue until a person is back, inside the five-working-day promise.
- **Rachel's job changes from doing everything to reviewing and deciding.** On Monday morning the weekend's messages are already sorted. She checks the AI's work, approves or corrects it, and spends her time on the exceptions and the difficult cases. Her corrections show where the system is getting things wrong.

## What makes it different

The product does not replace a backup person; a backup coordinator is always worth having. It changes what that backup can do. Today a stand-in is "someone who doesn't know the buildings, and it's a disaster". With this product any backup can do the job, because every message has already been read, sorted and explained by the time they open it. And the reading never stops: urgent messages are flagged within seconds, 24 hours a day, whether or not anyone is at a desk.

The alternatives, and why they fall short:

- **Do nothing.** Meridian keeps losing two days every absence and keeps missing its 24-hour emergency promise out of hours.
- **A second coordinator on their own.** Still a person who sleeps, takes holidays and does not know the buildings. Worth having as the backup, but not a fix for the reading bottleneck.
- **Inbox rules and a tenant web form.** Cheap and needs no AI, but tenants do not use forms at 7 pm in a panic: the Walshes sent angry emails, and Mrs Nowak wrote in Polish. A keyword rule sees "boiler" in both the Walsh email (no heating, a baby in the house) and the Patel email (banging, still working) and cannot tell the emergency from the routine job. Rules also cannot spot two problems in one email or read another language.

This product keeps fixed rules for the clear-cut emergencies and uses the AI only for what rules cannot do: reading messy, emotional, multi-issue or non-English messages. The AI can only raise urgency, never lower it, and a person approves every job.

## Who it serves

## Success criteria

## Scope
