# Eval results: anthropic/claude-haiku-5.5, 2026-11-16

Model `anthropic/claude-haiku-5.5`, prompt `triage.v1`. 52 cases (49 maintenance, 54 expected issues), run through the real intake pipeline (AD-12). Scoring rules: `eval/README.md`.

**Result: FAIL**

| Threshold | Target | Result | |
|---|---|---|---|
| Emergency recall (SM-1) | 100% | 100% (22 of 22) | pass |
| Category accuracy (SM-7) | at least 85% | 83.3% (45 of 54) | FAIL |

## Summary

| Measure | Result |
|---|---|
| Emergency recall | 100% (22 of 22) |
| Missed emergencies | 0 |
| Category accuracy | 83.3% (45 of 54) |
| Urgency exact match | 87% (47 of 54) |
| Under-escalations | 2 |
| Over-escalations | 5 |
| Issue count match (maintenance cases) | 100% (49 of 49) |
| Property match | 98.1% (53 of 54) |
| Vulnerability flag match | 100% (54 of 54) |
| Non-maintenance handled (one Unsure job, not Emergency) | 100% (3 of 3) |
| Follow-up grouping | 98.1% (51 of 52) |
| Cases where the model gave no valid answer | 0 of 52 |
| Pipeline errors | 0 |
| Model latency, total | 118066 ms |
| Tokens in / out | 47379 / 19790 |
| Estimated cost (USD) | 0.014633 |

## Failures by harm

### Critical (0)

Missed emergencies. Any one blocks release.

None.

### Tolerable (5)

Over-escalations. Tolerated; each is reviewed.

| Case | Field | Expected | Actual |
|---|---|---|---|
| var-11 | urgency | Routine | Urgent |
| var-23 | urgency | Routine | Emergency |
| var-41 | urgency | Urgent | Emergency |
| var-42 | urgency | Routine | Urgent |
| var-44 | urgency | Routine | Urgent |

### Minor (13)

Wrong category or a wrong field on a non-urgent issue.

| Case | Field | Expected | Actual |
|---|---|---|---|
| real-03 | property_text | 14b Alder Court | null |
| real-03 | follow_up_of | attached to real-02 (MER-000003), no new job | attached to [], new jobs [MER-000004] |
| real-06 | urgency | Urgent | Routine |
| var-01 | category | Heating | Appliances |
| var-02 | category | Heating | Unsure |
| var-04 | category | Heating | Unsure |
| var-05 | category | Heating | Unsure |
| var-06 | category | Heating | Unsure |
| var-15 | category | Plumbing | Unsure |
| var-21 | category | Heating | Unsure |
| var-22 | urgency | Urgent | Routine |
| var-28 | category | Unsure | Pest |
| var-43 | category | Appliances | Electrical |

## Cases

| Case | Correct | Produced | Model | Latency (ms) | Tokens in/out |
|---|---|---|---|---|---|
| real-01 | yes | Plumbing/Routine, Electrical/Routine | ok | 3378 | 975/571 |
| real-02 | yes | Heating/Emergency | ok | 1340 | 923/193 |
| real-03 | no | Unsure/Routine | ok | 952 | 878/108 |
| real-04 | yes | Heating/Emergency (follow-up) | ok | 3105 | 900/502 |
| real-05 | yes | Unsure/Urgent | ok | 2223 | 930/369 |
| real-06 | no | Heating/Routine | ok | 2558 | 904/424 |
| var-01 | no | Appliances/Emergency | ok | 1408 | 910/211 |
| var-02 | no | Unsure/Emergency | ok | 3432 | 905/641 |
| var-03 | yes | Heating/Emergency | ok | 2462 | 908/424 |
| var-04 | no | Unsure/Emergency | ok | 3474 | 925/556 |
| var-05 | no | Unsure/Emergency | ok | 2618 | 936/445 |
| var-06 | no | Unsure/Emergency | ok | 2746 | 915/545 |
| var-07 | yes | Heating/Emergency | ok | 1664 | 914/204 |
| var-08 | yes | Appliances/Emergency | ok | 1360 | 902/163 |
| var-09 | yes | LocksAndDoors/Emergency | ok | 2136 | 892/366 |
| var-10 | yes | Plumbing/Routine | ok | 2068 | 912/322 |
| var-11 | no | Plumbing/Urgent (follow-up) | ok | 3173 | 895/570 |
| var-12 | yes | Plumbing/Emergency (follow-up) | ok | 1686 | 917/290 |
| var-13 | yes | Plumbing/Emergency | ok | 2732 | 917/481 |
| var-14 | yes | Plumbing/Emergency | ok | 2620 | 905/474 |
| var-15 | no | Unsure/Emergency | ok | 2627 | 921/494 |
| var-16 | yes | Heating/Emergency | ok | 1376 | 906/183 |
| var-17 | yes | Heating/Routine | ok | 1829 | 916/276 |
| var-18 | yes | Heating/Urgent | ok | 3233 | 903/632 |
| var-19 | yes | Heating/Emergency | ok | 2760 | 893/465 |
| var-20 | yes | Heating/Urgent | ok | 3001 | 881/556 |
| var-21 | no | Unsure/Emergency | ok | 2077 | 888/290 |
| var-22 | no | Heating/Routine | ok | 2153 | 902/347 |
| var-23 | no | Plumbing/Emergency, Heating/Emergency | ok | 3665 | 907/742 |
| var-24 | yes | LocksAndDoors/Urgent, Pest/Routine | ok | 2659 | 928/519 |
| var-25 | yes | Appliances/Routine, Electrical/Routine, General/Routine | ok | 3159 | 930/675 |
| var-26 | yes | Electrical/Routine | ok | 2289 | 919/381 |
| var-27 | yes | Unsure/Routine | ok | 2193 | 884/382 |
| var-28 | no | Pest/Routine | ok | 2655 | 895/408 |
| var-29 | yes | Plumbing/Urgent | ok | 3251 | 978/523 |
| var-30 | yes | Electrical/Emergency | ok | 1075 | 950/174 |
| var-31 | yes | Electrical/Emergency | ok | 2520 | 920/417 |
| var-32 | yes | LocksAndDoors/Routine | ok | 1103 | 919/157 |
| var-33 | yes | Appliances/Routine | ok | 1878 | 926/317 |
| var-34 | yes | Unsure/Routine | ok | 697 | 903/15 |
| var-35 | yes | Unsure/Routine | ok | 678 | 899/15 |
| var-36 | yes | Unsure/Routine | ok | 652 | 898/15 |
| var-37 | yes | Plumbing/Routine | ok | 1261 | 918/187 |
| var-38 | yes | LocksAndDoors/Emergency | ok | 2183 | 918/397 |
| var-39 | yes | LocksAndDoors/Urgent | ok | 2561 | 909/453 |
| var-40 | yes | Pest/Routine | ok | 1200 | 902/180 |
| var-41 | no | Pest/Emergency | ok | 1804 | 907/281 |
| var-42 | no | Appliances/Urgent | ok | 3166 | 898/557 |
| var-43 | no | Electrical/Routine | ok | 5365 | 909/965 |
| var-44 | no | General/Urgent | ok | 2412 | 904/447 |
| var-45 | yes | Unsure/Routine | ok | 1637 | 901/197 |
| var-46 | yes | General/Routine | ok | 1812 | 884/284 |
