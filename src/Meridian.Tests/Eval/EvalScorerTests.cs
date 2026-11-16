using Meridian.Domain.Jobs;
using Meridian.Eval;

namespace Meridian.Tests.Eval;

public class EvalScorerTests
{
    private static readonly DateTime Received = new(2026, 1, 16, 19, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Stub_fallback_scores_as_the_pipelines_unsure_job_with_the_rules_urgency()
    {
        var evalCase = Case("c1", new ExpectedIssue(Category.Heating, Urgency.Emergency, false, "14b Alder Court"));
        var outcome = Outcome("c1", Issue(Category.Unsure, Urgency.Emergency), prepared: false, failure: "no-recorded-response");

        var score = EvalScorer.Score(new[] { evalCase }, new[] { outcome });

        Assert.Equal(new Ratio(1, 1), score.Summary.EmergencyRecall);
        Assert.Equal(new Ratio(0, 1), score.Summary.CategoryAccuracy);
        Assert.Equal(1, score.Summary.ModelFailures);
        Assert.True(score.Summary.EmergencyRecallPassed);
        Assert.False(score.Summary.Passed);
        Assert.Contains(score.Failures, f => f is { Field: "category", Expected: "Heating", Actual: "Unsure", Harm: Harm.Minor });
        Assert.Contains(score.Failures, f => f is { Field: "property_text", Harm: Harm.Minor });
    }

    [Fact]
    public void Missed_emergency_is_critical_and_fails_recall()
    {
        var evalCase = Case("c1", new ExpectedIssue(Category.Electrical, Urgency.Emergency, false, null));
        var outcome = Outcome("c1", Issue(Category.Electrical, Urgency.Urgent));

        var score = EvalScorer.Score(new[] { evalCase }, new[] { outcome });

        var failure = Assert.Single(score.Failures);
        Assert.Equal(new EvalFailure("c1", "urgency", "Emergency", "Urgent", Harm.Critical), failure);
        Assert.Equal(new Ratio(0, 1), score.Summary.EmergencyRecall);
        Assert.Equal(1, score.Summary.MissedEmergencies);
        Assert.Equal(1, score.Summary.UnderEscalations);
        Assert.False(score.Summary.EmergencyRecallPassed);
        Assert.True(score.Summary.CategoryAccuracyPassed);
        Assert.False(score.Summary.Passed);
    }

    [Fact]
    public void Over_escalation_is_tolerable_and_a_lower_non_emergency_urgency_is_minor()
    {
        var cases = new[]
        {
            Case("c1", new ExpectedIssue(Category.Heating, Urgency.Urgent, false, null)),
            Case("c2", new ExpectedIssue(Category.Plumbing, Urgency.Urgent, false, null)),
        };
        var outcomes = new[]
        {
            Outcome("c1", Issue(Category.Heating, Urgency.Emergency)),
            Outcome("c2", Issue(Category.Plumbing, Urgency.Routine)),
        };

        var score = EvalScorer.Score(cases, outcomes);

        Assert.Contains(new EvalFailure("c1", "urgency", "Urgent", "Emergency", Harm.Tolerable), score.Failures);
        Assert.Contains(new EvalFailure("c2", "urgency", "Urgent", "Routine", Harm.Minor), score.Failures);
        Assert.Equal(1, score.Summary.OverEscalations);
        Assert.Equal(1, score.Summary.UnderEscalations);
        Assert.Equal(0, score.Summary.MissedEmergencies);
        Assert.True(score.Summary.Passed);
    }

    [Fact]
    public void Non_maintenance_is_correct_with_one_unsure_job_that_is_not_emergency()
    {
        var evalCase = NonMaintenance("spam");
        var outcome = Outcome("spam", Issue(Category.Unsure, Urgency.Routine), prepared: false, failure: "no-recorded-response");

        var score = EvalScorer.Score(new[] { evalCase }, new[] { outcome });

        Assert.Empty(score.Failures);
        Assert.Equal(new Ratio(1, 1), score.Summary.NonMaintenance);
        Assert.Equal(new Ratio(0, 0), score.Summary.CategoryAccuracy);
        Assert.True(score.Summary.Passed);
    }

    [Fact]
    public void Non_maintenance_raised_to_emergency_is_a_tolerable_failure()
    {
        var evalCase = NonMaintenance("spam");
        var outcome = Outcome("spam", Issue(Category.Unsure, Urgency.Emergency));

        var score = EvalScorer.Score(new[] { evalCase }, new[] { outcome });

        var failure = Assert.Single(score.Failures);
        Assert.Equal("is_maintenance", failure.Field);
        Assert.Equal(Harm.Tolerable, failure.Harm);
        Assert.Equal(new Ratio(0, 1), score.Summary.NonMaintenance);
    }

    [Fact]
    public void Non_maintenance_given_a_real_category_is_a_minor_failure()
    {
        var score = EvalScorer.Score(
            new[] { NonMaintenance("rent") },
            new[] { Outcome("rent", Issue(Category.General, Urgency.Routine)) });

        Assert.Equal(Harm.Minor, Assert.Single(score.Failures).Harm);
    }

    [Fact]
    public void A_missing_emergency_issue_is_critical_and_the_count_mismatch_is_minor()
    {
        var evalCase = Case(
            "c1",
            new ExpectedIssue(Category.Plumbing, Urgency.Routine, false, null),
            new ExpectedIssue(Category.Heating, Urgency.Emergency, false, null));
        var outcome = Outcome("c1", Issue(Category.Plumbing, Urgency.Routine));

        var score = EvalScorer.Score(new[] { evalCase }, new[] { outcome });

        Assert.Contains(new EvalFailure("c1", "issue_count", "2", "1", Harm.Minor), score.Failures);
        Assert.Contains(score.Failures, f => f is { Field: "missed_issue", Harm: Harm.Critical });
        Assert.Equal(new Ratio(0, 1), score.Summary.EmergencyRecall);
        Assert.Equal(new Ratio(1, 2), score.Summary.CategoryAccuracy);
        Assert.Equal(new Ratio(0, 1), score.Summary.IssueCountMatch);
    }

    [Fact]
    public void Issues_pair_by_category_first_then_by_order()
    {
        var expected = new[]
        {
            new ExpectedIssue(Category.Plumbing, Urgency.Routine, false, null),
            new ExpectedIssue(Category.Electrical, Urgency.Routine, false, null),
        };
        var electrical = Issue(Category.Electrical, Urgency.Routine);
        var plumbing = Issue(Category.Plumbing, Urgency.Routine);

        var pairs = EvalScorer.Pair(expected, new[] { electrical, plumbing });

        Assert.Same(plumbing, pairs[0].Actual);
        Assert.Same(electrical, pairs[1].Actual);
    }

    [Fact]
    public void Category_accuracy_threshold_is_85_percent()
    {
        var cases = Enumerable.Range(0, 20)
            .Select(i => Case($"c{i:00}", new ExpectedIssue(Category.Plumbing, Urgency.Routine, false, null)))
            .ToList();
        // 17 of 20 = 85% passes; 16 of 20 = 80% fails.
        var passing = cases.Select((c, i) => Outcome(c.Id, Issue(i < 17 ? Category.Plumbing : Category.General, Urgency.Routine))).ToList();
        var failing = cases.Select((c, i) => Outcome(c.Id, Issue(i < 16 ? Category.Plumbing : Category.General, Urgency.Routine))).ToList();

        Assert.True(EvalScorer.Score(cases, passing).Summary.CategoryAccuracyPassed);
        Assert.False(EvalScorer.Score(cases, failing).Summary.CategoryAccuracyPassed);
    }

    [Theory]
    [InlineData("Flat 9, Beechwood House", "Mieszkanie 9, Beechwood House", true)]
    [InlineData("14b Alder Court", "14B alder court.", true)]
    [InlineData("22 Pemberton Road", "22 Pemberton Rd", true)]
    [InlineData("Flat 6, 18 Clarendon Road", "18 Clarendon Road", false)]
    [InlineData("14b Alder Court", null, false)]
    [InlineData(null, null, true)]
    [InlineData(null, "14b Alder Court", false)]
    public void Property_rule_follows_the_readme(string? expected, string? actual, bool matches)
    {
        Assert.Equal(matches, EvalScorer.PropertyMatches(expected, actual));
    }

    [Fact]
    public void Follow_up_must_attach_to_the_earlier_cases_job_and_make_no_new_job()
    {
        var first = Case("a", new ExpectedIssue(Category.Heating, Urgency.Emergency, false, null));
        var followUp = Case("b", new ExpectedIssue(Category.Unsure, Urgency.Routine, false, null)) with { FollowUpOf = "a" };
        var firstOutcome = Outcome("a", Issue(Category.Heating, Urgency.Emergency, "MER-000001"));
        var attached = Outcome("b", Issue(Category.Unsure, Urgency.Routine, "MER-000001", followUp: true)) with
        {
            NewJobReferences = Array.Empty<string>(),
            AttachedToReferences = new[] { "MER-000001" },
        };
        var separate = Outcome("b", Issue(Category.Unsure, Urgency.Routine, "MER-000002")) with
        {
            NewJobReferences = new[] { "MER-000002" },
        };

        Assert.Empty(EvalScorer.Score(new[] { first, followUp }, new[] { firstOutcome, attached }).Failures);
        var failure = Assert.Single(EvalScorer.Score(new[] { first, followUp }, new[] { firstOutcome, separate }).Failures);
        Assert.Equal("follow_up_of", failure.Field);
    }

    [Fact]
    public void Cost_uses_the_configured_prices_and_is_absent_without_tokens()
    {
        var evalCase = Case("c1", new ExpectedIssue(Category.Plumbing, Urgency.Routine, false, null));
        var withTokens = Outcome("c1", Issue(Category.Plumbing, Urgency.Routine)) with { InputTokens = 1_200, OutputTokens = 300, LatencyMs = 900 };

        var score = EvalScorer.Score(new[] { evalCase }, new[] { withTokens }, EvalPrices.Default);
        var none = EvalScorer.Score(new[] { evalCase }, new[] { Outcome("c1", Issue(Category.Plumbing, Urgency.Routine)) });

        Assert.Equal(0.00027m, score.Summary.CostUsd);
        Assert.Equal(900, score.Summary.LatencyMsTotal);
        Assert.Null(none.Summary.CostUsd);
    }

    [Theory]
    [InlineData("stub", "2026-11-16-stub")]
    [InlineData("anthropic/claude-haiku-5.5", "2026-11-16-anthropic-claude-haiku-5.5")]
    [InlineData("anthropic/claude-haiku-5.5:batch", "2026-11-16-anthropic-claude-haiku-5.5-batch")]
    public void Results_are_named_after_the_date_and_model(string modelId, string expected)
    {
        Assert.Equal(expected, EvalReport.BaseName(new DateOnly(2026, 11, 16), modelId));
    }

    [Fact]
    public void Markdown_lists_thresholds_and_every_failure_under_its_harm()
    {
        var evalCase = Case("c1", new ExpectedIssue(Category.Electrical, Urgency.Emergency, false, null));
        var outcome = Outcome("c1", Issue(Category.General, Urgency.Urgent));
        var score = EvalScorer.Score(new[] { evalCase }, new[] { outcome });

        var markdown = EvalReport.ToMarkdown(new DateOnly(2026, 11, 16), new EvalRun("stub", "triage.v1", new[] { outcome }), score);

        Assert.Contains("| Emergency recall (SM-1) | 100% | 0% (0 of 1) | FAIL |", markdown);
        Assert.Contains("### Critical (1)", markdown);
        Assert.Contains("| c1 | urgency | Emergency | Urgent |", markdown);
        Assert.Contains("### Minor (1)", markdown);
        Assert.Contains("| c1 | category | Electrical | General |", markdown);
    }

    private static EvalCase Case(string id, params ExpectedIssue[] issues) =>
        new(id, id + ".json", Received, "tenant@example.com", "text", true, issues, null, null);

    private static EvalCase NonMaintenance(string id) =>
        new(id, id + ".json", Received, "tenant@example.com", "text", false, Array.Empty<ExpectedIssue>(), null, null);

    private static ActualIssue Issue(Category category, Urgency urgency, string reference = "MER-000001", bool followUp = false) =>
        new(category, urgency, false, null, reference, followUp);

    private static CaseOutcome Outcome(string id, ActualIssue issue, bool prepared = true, string? failure = null) =>
        new(id, new[] { issue }, issue.IsFollowUp ? Array.Empty<string>() : new[] { issue.JobReference },
            Array.Empty<string>(), prepared, failure, 1, null, null, null);
}
