# Eval results: anthropic/claude-haiku-5.5, 2026-11-16

Model `anthropic/claude-haiku-5.5`, prompt `triage.v2`. 52 cases (49 maintenance, 54 expected issues), run through the real intake pipeline (AD-12). Scoring rules: `eval/README.md`.

**Result: PASS**

| Threshold | Target | Result | |
|---|---|---|---|
| Emergency recall (SM-1) | 100% | 100% (22 of 22) | pass |
| Category accuracy (SM-7) | at least 85% | 92.6% (50 of 54) | pass |

## Summary

| Measure | Result |
|---|---|
| Emergency recall | 100% (22 of 22) |
| Missed emergencies | 0 |
| Category accuracy | 92.6% (50 of 54) |
| Urgency exact match | 88.9% (48 of 54) |
| Under-escalations | 0 |
| Over-escalations | 6 |
| Issue count match (maintenance cases) | 100% (49 of 49) |
| Property match | 100% (54 of 54) |
| Vulnerability flag match | 100% (54 of 54) |
| Non-maintenance handled (one Unsure job, not Emergency) | 100% (3 of 3) |
| Follow-up grouping | 100% (52 of 52) |
| Cases where the model gave no valid answer | 0 of 52 |
| Pipeline errors | 0 |
| Model latency, total | 109289 ms |
| Tokens in / out | 59339 / 18245 |
| Estimated cost (USD) | 0.015056 |

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

### Minor (4)

Wrong category or a wrong field on a non-urgent issue.

| Case | Field | Expected | Actual |
|---|---|---|---|
| var-15 | category | Plumbing | Unsure |
| var-21 | category | Heating | Unsure |
| var-28 | category | Unsure | Pest |
| var-43 | category | Appliances | Electrical |

## Cases

| Case | Correct | Produced | Model | Latency (ms) | Tokens in/out |
|---|---|---|---|---|---|
| real-01 | yes | Plumbing/Routine, Electrical/Routine | ok | 3501 | 1205/584 |
| real-02 | yes | Heating/Emergency | ok | 1347 | 1153/181 |
| real-03 | yes | Unsure/Routine (follow-up) | ok | 2234 | 1108/371 |
| real-04 | yes | Heating/Emergency (follow-up) | ok | 4239 | 1130/777 |
| real-05 | yes | Unsure/Urgent | ok | 1898 | 1160/365 |
| real-06 | yes | Heating/Urgent | ok | 1819 | 1134/294 |
| var-01 | yes | Heating/Emergency | ok | 1815 | 1140/204 |
| var-02 | yes | Heating/Emergency | ok | 2598 | 1135/429 |
| var-03 | yes | Heating/Emergency | ok | 1319 | 1138/203 |
| var-04 | yes | Heating/Emergency | ok | 2066 | 1155/346 |
| var-05 | yes | Heating/Emergency | ok | 1711 | 1166/255 |
| var-06 | yes | Heating/Emergency | ok | 1726 | 1145/315 |
| var-07 | yes | Heating/Emergency | ok | 1364 | 1144/214 |
| var-08 | yes | Appliances/Emergency | ok | 2721 | 1132/561 |
| var-09 | yes | LocksAndDoors/Emergency | ok | 2827 | 1122/501 |
| var-10 | yes | Plumbing/Routine | ok | 1185 | 1142/169 |
| var-11 | no | Plumbing/Urgent (follow-up) | ok | 2564 | 1125/457 |
| var-12 | yes | Plumbing/Emergency (follow-up) | ok | 1728 | 1147/318 |
| var-13 | yes | Plumbing/Emergency | ok | 2100 | 1147/393 |
| var-14 | yes | Plumbing/Emergency | ok | 2273 | 1135/372 |
| var-15 | no | Unsure/Emergency | ok | 2891 | 1151/539 |
| var-16 | yes | Heating/Emergency | ok | 1428 | 1136/177 |
| var-17 | yes | Heating/Routine | ok | 1698 | 1146/296 |
| var-18 | yes | Heating/Urgent | ok | 3122 | 1133/626 |
| var-19 | yes | Heating/Emergency | ok | 2455 | 1123/405 |
| var-20 | yes | Heating/Urgent | ok | 3047 | 1111/512 |
| var-21 | no | Unsure/Emergency | ok | 1739 | 1118/285 |
| var-22 | yes | Heating/Urgent | ok | 1605 | 1132/287 |
| var-23 | no | Plumbing/Emergency, Heating/Emergency | ok | 2366 | 1137/379 |
| var-24 | yes | LocksAndDoors/Urgent, Pest/Routine | ok | 2760 | 1158/538 |
| var-25 | yes | Appliances/Routine, Electrical/Routine, General/Routine | ok | 3532 | 1160/715 |
| var-26 | yes | Electrical/Routine | ok | 2113 | 1149/370 |
| var-27 | yes | Unsure/Routine | ok | 1288 | 1114/173 |
| var-28 | no | Pest/Routine | ok | 2252 | 1125/365 |
| var-29 | yes | Plumbing/Urgent | ok | 2357 | 1208/407 |
| var-30 | yes | Electrical/Emergency | ok | 1243 | 1180/173 |
| var-31 | yes | Electrical/Emergency | ok | 2018 | 1150/287 |
| var-32 | yes | LocksAndDoors/Routine | ok | 1124 | 1149/155 |
| var-33 | yes | Appliances/Routine | ok | 2380 | 1156/373 |
| var-34 | yes | Unsure/Routine | ok | 1117 | 1133/103 |
| var-35 | yes | Unsure/Routine | ok | 889 | 1129/52 |
| var-36 | yes | Unsure/Routine | ok | 764 | 1128/54 |
| var-37 | yes | Plumbing/Routine | ok | 1703 | 1148/273 |
| var-38 | yes | LocksAndDoors/Emergency | ok | 2447 | 1148/336 |
| var-39 | yes | LocksAndDoors/Urgent | ok | 2650 | 1139/477 |
| var-40 | yes | Pest/Routine | ok | 1369 | 1132/234 |
| var-41 | no | Pest/Emergency | ok | 2018 | 1137/327 |
| var-42 | no | Appliances/Urgent | ok | 2581 | 1128/386 |
| var-43 | no | Electrical/Urgent | ok | 3021 | 1139/537 |
| var-44 | no | General/Urgent | ok | 3402 | 1134/646 |
| var-45 | yes | Unsure/Routine | ok | 1179 | 1131/182 |
| var-46 | yes | General/Routine | ok | 1696 | 1114/267 |
