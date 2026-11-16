using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Eval;

/// <summary>Writes the machine (JSON) and human (Markdown) results of a run.</summary>
public static class EvalReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary><c>&lt;yyyy-MM-dd&gt;-&lt;model-id-sanitised&gt;</c>, e.g. <c>2026-11-16-anthropic-claude-haiku-5.5</c>.</summary>
    public static string BaseName(DateOnly date, string modelId) =>
        $"{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}-{Sanitise(modelId)}";

    /// <summary>Keeps letters, digits, '.', '_' and '-'; everything else (e.g. '/' or ':') becomes '-'.</summary>
    public static string Sanitise(string modelId)
    {
        var builder = new StringBuilder(modelId.Length);
        foreach (var c in modelId.Trim())
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? char.ToLowerInvariant(c) : '-');
        }

        var name = builder.ToString().Trim('-', '.');
        return name.Length == 0 ? "model" : name;
    }

    /// <summary>Writes both files to <paramref name="directory"/> and returns their paths.</summary>
    public static (string JsonPath, string MarkdownPath) Write(string directory, DateOnly date, EvalRun run, EvalScore score)
    {
        Directory.CreateDirectory(directory);
        var baseName = BaseName(date, run.ModelId);
        var jsonPath = Path.Combine(directory, baseName + ".json");
        var markdownPath = Path.Combine(directory, baseName + ".md");
        File.WriteAllText(jsonPath, ToJson(date, run, score) + "\n", new UTF8Encoding(false));
        File.WriteAllText(markdownPath, ToMarkdown(date, run, score), new UTF8Encoding(false));
        return (jsonPath, markdownPath);
    }

    public static string ToJson(DateOnly date, EvalRun run, EvalScore score)
    {
        var s = score.Summary;
        var document = new
        {
            date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            modelId = run.ModelId,
            promptVersion = run.PromptVersion,
            passed = s.Passed,
            thresholds = new
            {
                emergencyRecall = score.Thresholds.EmergencyRecall,
                emergencyRecallPassed = s.EmergencyRecallPassed,
                categoryAccuracy = score.Thresholds.CategoryAccuracy,
                categoryAccuracyPassed = s.CategoryAccuracyPassed,
            },
            summary = s,
            failures = score.Failures.ToList(),
            cases = score.Cases.Select(c => new
            {
                id = c.Case.Id,
                correct = c.Correct,
                expected = new
                {
                    isMaintenance = c.Case.IsMaintenance,
                    issues = c.Case.Issues,
                    followUpOf = c.Case.FollowUpOf,
                },
                actual = new
                {
                    issues = c.Outcome.Issues,
                    newJobs = c.Outcome.NewJobReferences,
                    attachedTo = c.Outcome.AttachedToReferences,
                },
                modelPrepared = c.Outcome.ModelPrepared,
                modelFailureReason = c.Outcome.ModelFailureReason,
                modelCalls = c.Outcome.ModelCalls,
                latencyMs = c.Outcome.LatencyMs,
                inputTokens = c.Outcome.InputTokens,
                outputTokens = c.Outcome.OutputTokens,
                costUsd = c.CostUsd,
                error = c.Outcome.Error,
                failures = c.Failures,
            }),
        };
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static string ToMarkdown(DateOnly date, EvalRun run, EvalScore score)
    {
        var s = score.Summary;
        var md = new StringBuilder();
        md.AppendLine($"# Eval results: {run.ModelId}, {date:yyyy-MM-dd}");
        md.AppendLine();
        md.AppendLine($"Model `{run.ModelId}`, prompt `{run.PromptVersion}`. {s.Cases} cases ({s.MaintenanceCases} maintenance, {s.ExpectedIssues} expected issues), run through the real intake pipeline (AD-12). Scoring rules: `eval/README.md`.");
        md.AppendLine();
        md.AppendLine($"**Result: {(s.Passed ? "PASS" : "FAIL")}**");
        md.AppendLine();
        md.AppendLine("| Threshold | Target | Result | |");
        md.AppendLine("|---|---|---|---|");
        md.AppendLine($"| Emergency recall (SM-1) | {Percent(score.Thresholds.EmergencyRecall)} | {Show(s.EmergencyRecall)} | {(s.EmergencyRecallPassed ? "pass" : "FAIL")} |");
        md.AppendLine($"| Category accuracy (SM-7) | at least {Percent(score.Thresholds.CategoryAccuracy)} | {Show(s.CategoryAccuracy)} | {(s.CategoryAccuracyPassed ? "pass" : "FAIL")} |");
        md.AppendLine();
        md.AppendLine("## Summary");
        md.AppendLine();
        md.AppendLine("| Measure | Result |");
        md.AppendLine("|---|---|");
        md.AppendLine($"| Emergency recall | {Show(s.EmergencyRecall)} |");
        md.AppendLine($"| Missed emergencies | {s.MissedEmergencies} |");
        md.AppendLine($"| Category accuracy | {Show(s.CategoryAccuracy)} |");
        md.AppendLine($"| Urgency exact match | {Show(s.UrgencyExact)} |");
        md.AppendLine($"| Under-escalations | {s.UnderEscalations} |");
        md.AppendLine($"| Over-escalations | {s.OverEscalations} |");
        md.AppendLine($"| Issue count match (maintenance cases) | {Show(s.IssueCountMatch)} |");
        md.AppendLine($"| Property match | {Show(s.PropertyMatch)} |");
        md.AppendLine($"| Vulnerability flag match | {Show(s.VulnerabilityMatch)} |");
        md.AppendLine($"| Non-maintenance handled (one Unsure job, not Emergency) | {Show(s.NonMaintenance)} |");
        md.AppendLine($"| Follow-up grouping | {Show(s.FollowUp)} |");
        md.AppendLine($"| Cases where the model gave no valid answer | {s.ModelFailures} of {s.Cases} |");
        md.AppendLine($"| Pipeline errors | {s.PipelineErrors} |");
        md.AppendLine($"| Model latency, total | {(s.LatencyMsTotal is null ? "not reported" : $"{s.LatencyMsTotal} ms")} |");
        md.AppendLine($"| Tokens in / out | {(s.InputTokens is null && s.OutputTokens is null ? "not reported" : $"{s.InputTokens ?? 0} / {s.OutputTokens ?? 0}")} |");
        md.AppendLine($"| Estimated cost (USD) | {(s.CostUsd is null ? "not reported" : s.CostUsd.Value.ToString("0.000000", CultureInfo.InvariantCulture))} |");
        md.AppendLine();

        var failureReasons = score.Cases
            .Where(c => !c.Outcome.ModelPrepared && c.Outcome.ModelFailureReason is not null)
            .GroupBy(c => c.Outcome.ModelFailureReason!)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        if (failureReasons.Count > 0)
        {
            md.AppendLine("Model failures by reason (these cases fall back to the pipeline's Unsure job with the rules' urgency): "
                + string.Join("; ", failureReasons.Select(g => $"`{g.Key}` {g.Count()}")) + ".");
            md.AppendLine();
        }

        md.AppendLine("## Failures by harm");
        md.AppendLine();
        foreach (var harm in new[] { Harm.Critical, Harm.Tolerable, Harm.Minor })
        {
            var group = score.Failures.Where(f => f.Harm == harm).ToList();
            md.AppendLine($"### {harm} ({group.Count})");
            md.AppendLine();
            md.AppendLine(harm switch
            {
                Harm.Critical => "Missed emergencies. Any one blocks release.",
                Harm.Tolerable => "Over-escalations. Tolerated; each is reviewed.",
                _ => "Wrong category or a wrong field on a non-urgent issue.",
            });
            md.AppendLine();
            if (group.Count == 0)
            {
                md.AppendLine("None.");
                md.AppendLine();
                continue;
            }

            md.AppendLine("| Case | Field | Expected | Actual |");
            md.AppendLine("|---|---|---|---|");
            foreach (var failure in group)
            {
                md.AppendLine($"| {failure.CaseId} | {failure.Field} | {Cell(failure.Expected)} | {Cell(failure.Actual)} |");
            }

            md.AppendLine();
        }

        var errors = score.Cases.Where(c => c.Outcome.Error is not null).ToList();
        if (errors.Count > 0)
        {
            md.AppendLine("## Pipeline errors");
            md.AppendLine();
            foreach (var c in errors)
            {
                md.AppendLine($"- {c.Case.Id}: {Cell(c.Outcome.Error!)}");
            }

            md.AppendLine();
        }

        md.AppendLine("## Cases");
        md.AppendLine();
        md.AppendLine("| Case | Correct | Produced | Model | Latency (ms) | Tokens in/out |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (var c in score.Cases)
        {
            var produced = c.Outcome.Issues.Count == 0
                ? "nothing"
                : string.Join(", ", c.Outcome.Issues.Select(i => $"{i.Category}/{i.Urgency}{(i.IsFollowUp ? " (follow-up)" : string.Empty)}"));
            var model = c.Outcome.ModelPrepared ? "ok" : (c.Outcome.ModelFailureReason ?? (c.Outcome.Error is null ? "no answer" : "error"));
            var tokens = c.Outcome.InputTokens is null && c.Outcome.OutputTokens is null ? "-" : $"{c.Outcome.InputTokens ?? 0}/{c.Outcome.OutputTokens ?? 0}";
            md.AppendLine($"| {c.Case.Id} | {(c.Correct ? "yes" : "no")} | {Cell(produced)} | {Cell(model)} | {c.Outcome.LatencyMs?.ToString(CultureInfo.InvariantCulture) ?? "-"} | {tokens} |");
        }

        return md.ToString();
    }

    private static string Show(Ratio ratio) =>
        ratio.Rate is null ? "n/a (0 of 0)" : $"{Percent(ratio.Rate.Value)} ({ratio.Correct} of {ratio.Total})";

    private static string Percent(double rate) => (rate * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string Cell(string text) => text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
