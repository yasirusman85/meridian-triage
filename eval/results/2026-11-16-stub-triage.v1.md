# Eval results: stub, 2026-11-16

Model `stub`, prompt `triage.v1`. 52 cases (49 maintenance, 54 expected issues), run through the real intake pipeline (AD-12). Scoring rules: `eval/README.md`.

**Result: FAIL**

| Threshold | Target | Result | |
|---|---|---|---|
| Emergency recall (SM-1) | 100% | 77.3% (17 of 22) | FAIL |
| Category accuracy (SM-7) | at least 85% | 18.5% (10 of 54) | FAIL |

## Summary

| Measure | Result |
|---|---|
| Emergency recall | 77.3% (17 of 22) |
| Missed emergencies | 5 |
| Category accuracy | 18.5% (10 of 54) |
| Urgency exact match | 70.4% (38 of 54) |
| Under-escalations | 11 |
| Over-escalations | 1 |
| Issue count match (maintenance cases) | 93.9% (46 of 49) |
| Property match | 14.8% (8 of 54) |
| Vulnerability flag match | 81.5% (44 of 54) |
| Non-maintenance handled (one Unsure job, not Emergency) | 100% (3 of 3) |
| Follow-up grouping | 96.2% (50 of 52) |
| Cases where the model gave no valid answer | 46 of 52 |
| Pipeline errors | 0 |
| Model latency, total | 0 ms |
| Tokens in / out | not reported |
| Estimated cost (USD) | not reported |

Model failures by reason (these cases fall back to the pipeline's Unsure job with the rules' urgency): `no-recorded-response` 46.

## Failures by harm

### Critical (5)

Missed emergencies. Any one blocks release.

| Case | Field | Expected | Actual |
|---|---|---|---|
| var-15 | urgency | Emergency | Routine |
| var-23 | missed_issue | Heating/Emergency | none |
| var-30 | urgency | Emergency | Routine |
| var-31 | urgency | Emergency | Routine |
| var-38 | urgency | Emergency | Routine |

### Tolerable (1)

Over-escalations. Tolerated; each is reviewed.

| Case | Field | Expected | Actual |
|---|---|---|---|
| var-23 | urgency | Routine | Emergency |

### Minor (103)

Wrong category or a wrong field on a non-urgent issue.

| Case | Field | Expected | Actual |
|---|---|---|---|
| var-01 | category | Heating | Unsure |
| var-01 | property_text | Flat 6, 18 Clarendon Road | null |
| var-02 | category | Heating | Unsure |
| var-02 | property_text | 3 Ferndale Mews | null |
| var-03 | category | Heating | Unsure |
| var-03 | property_text | Flat 11, Kingfisher House | null |
| var-04 | category | Heating | Unsure |
| var-04 | property_text | Mieszkanie 3, 41 Hollins Street | null |
| var-05 | category | Heating | Unsure |
| var-05 | vulnerability_mentioned | true | false |
| var-05 | property_text | 17 Reed Lane, Levenshulme | null |
| var-06 | category | Heating | Unsure |
| var-06 | property_text | Apartamentul 12, Orchard Mill | null |
| var-07 | category | Heating | Unsure |
| var-07 | property_text | 27 Ashby Grove | null |
| var-08 | category | Appliances | Unsure |
| var-08 | property_text | Flat 2, 9 Linden Court | null |
| var-09 | category | LocksAndDoors | Unsure |
| var-09 | property_text | 44 Mill Brow | null |
| var-10 | category | Plumbing | Unsure |
| var-10 | property_text | Flat 2, 7 Kingsway Court | null |
| var-11 | category | Plumbing | Unsure |
| var-11 | property_text | Flat 2, 7 Kingsway Court | null |
| var-11 | follow_up_of | attached to var-10 (MER-000015), no new job | attached to [], new jobs [MER-000016] |
| var-12 | category | Plumbing | Unsure |
| var-12 | property_text | Flat 2, 7 Kingsway Court | null |
| var-12 | follow_up_of | attached to var-10 (MER-000015), no new job | attached to [], new jobs [MER-000017] |
| var-13 | category | Plumbing | Unsure |
| var-13 | property_text | Flat 5, 60 Wilmslow Road | null |
| var-14 | category | Plumbing | Unsure |
| var-14 | property_text | Flat 8 Hawthorn Hse | null |
| var-15 | category | Plumbing | Unsure |
| var-15 | property_text | 12 Primrose Bank | null |
| var-16 | category | Heating | Unsure |
| var-16 | vulnerability_mentioned | true | false |
| var-16 | property_text | 5 Ladybarn Lane | null |
| var-17 | category | Heating | Unsure |
| var-17 | property_text | Flat 3, 2 Egerton Street | null |
| var-18 | category | Heating | Unsure |
| var-18 | urgency | Urgent | Routine |
| var-18 | vulnerability_mentioned | true | false |
| var-18 | property_text | Flat 1, 15 Cromwell Avenue | null |
| var-19 | category | Heating | Unsure |
| var-19 | property_text | 9 Rusholme Place | null |
| var-20 | category | Heating | Unsure |
| var-20 | urgency | Urgent | Routine |
| var-20 | property_text | 9 Rusholme Place | null |
| var-21 | category | Heating | Unsure |
| var-22 | category | Heating | Unsure |
| var-22 | urgency | Urgent | Routine |
| var-22 | property_text | Flat 7, 31 Albert Road | null |
| var-23 | issue_count | 2 | 1 |
| var-23 | category | Plumbing | Unsure |
| var-23 | property_text | 21 Granby Row | null |
| var-24 | issue_count | 2 | 1 |
| var-24 | category | LocksAndDoors | Unsure |
| var-24 | urgency | Urgent | Routine |
| var-24 | property_text | Flat 10, Elm Bank | null |
| var-24 | missed_issue | Pest/Routine | none |
| var-25 | issue_count | 3 | 1 |
| var-25 | category | Appliances | Unsure |
| var-25 | property_text | Flat 4, 50 Upper Chorlton Road | null |
| var-25 | missed_issue | Electrical/Routine | none |
| var-25 | missed_issue | General/Routine | none |
| var-26 | category | Electrical | Unsure |
| var-26 | property_text | Flat 2, 11 Station Approach | null |
| var-27 | property_text | 5 Moss Lane | null |
| var-28 | property_text | Flat 6, Victoria Mill | null |
| var-29 | category | Plumbing | Unsure |
| var-29 | urgency | Urgent | Routine |
| var-29 | property_text | 38 Brookfield Road | null |
| var-30 | category | Electrical | Unsure |
| var-30 | property_text | 4 Delamere Street | null |
| var-31 | category | Electrical | Unsure |
| var-31 | vulnerability_mentioned | true | false |
| var-31 | property_text | Flat 9, Rowan Court | null |
| var-32 | category | LocksAndDoors | Unsure |
| var-32 | property_text | Apartamentul 5, 30 Clarendon Road | null |
| var-33 | category | Appliances | Unsure |
| var-33 | property_text | فلیٹ 2، 9 Wellington Grove | null |
| var-37 | category | Plumbing | Unsure |
| var-37 | property_text | Flat 1, 6 Hope Street | null |
| var-38 | category | LocksAndDoors | Unsure |
| var-38 | vulnerability_mentioned | true | false |
| var-38 | property_text | 19 Thornbury Way | null |
| var-39 | category | LocksAndDoors | Unsure |
| var-39 | urgency | Urgent | Routine |
| var-39 | property_text | Flat 12, Albany Court | null |
| var-40 | category | Pest | Unsure |
| var-40 | property_text | Flat 3, 8 Wycliffe Street | null |
| var-41 | category | Pest | Unsure |
| var-41 | urgency | Urgent | Routine |
| var-41 | vulnerability_mentioned | true | false |
| var-41 | property_text | 2 Laurel Close | null |
| var-42 | category | Appliances | Unsure |
| var-42 | property_text | flat 5 31 Dover st | null |
| var-43 | category | Appliances | Unsure |
| var-43 | property_text | Flat 7, 27 Princess Street | null |
| var-44 | category | General | Unsure |
| var-44 | property_text | Pennine House, flat 4 | null |
| var-45 | property_text | 16 Heaton Road | null |
| var-46 | category | General | Unsure |
| var-46 | property_text | 33 Moorside Road | null |

## Cases

| Case | Correct | Produced | Model | Latency (ms) | Tokens in/out |
|---|---|---|---|---|---|
| real-01 | yes | Plumbing/Routine, Electrical/Routine | ok | 0 | - |
| real-02 | yes | Heating/Emergency | ok | 0 | - |
| real-03 | yes | Unsure/Routine (follow-up) | ok | 0 | - |
| real-04 | yes | Heating/Emergency (follow-up) | ok | 0 | - |
| real-05 | yes | Unsure/Urgent | ok | 0 | - |
| real-06 | yes | Heating/Urgent | ok | 0 | - |
| var-01 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-02 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-03 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-04 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-05 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-06 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-07 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-08 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-09 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-10 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-11 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-12 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-13 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-14 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-15 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-16 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-17 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-18 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-19 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-20 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-21 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-22 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-23 | no | Unsure/Emergency | no-recorded-response | 0 | - |
| var-24 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-25 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-26 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-27 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-28 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-29 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-30 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-31 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-32 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-33 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-34 | yes | Unsure/Routine | no-recorded-response | 0 | - |
| var-35 | yes | Unsure/Routine | no-recorded-response | 0 | - |
| var-36 | yes | Unsure/Routine | no-recorded-response | 0 | - |
| var-37 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-38 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-39 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-40 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-41 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-42 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-43 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-44 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-45 | no | Unsure/Routine | no-recorded-response | 0 | - |
| var-46 | no | Unsure/Routine | no-recorded-response | 0 | - |
