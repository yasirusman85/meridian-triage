using System.Text.Json;
using Meridian.Application.Messages;
using Meridian.Application.Outbox;
using Meridian.Application.Triage;
using Meridian.Domain.Jobs;
using Meridian.Infrastructure.Triage;
using static Meridian.Tests.Application.SubmitMessageTests;

namespace Meridian.Tests.Application;

/// <summary>The model step (story 2's I/O matrix), run through the intake pipeline on a message with one issue.</summary>
public class PrepareJobTests
{
    private const string NowakText =
        "Dzień dobry, w mieszkaniu jest pleśń na ścianie w sypialni, coraz gorzej, dziecko ma astmę. Mieszkanie 9, Beechwood House. Dziękuję.";

    private static readonly DateTime Received = new(2026, 11, 11, 8, 3, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Valid_recorded_response_prepares_the_job_from_the_real_stub()
    {
        var stub = StubTriageModel.FromDirectory(StubTriageModel.DefaultDirectory);
        var (outcome, job) = await RunAsync(stub, NowakText);

        Assert.True(outcome.Prepared);
        Assert.Equal(1, outcome.Attempts);
        Assert.Equal(Category.Unsure, job.Category);
        Assert.Equal(Urgency.Urgent, job.Urgency);
        Assert.Equal("pl", job.Language);
        Assert.False(string.IsNullOrWhiteSpace(job.SummaryEn));
        Assert.False(string.IsNullOrWhiteSpace(job.Explanation));
        Assert.Equal(
            new[] { EventType.MessageReceived, EventType.JobPrepared, EventType.RulesApplied, EventType.ModelPrepared, EventType.UrgencyRaised, EventType.AcknowledgementSent },
            job.Events.Select(e => e.Type));

        using var payload = JsonDocument.Parse(job.Events[3].PayloadJson);
        var root = payload.RootElement;
        Assert.Equal("stub", root.GetProperty("modelId").GetString());
        Assert.Equal("triage.v1", root.GetProperty("promptVersion").GetString());
        Assert.Equal(1, root.GetProperty("attemptCount").GetInt32());
        Assert.True(root.TryGetProperty("latencyMs", out _));
        var attempt = Assert.Single(root.GetProperty("attempts").EnumerateArray());
        Assert.True(TriageValidator.Validate(attempt.GetProperty("rawOutput").GetString()).IsValid);
        Assert.Empty(attempt.GetProperty("validationErrors").EnumerateArray());
    }

    [Fact]
    public async Task The_model_receives_only_the_text_and_received_time()
    {
        var model = new FakeTriageModel().Returns(TriageValidatorTests.ValidJson);
        await RunAsync(model, NowakText);

        var request = Assert.Single(model.Requests);
        Assert.Equal(new TriageRequest(NowakText, Received), request);
    }

    [Fact]
    public async Task Invalid_json_then_valid_prepares_from_the_second_attempt()
    {
        var model = new FakeTriageModel().Returns("{not json").Returns(TriageValidatorTests.ValidJson);
        var (outcome, job) = await RunAsync(model);

        Assert.True(outcome.Prepared);
        Assert.Equal(2, outcome.Attempts);
        Assert.Equal(Category.General, job.Category);
        var root = PayloadOf(job, EventType.ModelPrepared);
        Assert.Equal(2, root.GetProperty("attemptCount").GetInt32());
        var attempts = root.GetProperty("attempts").EnumerateArray().ToList();
        Assert.Equal("{not json", attempts[0].GetProperty("rawOutput").GetString());
        Assert.NotEmpty(attempts[0].GetProperty("validationErrors").EnumerateArray());
        Assert.Empty(attempts[1].GetProperty("validationErrors").EnumerateArray());
    }

    [Fact]
    public async Task Invalid_twice_leaves_the_job_unsure_and_records_the_errors()
    {
        var roofing = TriageValidatorTests.Mutate(i => i["category"] = "Roofing");
        var model = new FakeTriageModel().Returns(roofing).Returns(roofing).Returns(TriageValidatorTests.ValidJson);
        var (outcome, job) = await RunAsync(model);

        Assert.False(outcome.Prepared);
        Assert.Equal(2, model.Requests.Count);
        AssertUnchanged(job);
        var root = PayloadOf(job, EventType.ModelPreparationFailed);
        Assert.Equal(TriageFailureReasons.InvalidOutput, root.GetProperty("reason").GetString());
        Assert.Equal(2, root.GetProperty("attemptCount").GetInt32());
        Assert.All(
            root.GetProperty("attempts").EnumerateArray(),
            a => Assert.Contains(
                a.GetProperty("validationErrors").EnumerateArray(),
                e => e.GetString()!.Contains("Roofing", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Timeout_leaves_the_job_unsure_with_reason_timeout()
    {
        var model = new FakeTriageModel().Hangs();
        var (outcome, job) = await RunAsync(model, timeout: TimeSpan.FromMilliseconds(100));

        Assert.False(outcome.Prepared);
        Assert.Equal(TriageFailureReasons.Timeout, outcome.FailureReason);
        AssertUnchanged(job);
        var root = PayloadOf(job, EventType.ModelPreparationFailed);
        Assert.Equal("timeout", root.GetProperty("reason").GetString());
        Assert.Equal("stub", root.GetProperty("modelId").GetString());
        Assert.Equal("triage.v1", root.GetProperty("promptVersion").GetString());
    }

    [Fact]
    public async Task Timeout_is_enforced_even_if_the_adapter_ignores_cancellation()
    {
        var model = new FakeTriageModel().HangsIgnoringCancellation();
        var (outcome, job) = await RunAsync(model, timeout: TimeSpan.FromMilliseconds(100));

        Assert.Equal(TriageFailureReasons.Timeout, outcome.FailureReason);
        AssertUnchanged(job);
    }

    [Fact]
    public void Default_timeout_is_20_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(20), new PrepareJobOptions().Timeout);
    }

    [Fact]
    public async Task Model_exception_leaves_the_job_unsure_with_reason_unavailable_and_does_not_throw()
    {
        var model = new FakeTriageModel().Throws(new HttpRequestException("connection refused"));
        var (outcome, job) = await RunAsync(model);

        Assert.Equal(TriageFailureReasons.Unavailable, outcome.FailureReason);
        Assert.Equal(1, outcome.Attempts);
        AssertUnchanged(job);
        var root = PayloadOf(job, EventType.ModelPreparationFailed);
        Assert.Equal("unavailable", root.GetProperty("reason").GetString());
        var attempt = Assert.Single(root.GetProperty("attempts").EnumerateArray());
        Assert.Equal(nameof(HttpRequestException), attempt.GetProperty("exceptionType").GetString());
    }

    [Fact]
    public async Task No_recorded_answer_leaves_the_job_unsure_with_reason_no_recorded_response()
    {
        var stub = StubTriageModel.FromDirectory(StubTriageModel.DefaultDirectory);
        var (outcome, job) = await RunAsync(stub, "A message nobody recorded.");

        Assert.Equal(TriageFailureReasons.NoRecordedResponse, outcome.FailureReason);
        Assert.Equal(1, outcome.Attempts);
        AssertUnchanged(job);
        var root = PayloadOf(job, EventType.ModelPreparationFailed);
        Assert.Equal("no-recorded-response", root.GetProperty("reason").GetString());
        Assert.Equal("stub", root.GetProperty("modelId").GetString());
    }

    [Fact]
    public async Task Low_confidence_sets_category_unsure_but_keeps_explanation_and_raises_urgency()
    {
        var low = TriageValidatorTests.Mutate(i => i["confidence"] = "low");
        var (outcome, job) = await RunAsync(new FakeTriageModel().Returns(low));

        Assert.True(outcome.Prepared);
        Assert.Equal(Category.Unsure, job.Category);
        Assert.Equal("Mould, child with asthma.", job.Explanation);
        Assert.Equal(Urgency.Urgent, job.Urgency);
        var applied = PayloadOf(job, EventType.ModelPrepared).GetProperty("applied");
        Assert.Equal("General", applied.GetProperty("proposedCategory").GetString());
        Assert.Equal("Unsure", applied.GetProperty("category").GetString());
    }

    [Fact]
    public async Task A_proposal_lower_than_the_current_urgency_leaves_it_unchanged()
    {
        var routine = TriageValidatorTests.Mutate(i => i["urgency"] = "Routine");
        // The gas rule raises the job to Emergency before the model's Routine proposal is applied.
        var (outcome, job) = await RunAsync(new FakeTriageModel().Returns(routine), "I can smell gas in the kitchen");

        Assert.True(outcome.Prepared);
        Assert.Equal(Urgency.Emergency, job.Urgency);
        var raised = Assert.Single(job.Events, e => e.Type == EventType.UrgencyRaised);
        Assert.True(job.Events.ToList().IndexOf(raised) < job.Events.ToList().FindIndex(e => e.Type == EventType.ModelPrepared));
    }

    [Fact]
    public async Task An_adapter_failure_is_not_retried()
    {
        var model = new FakeTriageModel().Fails(TriageFailureReasons.Unavailable).Returns(TriageValidatorTests.ValidJson);
        var (outcome, job) = await RunAsync(model);

        Assert.False(outcome.Prepared);
        Assert.Single(model.Requests);
        AssertUnchanged(job);
    }

    [Fact]
    public async Task Caller_cancellation_still_records_exactly_one_outcome()
    {
        using var cts = new CancellationTokenSource();
        var model = new FakeTriageModel().Hangs();
        model.OnCall = () => cts.CancelAfter(50);
        var (outcome, job) = await RunAsync(model, cancellationToken: cts.Token);

        Assert.Equal(TriageFailureReasons.Cancelled, outcome.FailureReason);
        Assert.Single(job.Events, e => e.Type == EventType.ModelPreparationFailed);
    }

    private static void AssertUnchanged(Job job)
    {
        Assert.Equal(Category.Unsure, job.Category);
        Assert.Equal(Urgency.Routine, job.Urgency);
        Assert.Null(job.Explanation);
        Assert.Null(job.SummaryEn);
        Assert.Equal(
            new[] { EventType.MessageReceived, EventType.JobPrepared, EventType.RulesApplied, EventType.ModelPreparationFailed, EventType.AcknowledgementSent },
            job.Events.Select(e => e.Type));
    }

    private static JsonElement PayloadOf(Job job, EventType type)
    {
        using var document = JsonDocument.Parse(Assert.Single(job.Events, e => e.Type == type).PayloadJson);
        return document.RootElement.Clone();
    }

    /// <summary>Posts one message through the pipeline and returns the model step's outcome, read from the job's events.</summary>
    private static async Task<(Outcome Outcome, Job Job)> RunAsync(
        ITriageModel model,
        string text = "The hallway light does not work",
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var jobs = new FakeJobRepository();
        var clock = new FakeClock(Received);
        var options = timeout is null ? null : new PrepareJobOptions { Timeout = timeout.Value };
        var submit = new SubmitMessage(jobs, new FakePropertyRepository(), new FakeUnitOfWork(), clock, new PrepareJob(model, options), new AcknowledgeTenant(new FakeOutbox()));

        var result = await submit.ExecuteAsync(new SubmitMessageCommand("tenant@example.com", text), cancellationToken);

        Assert.True(result.Succeeded);
        var job = Assert.Single(jobs.Added);
        var outcomeEvent = Assert.Single(job.Events, e => e.Type is EventType.ModelPrepared or EventType.ModelPreparationFailed);
        using var payload = JsonDocument.Parse(outcomeEvent.PayloadJson);
        var root = payload.RootElement;
        var reason = root.GetProperty("reason");
        return (new Outcome(
            outcomeEvent.Type == EventType.ModelPrepared,
            root.GetProperty("attemptCount").GetInt32(),
            reason.ValueKind == JsonValueKind.Null ? null : reason.GetString()), job);
    }

    private sealed record Outcome(bool Prepared, int Attempts, string? FailureReason);
}
