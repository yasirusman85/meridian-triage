# Triage evaluation set

The labelled test set for the AI triage step (PRD "AI behavior and evaluation", SM-1, SM-7). There is one JSON file per case in `cases/`.

## Where it came from

- **6 client messages** (`real-01`..`real-06`, `"source": "client"`): the six messages Dawn forwarded, with the text copied verbatim from `docs/input/client-input.md`. The client gave only a weekday and a time, so `received_at` uses a January 2026 date with that weekday and time. This matters for `real-02`: it is an emergency only because it falls in the October to April window.
- **46 written variations** (`var-01`..`var-46`, `"source": "written"`): written by the developer with AI assistance to cover the cases the PRD lists. Names, addresses and email addresses are made up. None of them come from real tenants.

## Case format

```json
{"id": "var-07", "source": "client|written", "received_at": "ISO-8601 UTC", "sender": "...", "text": "...",
 "expected": {"is_maintenance": true,
              "issues": [{"category": "...", "urgency": "...", "vulnerability_mentioned": true, "property_text": "..."}],
              "follow_up_of": null},
 "harm_if_wrong": "critical|tolerable|minor", "notes": "why this label"}
```

## What "correct" means, per field

Each check is made on the system's prepared output, after the emergency rules and the AI have both run. The final urgency is the higher of the two (FR-9).

| Field | Correct when |
|---|---|
| `is_maintenance` | The pipeline never drops a message (RULE-9, RULE-11): every message makes at least one job for a person to see. So a `false` case is correct when it produces exactly one job, in the Unsure category, not at Emergency, attached to no earlier job. |
| issue count | The number of issues produced equals `len(expected.issues)`. Issues are paired with expected issues by category first and then by list order. An issue left without a pair counts as missed, and an extra issue counts as spurious. |
| `category` | The category is an exact match, from the eight values. |
| `urgency` | The final urgency is an exact match. If it is higher than expected, the case is counted as **over-escalation**. If it is lower, the case is counted as **under-escalation**. If the expected urgency is Emergency and the output is lower, it is a **missed emergency**. |
| `vulnerability_mentioned` | The boolean is an exact match. |
| `property_text` | The output contains every number and every street or building name word from the expected value. Case and punctuation are ignored, and unit words (Flat, Apt, Mieszkanie, Apartamentul, فلیٹ) are not compared, so a translated "Flat 9, Beechwood House" passes. When the expected value is `null`, the output must be `null`: the AI must not invent an address. |
| `follow_up_of` | Run the referenced case first. The follow-up message must then be attached to that case's job and must not create a new job. When the value is `null`, the message must not be attached to any job in the run. |

For a follow-up, `issues` describes the message as the AI should read it on its own. Attaching can only raise the job's urgency. So `real-03` ("Still nothing") is labelled Unsure/Routine, and its job stays Emergency through `real-02`. `var-12` is a follow-up that has to raise its job to Emergency.

How the labels follow the rules:
- The emergency rules apply as written. Any mention of gas or a gas smell is an Emergency, and that includes gas described without the word ("smells like eggs", a CO alarm) and gas in Polish, Urdu or Romanian. So is water coming through a ceiling, and so is no heating from October to April. The month is the UK local month of `received_at`.
- The gas rule is broad on purpose. `var-09` (a broken door on the gas meter cupboard) is therefore expected at Emergency, and a person lowers it.
- Some cases are emergencies that no rule covers: a sparking socket, a power cut with medical equipment on battery, a toddler locked out at night, a bulging wet ceiling. For these the AI is expected to raise the urgency to Emergency.
- Damp and mould go to Unsure. A boiler that bangs but still works is Urgent. Instructions written inside a message are ignored in both directions (`var-07`, `var-37`).
- Gas with no clear source is labelled Heating, because a gas engineer handles it. Gas from an appliance such as a hob is labelled Appliances.

## Weighting by harm

Each case carries `harm_if_wrong`:
- **critical**: a wrong answer here could mean a missed emergency. Every case with an expected Emergency is critical. The one exception is `var-09`, where the risk is only over-escalation.
- **tolerable**: the likely error is over-escalation, such as a banging boiler raised to Emergency, or no heating in July.
- **minor**: the likely error is a wrong routine category or a wrong field on a non-urgent case.

Errors are reported in these groups and not as a single score:
1. **Missed emergencies.** These must be 0. Any miss blocks release, and so does any miss on a gas case.
2. **Over-escalations.** These are counted and reported. They are tolerated, and each one is reviewed.
3. **Category errors** on the remaining issues. These are minor and count toward SM-7.
4. **Injection failures.** Any output that followed an instruction inside the message is reported separately.

## How to run

From the repository root:

```sh
dotnet run --project src/Meridian.Eval                       # stub model (no key needed)
OPENROUTER_API_KEY=... dotnet run --project src/Meridian.Eval # OpenRouter, model from Triage:Model
dotnet run --project src/Meridian.Eval -- --cases eval/cases --out eval/results --Triage:Model=openai/gpt-6-luna
```

`Meridian.Eval` (AD-12) runs every case, in id order, through the product's own intake pipeline (`SubmitMessage`: emergency rules, model, validator, urgency merge, property resolution, follow-up matching, splitting into jobs) with the Infrastructure services, against a throwaway SQLite database that is migrated and seeded like the app's and deleted afterwards. Each message is received at its case's `received_at`. All cases share the one database, so a follow-up finds its earlier case's job, and an unrelated case at the same property within 7 days can be grouped with it, as it would be in the product.

How the runner reads the output:
- An issue that became its own job is scored on the job: its final category and urgency.
- An issue attached to an earlier job is scored as the message read on its own (see above): the category the pipeline gives that issue and the higher of the rules and the model's urgency.
- `vulnerability_mentioned` and `property_text` come from the model's validated answer. When the model gave no valid answer, the message's one Unsure job is the only issue, with no vulnerability flag and no property.
- `property_text` words are normalised as `PropertyResolver` does (unit words dropped, common abbreviations such as St and Rd expanded).

With the stub, only the six client messages have recorded answers. Every other case falls back as the product does when the model is unavailable (one Unsure job, rules' urgency), so a stub run shows what the rules alone catch; it is expected to fail the thresholds.

Results go to `eval/results/<yyyy-MM-dd>-<model id>.json` (every case, every failure) and `.md` (summary, thresholds, failures grouped by harm: critical, tolerable, minor). Latency and tokens are totalled when the model reports them; the cost estimate uses `Eval:InputPricePerMillion` and `Eval:OutputPricePerMillion` (USD, defaults 0.10 and 0.50 from AI-DESIGN.md). Exit code 0 when both thresholds pass, 1 when either fails, 2 when a case file is unusable (the message names the file).

Harm classes per failure: an expected Emergency that ends lower, or is missed, is **critical**; an urgency higher than expected (and a non-maintenance message raised to Emergency) is **tolerable**; everything else (category, a lower non-emergency urgency, issue count, vulnerability, property, follow-up grouping) is **minor**. Injection failures are not yet reported separately; read the injection cases (`var-07`, `var-37`) in the per-case list.

## Thresholds to ship

- **Emergency recall: 100%** (SM-1). Every issue labelled Emergency must come out at Emergency.
- **Category accuracy: at least 85%** (SM-7). This is the share of expected issues, across all maintenance cases, that receive the correct category.

Run the set on the provider and model actually in use, and run it again whenever the model or the prompt changes.

## Limits

- **It is small.** There are 52 messages and 54 expected issues. A single error shifts category accuracy by about 2 points. A 100% emergency recall here does not show that the recall is 100% on real traffic.
- **The person who built the system wrote it.** The variations reflect what the developer expected to go wrong, written with AI help. So they probably share blind spots with the prompt and the rules.
- **It does not follow the real traffic mix.** Emergencies, other languages and edge cases are over-represented on purpose. Rates measured here are not the rates Meridian would see.
- **Some labels are judgement calls.** These are the emergencies that no rule covers (`var-15`, `var-30`, `var-31`, `var-38`), `real-04`, `real-05` (Urgent), and the Unsure label on `var-28`. Rachel should review the labels before anyone relies on them.
- **Only text is covered.** Photos (`real-05`) are not scored, and there are no WhatsApp messages.
