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

**Primary: the backup.** Dawn, or whoever covers when Rachel is away. They use the same review queue but do not know the buildings, the tenants or which contractor suits which job, and today that is where things go wrong. The product is designed for them first: every job arrives explained, and the map they need is built into the product: each job shows the building, the usual contractor for that category and the emergency rules that applied. A one-page guide covers only the process: how to approve, when to escalate, who to call. Success for the backup is a normal week of approvals while Rachel is away, with nothing missed and no call to her.

**Primary: Rachel.** The maintenance coordinator who uses the queue every day. She needs speed: getting through a weekend's prepared jobs quickly, and spending her time on the exceptions and the difficult cases. A design that works for someone who does not know the buildings also works for her. Success for Rachel is time off that is actually off.

**Secondary: tenants.** They never see the system, but they get an acknowledgement at once, a safety message for gas, and a faster response on emergencies.

**Secondary: the MD.** Wants "how many jobs, how fast, that sort of thing", without anyone going mad on it.

**Not users: contractors.** They receive approved jobs the same way they do today.

## Success criteria

For tenants and the business:

- **SC1. No emergency is missed.** Every message that meets an emergency rule (gas, water through a ceiling, no heating in winter) reaches Rachel or the backup within 30 minutes, at any hour. One missed gas report is a stop-and-fix event, not a percentage. [ASSUMPTION]
- **SC2. The 24-hour emergency promise is kept** for every emergency job, measured from the tenant's first message to the job being approved. [ASSUMPTION]
- **SC3. Every tenant is acknowledged within 5 minutes** of their message arriving. [ASSUMPTION]
- **SC4. No backlog when Rachel is away.** A week of her absence ends with no prepared job older than one working day waiting for approval. [ASSUMPTION]

For the AI component:

- **SC5. The AI never lowers urgency.** On the test set, no message labelled emergency is prepared as routine. [ASSUMPTION]
- **SC6. Most prepared jobs are right first time.** At least 80% of routine jobs are approved without the category or contractor being changed. Below 60%, the AI is costing more time than it saves and should be switched off. [ASSUMPTION]

For the MD: a small set of numbers. Jobs by category and urgency, time from message to first human look, time to approval, and how often the 24-hour and five-day promises were met.

## Scope

**In the first version:**

- Taking in maintenance messages (email first; for the demo, messages are pasted or seeded rather than read from a live inbox).
- Preparing each job: category, urgency, property, suggested contractor and a plain-English explanation, including messages not written in English.
- Fixed emergency rules first; the AI may only raise urgency.
- Splitting a message with two problems into two jobs, and grouping repeat messages into one.
- A review queue where Rachel or the backup approves, edits or rejects every job. Approval marks the job ready for the contractor.
- Escalation of urgent jobs to Rachel, then to the named backup after 30 minutes (notification simulated in the demo).
- Automatic tenant acknowledgement and the fixed gas safety message (sending simulated in the demo).
- The built-in map: buildings, categories and contractors, from seed data.
- A record of every AI decision and every human correction.
- A small numbers page for the MD.

**Left out, and why:**

- **WhatsApp.** Dawn has no process for it yet. The intake is built so a second channel can be added later.
- **Reading photos.** Mrs Nowak's photos would help, but the text alone already marks her case as serious; photo analysis adds cost and risk for little gain in the first version.
- **Connecting to the live inbox, Meridian's job system and contractors.** Integration work that can follow once the triage is trusted.
- **Real SMS and email sending.** Simulated, so the demo runs anywhere without accounts.
- **The system learning by itself from corrections.** Corrections are recorded and reviewed by people; nothing changes the AI's behaviour automatically.
