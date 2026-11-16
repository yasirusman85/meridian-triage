using Meridian.Application.Triage;
using Meridian.Domain.Jobs;

namespace Meridian.Eval;

/// <summary>How much a wrong answer could hurt (eval README, AD-12).</summary>
public enum Harm
{
    /// <summary>A missed emergency.</summary>
    Critical,

    /// <summary>Over-escalation: someone is woken or rushed unnecessarily.</summary>
    Tolerable,

    /// <summary>A wrong routine field a reviewer corrects with a click.</summary>
    Minor,
}

/// <summary>One wrong field on one case.</summary>
public sealed record EvalFailure(string CaseId, string Field, string Expected, string Actual, Harm Harm);

/// <summary>A count of correct answers out of a total. <see cref="Rate"/> is null when there is nothing to count.</summary>
public sealed record Ratio(int Correct, int Total)
{
    public double? Rate => Total == 0 ? null : (double)Correct / Total;
}

/// <summary>Per-token prices for the cost estimate, in USD per million tokens (AI-DESIGN.md defaults).</summary>
public sealed record EvalPrices(decimal InputPerMillion, decimal OutputPerMillion)
{
    public static readonly EvalPrices Default = new(0.10m, 0.50m);

    public decimal? Cost(int? inputTokens, int? outputTokens) =>
        inputTokens is null && outputTokens is null
            ? null
            : ((inputTokens ?? 0) * InputPerMillion + (outputTokens ?? 0) * OutputPerMillion) / 1_000_000m;
}

public sealed record EvalThresholds(double EmergencyRecall, double CategoryAccuracy)
{
    /// <summary>SM-1 and SM-7.</summary>
    public static readonly EvalThresholds Ship = new(1.0, 0.85);
}

public sealed record EvalSummary(
    int Cases,
    int MaintenanceCases,
    int ExpectedIssues,
    Ratio EmergencyRecall,
    Ratio CategoryAccuracy,
    Ratio UrgencyExact,
    int UnderEscalations,
    int MissedEmergencies,
    int OverEscalations,
    Ratio IssueCountMatch,
    Ratio PropertyMatch,
    Ratio VulnerabilityMatch,
    Ratio NonMaintenance,
    Ratio FollowUp,
    int ModelFailures,
    int PipelineErrors,
    long? LatencyMsTotal,
    int? InputTokens,
    int? OutputTokens,
    decimal? CostUsd,
    bool EmergencyRecallPassed,
    bool CategoryAccuracyPassed)
{
    public bool Passed => EmergencyRecallPassed && CategoryAccuracyPassed;
}

/// <summary>The score of one case.</summary>
public sealed record CaseScore(EvalCase Case, CaseOutcome Outcome, IReadOnlyList<EvalFailure> Failures, decimal? CostUsd)
{
    public bool Correct => Failures.Count == 0 && Outcome.Error is null;
}

public sealed record EvalScore(EvalSummary Summary, IReadOnlyList<CaseScore> Cases, EvalThresholds Thresholds)
{
    public IEnumerable<EvalFailure> Failures => Cases.SelectMany(c => c.Failures);
}

/// <summary>
/// Scores each case's outcome against its labels, per <c>eval/README.md</c>. Pure: no pipeline, no I/O.
/// </summary>
public static class EvalScorer
{
    public static EvalScore Score(
        IReadOnlyList<EvalCase> cases,
        IReadOnlyList<CaseOutcome> outcomes,
        EvalPrices? prices = null,
        EvalThresholds? thresholds = null)
    {
        prices ??= EvalPrices.Default;
        thresholds ??= EvalThresholds.Ship;
        var outcomesById = outcomes.ToDictionary(o => o.CaseId, StringComparer.Ordinal);
        var tally = new Tally();
        var scored = new List<CaseScore>();

        foreach (var evalCase in cases)
        {
            if (!outcomesById.TryGetValue(evalCase.Id, out var outcome))
            {
                throw new ArgumentException($"No outcome for case {evalCase.Id}.", nameof(outcomes));
            }

            var failures = new List<EvalFailure>();
            if (evalCase.IsMaintenance)
            {
                ScoreMaintenance(evalCase, outcome, failures, tally);
            }
            else
            {
                ScoreNonMaintenance(evalCase, outcome, failures, tally);
            }

            ScoreFollowUp(evalCase, outcome, outcomesById, failures, tally);
            tally.ModelFailures += outcome.ModelPrepared ? 0 : 1;
            tally.Errors += outcome.Error is null ? 0 : 1;
            scored.Add(new CaseScore(evalCase, outcome, failures, prices.Cost(outcome.InputTokens, outcome.OutputTokens)));
        }

        int? SumTokens(Func<CaseOutcome, int?> pick) =>
            outcomes.Any(o => pick(o) is not null) ? outcomes.Sum(o => pick(o) ?? 0) : null;

        var inputTokens = SumTokens(o => o.InputTokens);
        var outputTokens = SumTokens(o => o.OutputTokens);
        var recall = new Ratio(tally.EmergencyHits, tally.EmergencyTotal);
        var category = new Ratio(tally.CategoryCorrect, tally.ExpectedIssues);
        var summary = new EvalSummary(
            cases.Count,
            cases.Count(c => c.IsMaintenance),
            tally.ExpectedIssues,
            recall,
            category,
            new Ratio(tally.UrgencyExact, tally.ExpectedIssues),
            tally.Under,
            tally.MissedEmergencies,
            tally.Over,
            new Ratio(tally.IssueCountMatch, cases.Count(c => c.IsMaintenance)),
            new Ratio(tally.PropertyCorrect, tally.ExpectedIssues),
            new Ratio(tally.VulnerabilityCorrect, tally.ExpectedIssues),
            new Ratio(tally.NonMaintenanceCorrect, cases.Count(c => !c.IsMaintenance)),
            new Ratio(tally.FollowUpCorrect, cases.Count),
            tally.ModelFailures,
            tally.Errors,
            outcomes.Any(o => o.LatencyMs is not null) ? outcomes.Sum(o => o.LatencyMs ?? 0) : null,
            inputTokens,
            outputTokens,
            prices.Cost(inputTokens, outputTokens),
            (recall.Rate ?? 1.0) >= thresholds.EmergencyRecall,
            (category.Rate ?? 1.0) >= thresholds.CategoryAccuracy);

        return new EvalScore(summary, scored, thresholds);
    }

    /// <summary>
    /// Pairs actual issues with expected ones: by category first, then the rest by list order (README).
    /// Returns one entry per expected issue; the actual is null when it was missed.
    /// </summary>
    public static IReadOnlyList<(ExpectedIssue Expected, ActualIssue? Actual)> Pair(
        IReadOnlyList<ExpectedIssue> expected,
        IReadOnlyList<ActualIssue> actual)
    {
        var used = new bool[actual.Count];
        var pairs = new ActualIssue?[expected.Count];
        for (var e = 0; e < expected.Count; e++)
        {
            for (var a = 0; a < actual.Count; a++)
            {
                if (!used[a] && actual[a].Category == expected[e].Category)
                {
                    used[a] = true;
                    pairs[e] = actual[a];
                    break;
                }
            }
        }

        for (var e = 0; e < expected.Count; e++)
        {
            if (pairs[e] is not null)
            {
                continue;
            }

            for (var a = 0; a < actual.Count; a++)
            {
                if (!used[a])
                {
                    used[a] = true;
                    pairs[e] = actual[a];
                    break;
                }
            }
        }

        return expected.Select((issue, i) => (issue, pairs[i])).ToList();
    }

    /// <summary>
    /// The README's property rule: the output contains every number and name word of the expected value, ignoring
    /// case, punctuation and unit words (normalised as <see cref="PropertyResolver.Tokenize"/> does, so "St" and
    /// "Street" are the same word). An expected null needs a null output.
    /// </summary>
    public static bool PropertyMatches(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return string.IsNullOrWhiteSpace(actual);
        }

        var have = PropertyResolver.Tokenize(actual).ToHashSet(StringComparer.Ordinal);
        return PropertyResolver.Tokenize(expected).All(have.Contains);
    }

    private static void ScoreMaintenance(EvalCase evalCase, CaseOutcome outcome, List<EvalFailure> failures, Tally tally)
    {
        var id = evalCase.Id;
        if (outcome.Issues.Count == evalCase.Issues.Count)
        {
            tally.IssueCountMatch++;
        }
        else
        {
            failures.Add(new EvalFailure(id, "issue_count", evalCase.Issues.Count.ToString(), outcome.Issues.Count.ToString(), Harm.Minor));
        }

        foreach (var (expected, actual) in Pair(evalCase.Issues, outcome.Issues))
        {
            tally.ExpectedIssues++;
            var isEmergency = expected.Urgency == Urgency.Emergency;
            tally.EmergencyTotal += isEmergency ? 1 : 0;
            if (actual is null)
            {
                tally.MissedEmergencies += isEmergency ? 1 : 0;
                failures.Add(new EvalFailure(id, "missed_issue", $"{expected.Category}/{expected.Urgency}", "none",
                    isEmergency ? Harm.Critical : Harm.Minor));
                continue;
            }

            if (actual.Category == expected.Category)
            {
                tally.CategoryCorrect++;
            }
            else
            {
                failures.Add(new EvalFailure(id, "category", expected.Category.ToString(), actual.Category.ToString(), Harm.Minor));
            }

            if (isEmergency && actual.Urgency == Urgency.Emergency)
            {
                tally.EmergencyHits++;
            }

            if (actual.Urgency == expected.Urgency)
            {
                tally.UrgencyExact++;
            }
            else if (actual.Urgency > expected.Urgency)
            {
                tally.Over++;
                failures.Add(new EvalFailure(id, "urgency", expected.Urgency.ToString(), actual.Urgency.ToString(), Harm.Tolerable));
            }
            else
            {
                tally.Under++;
                tally.MissedEmergencies += isEmergency ? 1 : 0;
                failures.Add(new EvalFailure(id, "urgency", expected.Urgency.ToString(), actual.Urgency.ToString(),
                    isEmergency ? Harm.Critical : Harm.Minor));
            }

            if (actual.VulnerabilityMentioned == expected.VulnerabilityMentioned)
            {
                tally.VulnerabilityCorrect++;
            }
            else
            {
                failures.Add(new EvalFailure(id, "vulnerability_mentioned", Bool(expected.VulnerabilityMentioned),
                    Bool(actual.VulnerabilityMentioned), Harm.Minor));
            }

            if (PropertyMatches(expected.PropertyText, actual.PropertyText))
            {
                tally.PropertyCorrect++;
            }
            else
            {
                failures.Add(new EvalFailure(id, "property_text", expected.PropertyText ?? "null", actual.PropertyText ?? "null", Harm.Minor));
            }
        }
    }

    /// <summary>
    /// The pipeline never drops a message, so a non-maintenance case is correct when it makes exactly one Unsure job,
    /// not Emergency, and attaches to nothing.
    /// </summary>
    private static void ScoreNonMaintenance(EvalCase evalCase, CaseOutcome outcome, List<EvalFailure> failures, Tally tally)
    {
        var actual = outcome.Issues.Count == 1 ? outcome.Issues[0] : null;
        var correct = actual is { IsFollowUp: false, Category: Category.Unsure } && actual.Urgency != Urgency.Emergency
            && outcome.NewJobReferences.Count == 1 && outcome.AttachedToReferences.Count == 0;
        if (correct)
        {
            tally.NonMaintenanceCorrect++;
            return;
        }

        var description = outcome.Issues.Count == 0
            ? "no job"
            : string.Join(", ", outcome.Issues.Select(i => $"{i.Category}/{i.Urgency}{(i.IsFollowUp ? " (follow-up)" : string.Empty)}"));
        var harm = outcome.Issues.Any(i => i.Urgency == Urgency.Emergency) ? Harm.Tolerable : Harm.Minor;
        failures.Add(new EvalFailure(evalCase.Id, "is_maintenance", "one Unsure job, not Emergency", description, harm));
    }

    private static void ScoreFollowUp(
        EvalCase evalCase,
        CaseOutcome outcome,
        IReadOnlyDictionary<string, CaseOutcome> outcomesById,
        List<EvalFailure> failures,
        Tally tally)
    {
        var attached = outcome.AttachedToReferences;
        bool correct;
        string expected;
        if (evalCase.FollowUpOf is null)
        {
            expected = "not attached";
            correct = attached.Count == 0;
        }
        else
        {
            var earlier = outcomesById.TryGetValue(evalCase.FollowUpOf, out var e) ? e.NewJobReferences : Array.Empty<string>();
            expected = $"attached to {evalCase.FollowUpOf} ({(earlier.Count == 0 ? "no job" : string.Join(", ", earlier))}), no new job";
            correct = attached.Count > 0 && attached.All(earlier.Contains) && outcome.NewJobReferences.Count == 0;
        }

        if (correct)
        {
            tally.FollowUpCorrect++;
            return;
        }

        var actual = $"attached to [{string.Join(", ", attached)}], new jobs [{string.Join(", ", outcome.NewJobReferences)}]";
        failures.Add(new EvalFailure(evalCase.Id, "follow_up_of", expected, actual, Harm.Minor));
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private sealed class Tally
    {
        public int ExpectedIssues;
        public int EmergencyTotal;
        public int EmergencyHits;
        public int CategoryCorrect;
        public int UrgencyExact;
        public int Under;
        public int Over;
        public int MissedEmergencies;
        public int IssueCountMatch;
        public int PropertyCorrect;
        public int VulnerabilityCorrect;
        public int NonMaintenanceCorrect;
        public int FollowUpCorrect;
        public int ModelFailures;
        public int Errors;
    }
}
