using System.Text.Json;
using Meridian.Application.Messages;
using Meridian.Application.Triage;
using Meridian.Domain.Jobs;
using Meridian.Domain.Rules;
using Meridian.Infrastructure.Triage;
using static Meridian.Tests.Application.SubmitMessageTests;

namespace Meridian.Tests.Application;

/// <summary>The emergency rules in the intake pipeline: before the model, in the first transaction (AD-3).</summary>
public class SubmitMessageRulesTests
{
    // The Walsh message (client message 2), received on a Friday evening in January.
    private const string WalshText =
        "URGENT URGENT no hot water and no heating at all since this morning, I have a baby in the house, this is unacceptable, I have called three times. 14b Alder Court. Please someone call me TONIGHT.";

    private static readonly DateTime FridayInJanuary = new(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc);

    public static IEnumerable<object[]> AnyStubAnswer() => new[]
    {
        new object[] { "routine" },
        new object[] { "invalid" },
        new object[] { "unavailable" },
        new object[] { "timeout" },
        new object[] { "recorded" },
    };

    [Theory]
    [MemberData(nameof(AnyStubAnswer))]
    public async Task The_Walsh_message_in_January_is_Emergency_by_rule_whatever_the_model_answers(string answer)
    {
        ITriageModel model = answer switch
        {
            "routine" => new FakeTriageModel().Returns(TriageValidatorTests.Mutate(i => i["urgency"] = "Routine")),
            "invalid" => new FakeTriageModel().Returns("{not json").Returns("{not json"),
            "unavailable" => new FakeTriageModel().Throws(new HttpRequestException("down")),
            "timeout" => new FakeTriageModel().Hangs(),
            _ => StubTriageModel.FromDirectory(StubTriageModel.DefaultDirectory),
        };
        var (submit, jobs) = Create(model, FridayInJanuary);

        var result = await submit.ExecuteAsync(new SubmitMessageCommand("m.walsh@example.com", WalshText), default);

        Assert.True(result.Succeeded);
        var job = Assert.Single(jobs.Added);
        Assert.Equal(Urgency.Emergency, job.Urgency);
        Assert.Equal(new[] { EmergencyRuleIds.NoHeatingWinter }, job.RuleIds);
        AssertRulesAppliedBeforeModelOutcome(job);
    }

    [Fact]
    public async Task A_model_timeout_after_a_rule_hit_leaves_Emergency_and_Unsure()
    {
        var (submit, jobs) = Create(new FakeTriageModel().Hangs(), FridayInJanuary.AddMonths(6));

        await submit.ExecuteAsync(new SubmitMessageCommand("tenant@example.com", "I can smell gas in the kitchen"), default);

        var job = Assert.Single(jobs.Added);
        Assert.Equal(Urgency.Emergency, job.Urgency);
        Assert.Equal(Category.Unsure, job.Category);
        Assert.Equal(new[] { "gas" }, job.RuleIds);
        var failure = Assert.Single(job.Events, e => e.Type == EventType.ModelPreparationFailed);
        using var payload = JsonDocument.Parse(failure.PayloadJson);
        Assert.Equal(TriageFailureReasons.Timeout, payload.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Rules_run_before_the_model_and_every_job_gets_RulesApplied_then_the_model_outcome()
    {
        var jobs = new FakeJobRepository();
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(FridayInJanuary);
        var model = new FakeTriageModel();
        int? messagesWhenCalled = null;
        model.OnCall = () => messagesWhenCalled = jobs.Messages.Count;
        var submit = new SubmitMessage(jobs, new FakePropertyRepository(), uow, clock, new PrepareJob(model));

        await submit.ExecuteAsync(new SubmitMessageCommand("tenant@example.com", "water coming through the ceiling"), default);

        Assert.Equal(1, messagesWhenCalled);
        var job = Assert.Single(jobs.Added);
        Assert.Equal(
            new[] { EventType.MessageReceived, EventType.JobPrepared, EventType.RulesApplied, EventType.UrgencyRaised, EventType.ModelPreparationFailed },
            job.Events.Select(e => e.Type));
        Assert.Equal(2, uow.Executions);
    }

    [Theory]
    [InlineData("Tap dripping in the bathroom")]
    [InlineData("no heating")] // July: outside the winter window.
    public async Task No_rule_hit_still_records_an_empty_RulesApplied_before_the_model_outcome(string text)
    {
        var (submit, jobs) = Create(new FakeTriageModel(), new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc));

        await submit.ExecuteAsync(new SubmitMessageCommand("tenant@example.com", text), default);

        var job = Assert.Single(jobs.Added);
        Assert.Equal(Urgency.Routine, job.Urgency);
        Assert.Empty(job.RuleIds);
        var applied = Assert.Single(job.Events, e => e.Type == EventType.RulesApplied);
        using var payload = JsonDocument.Parse(applied.PayloadJson);
        Assert.Empty(payload.RootElement.GetProperty("rules").EnumerateArray());
        AssertRulesAppliedBeforeModelOutcome(job);
    }

    private static void AssertRulesAppliedBeforeModelOutcome(Job job)
    {
        var types = job.Events.Select(e => e.Type).ToList();
        var rules = types.IndexOf(EventType.RulesApplied);
        var outcome = types.FindIndex(t => t is EventType.ModelPrepared or EventType.ModelPreparationFailed);
        Assert.True(rules >= 0, "no RulesApplied event");
        Assert.True(outcome > rules, $"RulesApplied at {rules}, model outcome at {outcome}");
        Assert.True(types.IndexOf(EventType.JobPrepared) < rules);
    }

    private static (SubmitMessage Submit, FakeJobRepository Jobs) Create(ITriageModel model, DateTime now)
    {
        var jobs = new FakeJobRepository();
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(now);
        var prepare = new PrepareJob(model, new PrepareJobOptions { Timeout = TimeSpan.FromMilliseconds(100) });
        return (new SubmitMessage(jobs, new FakePropertyRepository(), uow, clock, prepare), jobs);
    }
}
