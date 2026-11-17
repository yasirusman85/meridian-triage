# Eval results: anthropic/claude-haiku-5.5, 2026-11-17

Model `anthropic/claude-haiku-5.5`, prompt `triage.v2`. 52 cases (49 maintenance, 54 expected issues), run through the real intake pipeline (AD-12). Scoring rules: `eval/README.md`.

**Result: PASS**

| Threshold | Target | Result | |
|---|---|---|---|
| Emergency recall (SM-1) | 100% | 100% (22 of 22) | pass |
| Category accuracy (SM-7) | at least 85% | 90.7% (49 of 54) | pass |

## Summary

| Measure | Result |
|---|---|
| Emergency recall | 100% (22 of 22) |
| Missed emergencies | 0 |
| Category accuracy | 90.7% (49 of 54) |
| Urgency exact match | 88.9% (48 of 54) |
| Under-escalations | 0 |
| Over-escalations | 6 |
| Issue count match (maintenance cases) | 98% (48 of 49) |
| Property match | 100% (54 of 54) |
| Vulnerability flag match | 100% (54 of 54) |
| Non-maintenance handled (one Unsure job, not Emergency) | 100% (3 of 3) |
| Follow-up grouping | 100% (52 of 52) |
| Cases where the model gave no valid answer | 0 of 52 |
| Pipeline errors | 0 |
| Model latency, total | 108865 ms |
| Tokens in / out | 59339 / 18384 |
| Estimated cost (USD) | 0.015126 |

## Failures by harm

### Critical (0)

Missed emergencies. Any one blocks release.

None.

### Tolerable (6)

Over-escalations. Tolerated; each is reviewed.

| Case | Field | Expected | Actual |
|---|---|---|---|
| var-11 | urgency | Routine | Urgent |
| var-23 | urgency | Routine | Emergency |
| var-41 | urgency | Urgent | Emergency |
| var-42 | urgency | Routine | Urgent |
| var-43 | urgency | Routine | Urgent |
| var-44 | urgency | Routine | Urgent |

### Minor (6)

Wrong category or a wrong field on a non-urgent issue.

| Case | Field | Expected | Actual |
|---|---|---|---|
| var-08 | issue_count | 1 | 2 |
| var-09 | category | LocksAndDoors | General |
| var-15 | category | Plumbing | Unsure |
| var-21 | category | Heating | Unsure |
| var-28 | category | Unsure | Pest |
| var-43 | category | Appliances | Electrical |

## Cases

| Case | Correct | Produced | Model | Latency (ms) | Tokens in/out |
|---|---|---|---|---|---|
| real-01 | yes | Plumbing/Routine, Electrical/Routine | ok | 3716 | 1205/619 |
| real-02 | yes | Heating/Emergency | ok | 1389 | 1153/195 |
| real-03 | yes | Unsure/Routine (follow-up) | ok | 2727 | 1108/460 |
| real-04 | yes | Heating/Emergency (follow-up) | ok | 3234 | 1130/583 |
| real-05 | yes | Unsure/Urgent | ok | 2108 | 1160/331 |
| real-06 | yes | Heating/Urgent | ok | 1673 | 1134/276 |
| var-01 | yes | Heating/Emergency | ok | 1370 | 1140/224 |
| var-02 | yes | Heating/Emergency | ok | 2894 | 1135/516 |
| var-03 | yes | Heating/Emergency | ok | 1280 | 1138/185 |
| var-04 | yes | Heating/Emergency | ok | 2193 | 1155/402 |
| var-05 | yes | Heating/Emergency | ok | 1579 | 1166/272 |
| var-06 | yes | Heating/Emergency | ok | 1822 | 1145/309 |
| var-07 | yes | Heating/Emergency | ok | 1406 | 1144/190 |
| var-08 | no | Heating/Emergency, Appliances/Emergency | ok | 3111 | 1132/642 |
| var-09 | no | General/Emergency | ok | 2298 | 1122/430 |
| var-10 | yes | Plumbing/Routine | ok | 1434 | 1142/181 |
| var-11 | no | Plumbing/Urgent (follow-up) | ok | 2750 | 1125/498 |
| var-12 | yes | Plumbing/Emergency (follow-up) | ok | 1658 | 1147/303 |
| var-13 | yes | Plumbing/Emergency | ok | 1334 | 1147/204 |
| var-14 | yes | Plumbing/Emergency | ok | 2432 | 1135/400 |
| var-15 | no | Unsure/Emergency | ok | 3306 | 1151/605 |
| var-16 | yes | Heating/Emergency | ok | 1695 | 1136/262 |
| var-17 | yes | Heating/Routine | ok | 1810 | 1146/316 |
| var-18 | yes | Heating/Urgent | ok | 3284 | 1133/621 |
| var-19 | yes | Heating/Emergency | ok | 3315 | 1123/542 |
| var-20 | yes | Heating/Urgent | ok | 3104 | 1111/533 |
| var-21 | no | Unsure/Emergency | ok | 1836 | 1118/269 |
| var-22 | yes | Heating/Urgent | ok | 2152 | 1132/326 |
| var-23 | no | Plumbing/Emergency, Heating/Emergency | ok | 2183 | 1137/372 |
| var-24 | yes | LocksAndDoors/Urgent, Pest/Routine | ok | 2471 | 1158/469 |
| var-25 | yes | Appliances/Routine, Electrical/Routine, General/Routine | ok | 3010 | 1160/662 |
| var-26 | yes | Electrical/Routine | ok | 1948 | 1149/344 |
| var-27 | yes | Unsure/Routine | ok | 1281 | 1114/181 |
| var-28 | no | Pest/Routine | ok | 1884 | 1125/317 |
| var-29 | yes | Plumbing/Urgent | ok | 3321 | 1208/644 |
| var-30 | yes | Electrical/Emergency | ok | 1780 | 1180/263 |
| var-31 | yes | Electrical/Emergency | ok | 1308 | 1150/205 |
| var-32 | yes | LocksAndDoors/Routine | ok | 1122 | 1149/160 |
| var-33 | yes | Appliances/Routine | ok | 1931 | 1156/304 |
| var-34 | yes | Unsure/Routine | ok | 914 | 1133/75 |
| var-35 | yes | Unsure/Routine | ok | 691 | 1129/15 |
| var-36 | yes | Unsure/Routine | ok | 709 | 1128/54 |
| var-37 | yes | Plumbing/Routine | ok | 1788 | 1148/305 |
| var-38 | yes | LocksAndDoors/Emergency | ok | 1728 | 1148/312 |
| var-39 | yes | LocksAndDoors/Urgent | ok | 3458 | 1139/563 |
| var-40 | yes | Pest/Routine | ok | 1598 | 1132/245 |
| var-41 | no | Pest/Emergency | ok | 2010 | 1137/295 |
| var-42 | no | Appliances/Urgent | ok | 3194 | 1128/509 |
| var-43 | no | Electrical/Urgent | ok | 2332 | 1139/429 |
| var-44 | no | General/Urgent | ok | 2484 | 1134/490 |
| var-45 | yes | Unsure/Routine | ok | 1077 | 1131/182 |
| var-46 | yes | General/Routine | ok | 1733 | 1114/295 |
