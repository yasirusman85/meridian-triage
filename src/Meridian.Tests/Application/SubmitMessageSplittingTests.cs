using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Application.Messages;
using Meridian.Application.Outbox;
using Meridian.Application.Triage;
using Meridian.Domain.Jobs;
using Meridian.Infrastructure.Triage;
using Meridian.Tests.Domain;
using static Meridian.Tests.Application.SubmitMessageTests;

namespace Meridian.Tests.Application;

/// <summary>Messages become the right jobs (story 4): one test per row of the I/O matrix.</summary>
public class SubmitMessageSplittingTests
{
    // The client messages, verbatim (they have recorded stub answers).
    private const string JoyText =
        "Hi there the tap in the bathroom has been dripping for about two weeks now, it's not urgent but it's quite annoying at night. Also while I'm emailing, the light in the hallway cupboard doesn't work, I think it's just the bulb but I'm not allowed to change it myself am I. Flat 4, 22 Pemberton Road. Thanks, Joy";

    private const string WalshText =
        "URGENT URGENT no hot water and no heating at all since this morning, I have a baby in the house, this is unacceptable, I have called three times. 14b Alder Court. Please someone call me TONIGHT.";

    private const string WalshChaserText = "Still nothing. Michael Walsh, 14b Alder Court.";

    private const string WalshWifeText =
        "My husband has emailed twice about the heating at 14b Alder Court, no one has responded. We have a 7 month old baby. What is going on.";

    private const string NowakText =
        "Dzień dobry, w mieszkaniu jest pleśń na ścianie w sypialni, coraz gorzej, dziecko ma astmę. Mieszkanie 9, Beechwood House. Dziękuję.";

    private static readonly DateTime FridayEvening = new(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc);
    private static readonly DateTime SummerDay = new(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Two_issues_make_two_jobs_at_the_same_property_sharing_one_message()
    {
        var pipeline = new Pipeline(Stub(), new DateTime(2026, 1, 13, 10, 42, 0, DateTimeKind.Utc));

        var result = await pipeline.SubmitAsync("joy@example.com", JoyText);

        Assert.Equal(2, result.Jobs.Count);
        Assert.All(result.Jobs, j => Assert.False(j.IsFollowUp));
        Assert.Equal(2, pipeline.Jobs.Added.Count);
        var (tap, bulb) = (pipeline.Jobs.Added[0], pipeline.Jobs.Added[1]);
        Assert.Equal(Category.Plumbing, tap.Category);
        Assert.Equal(Category.Electrical, bulb.Category);
        var flat = FakePropertyRepository.Named("Flat 4, 22 Pemberton Road");
        Assert.Equal(flat.Id, tap.PropertyId);
        Assert.Equal(flat.Id, bulb.PropertyId);
        var message = Assert.Single(pipeline.Jobs.Messages);
        Assert.Same(message, Assert.Single(tap.Messages));
        Assert.Same(message, Assert.Single(bulb.Messages));
        Assert.NotEqual(tap.Reference, bulb.Reference);
        Assert.All(pipeline.Jobs.Added, AssertOwnPreparationEvents);
        Assert.Equal(1, IssueIndexOf(bulb));
    }

    [Fact]
    public async Task A_chaser_from_the_same_sender_is_attached_to_the_open_job()
    {
        var pipeline = new Pipeline(Stub(), FridayEvening);
        await pipeline.SubmitAsync("m.walsh@example.com", WalshText);

        pipeline.Clock.UtcNow = FridayEvening.AddMinutes(36);
        var result = await pipeline.SubmitAsync("m.walsh@example.com", WalshChaserText);

        var job = Assert.Single(pipeline.Jobs.Added);
        var submitted = Assert.Single(result.Jobs);
        Assert.True(submitted.IsFollowUp);
        Assert.Equal(job.Reference, submitted.Reference);
        Assert.Equal(2, job.Messages.Count);
        Assert.Equal(1, job.FollowUpCount);
        Assert.Equal(Category.Heating, job.Category);
        Assert.Equal(Urgency.Emergency, job.Urgency);
        AssertFollowUpAttached(job, pipeline.Jobs.Messages[1].Id);
    }

    [Fact]
    public async Task A_message_from_another_sender_about_the_same_flat_is_attached_to_the_same_job()
    {
        var pipeline = new Pipeline(Stub(), FridayEvening);
        await pipeline.SubmitAsync("m.walsh@example.com", WalshText);
        pipeline.Clock.UtcNow = FridayEvening.AddMinutes(36);
        await pipeline.SubmitAsync("m.walsh@example.com", WalshChaserText);

        pipeline.Clock.UtcNow = FridayEvening.AddMinutes(75);
        var result = await pipeline.SubmitAsync("s.walsh@example.com", WalshWifeText);

        var job = Assert.Single(pipeline.Jobs.Added);
        Assert.True(Assert.Single(result.Jobs).IsFollowUp);
        Assert.Equal(3, job.Messages.Count);
        Assert.Equal(2, job.FollowUpCount);
        Assert.Equal(2, job.Events.Count(e => e.Type == EventType.FollowUpAttached));
        Assert.Equal("s.walsh@example.com", job.Messages[2].Sender);
    }

    [Fact]
    public async Task A_different_category_at_the_same_property_is_a_new_job()
    {
        var model = new FakeTriageModel()
            .Returns(Answer(Issue("Heating", "Urgent", "14b Alder Court")))
            .Returns(Answer(Issue("Plumbing", "Routine", "14b Alder Court")));
        var pipeline = new Pipeline(model, SummerDay);
        await pipeline.SubmitAsync("m.walsh@example.com", "Radiator cold");

        var result = await pipeline.SubmitAsync("m.walsh@example.com", "Kitchen tap dripping");

        Assert.Equal(2, pipeline.Jobs.Added.Count);
        Assert.False(Assert.Single(result.Jobs).IsFollowUp);
        Assert.Equal(Category.Plumbing, pipeline.Jobs.Added[1].Category);
        Assert.Equal(pipeline.Jobs.Added[0].PropertyId, pipeline.Jobs.Added[1].PropertyId);
        Assert.All(pipeline.Jobs.Added, j => Assert.Equal(0, j.FollowUpCount));
    }

    [Fact]
    public async Task A_matching_job_opened_8_days_ago_gets_a_new_job()
    {
        var model = new FakeTriageModel()
            .Returns(Answer(Issue("Plumbing", "Routine", "Flat 2, 7 Kingsway Court")))
            .Returns(Answer(Issue("Plumbing", "Routine", "Flat 2, 7 Kingsway Court")));
        var pipeline = new Pipeline(model, SummerDay);
        await pipeline.SubmitAsync("tenant@example.com", "Toilet keeps running");

        pipeline.Clock.UtcNow = SummerDay.AddDays(8);
        var result = await pipeline.SubmitAsync("tenant@example.com", "Toilet still running");

        Assert.Equal(2, pipeline.Jobs.Added.Count);
        Assert.False(Assert.Single(result.Jobs).IsFollowUp);
    }

    [Fact]
    public async Task A_matching_job_that_was_rejected_gets_a_new_job()
    {
        var model = new FakeTriageModel()
            .Returns(Answer(Issue("Plumbing", "Routine", "Flat 2, 7 Kingsway Court")))
            .Returns(Answer(Issue("Plumbing", "Routine", "Flat 2, 7 Kingsway Court")));
        var pipeline = new Pipeline(model, SummerDay);
        await pipeline.SubmitAsync("tenant@example.com", "Toilet keeps running");
        TestJobs.SetStatus(pipeline.Jobs.Added[0], JobStatus.Rejected);

        pipeline.Clock.UtcNow = SummerDay.AddHours(2);
        var result = await pipeline.SubmitAsync("tenant@example.com", "Toilet still running");

        Assert.Equal(2, pipeline.Jobs.Added.Count);
        Assert.False(Assert.Single(result.Jobs).IsFollowUp);
    }

    [Theory]
    [InlineData("Flat 3, Nowhere Street", "Unresolved")] // unknown address
    [InlineData("Alder Court", "Ambiguous")] // a block with several flats
    [InlineData("", "NoAddress")] // the text names no property
    public async Task An_unresolved_property_leaves_the_property_unset_and_the_job_Unsure(string propertyText, string resolution)
    {
        var model = new FakeTriageModel().Returns(Answer(Issue("Plumbing", "Routine", propertyText)));
        var pipeline = new Pipeline(model, SummerDay);

        await pipeline.SubmitAsync("tenant@example.com", "Tap dripping");

        var job = Assert.Single(pipeline.Jobs.Added);
        Assert.Null(job.PropertyId);
        Assert.Equal(Category.Unsure, job.Category);
        var applied = Applied(job);
        Assert.Equal("Plumbing", applied.GetProperty("proposedCategory").GetString());
        Assert.Equal(resolution, applied.GetProperty("propertyResolution").GetString());
    }

    [Fact]
    public async Task Unresolved_jobs_are_never_grouped_as_follow_ups()
    {
        var model = new FakeTriageModel()
            .Returns(Answer(Issue("Plumbing", "Routine", "Flat 3, Nowhere Street")))
            .Returns(Answer(Issue("Plumbing", "Routine", "Flat 3, Nowhere Street")));
        var pipeline = new Pipeline(model, SummerDay);
        await pipeline.SubmitAsync("tenant@example.com", "Tap dripping");

        await pipeline.SubmitAsync("tenant@example.com", "Tap still dripping");

        Assert.Equal(2, pipeline.Jobs.Added.Count);
    }

    [Fact]
    public async Task A_translated_address_resolves_to_the_seeded_flat()
    {
        var pipeline = new Pipeline(Stub(), new DateTime(2026, 1, 14, 8, 3, 0, DateTimeKind.Utc));

        await pipeline.SubmitAsync("a.nowak@example.com", NowakText);

        var job = Assert.Single(pipeline.Jobs.Added);
        Assert.Equal(FakePropertyRepository.Named("Flat 9, Beechwood House").Id, job.PropertyId);
        Assert.Equal("Resolved", Applied(job).GetProperty("propertyResolution").GetString());
    }

    [Fact]
    public async Task A_follow_up_proposing_Emergency_raises_an_Urgent_job()
    {
        var model = new FakeTriageModel()
            .Returns(Answer(Issue("Plumbing", "Urgent", "Flat 2, 7 Kingsway Court")))
            .Returns(Answer(Issue("Plumbing", "Emergency", "Flat 2, 7 Kingsway Court")));
        var pipeline = new Pipeline(model, SummerDay);
        await pipeline.SubmitAsync("tenant@example.com", "Leak under the sink getting worse");
        var job = Assert.Single(pipeline.Jobs.Added);
        Assert.Equal(Urgency.Urgent, job.Urgency);

        pipeline.Clock.UtcNow = SummerDay.AddDays(2);
        await pipeline.SubmitAsync("tenant@example.com", "The cupboard is flooding and the water is near the sockets");

        Assert.Single(pipeline.Jobs.Added);
        Assert.Equal(Urgency.Emergency, job.Urgency);
        var types = job.Events.Select(e => e.Type).ToList();
        Assert.Equal(
            new[] { EventType.FollowUpAttached, EventType.UrgencyRaised, EventType.RulesApplied, EventType.AcknowledgementSent },
            types.TakeLast(4));
    }

    [Fact]
    public async Task A_follow_up_that_fires_an_emergency_rule_raises_the_job()
    {
        var model = new FakeTriageModel()
            .Returns(Answer(Issue("Heating", "Urgent", "8 Carlton Street")))
            .Returns(Answer(Issue("Unsure", "Routine", "8 Carlton Street", "low")));
        var pipeline = new Pipeline(model, SummerDay);
        await pipeline.SubmitAsync("tenant@example.com", "Boiler banging");

        await pipeline.SubmitAsync("tenant@example.com", "Now I can smell gas near it");

        var job = Assert.Single(pipeline.Jobs.Added);
        Assert.Equal(Urgency.Emergency, job.Urgency);
        Assert.Equal(new[] { "gas" }, job.RuleIds);
        Assert.Equal(2, job.Events.Count(e => e.Type == EventType.RulesApplied));
    }

    [Fact]
    public async Task Not_maintenance_makes_one_Unsure_job_flagged_for_a_person()
    {
        var model = new FakeTriageModel().Returns("""{"language":"en","issues":[]}""");
        var pipeline = new Pipeline(model, SummerDay);

        var result = await pipeline.SubmitAsync("newsletter@example.com", "Our summer offers are here!");

        var job = Assert.Single(pipeline.Jobs.Added);
        Assert.False(Assert.Single(result.Jobs).IsFollowUp);
        Assert.Equal(Category.Unsure, job.Category);
        Assert.Null(job.PropertyId);
        Assert.Equal(Job.NotMaintenanceExplanation, job.Explanation);
        Assert.True(Applied(job).GetProperty("notMaintenance").GetBoolean());
        AssertOwnPreparationEvents(job);
    }

    [Fact]
    public async Task Two_issues_matching_the_same_open_job_attach_the_message_once_with_the_higher_urgency()
    {
        var model = new FakeTriageModel()
            .Returns(Answer(Issue("Heating", "Routine", "8 Carlton Street")))
            .Returns(Answer(
                Issue("Heating", "Urgent", "8 Carlton Street"),
                Issue("Unsure", "Routine", "8 Carlton Street", "low")));
        var pipeline = new Pipeline(model, SummerDay);
        await pipeline.SubmitAsync("tenant@example.com", "Radiator cold");

        var result = await pipeline.SubmitAsync("tenant@example.com", "Radiator still cold and the boiler shows a code");

        var job = Assert.Single(pipeline.Jobs.Added);
        var submitted = Assert.Single(result.Jobs);
        Assert.True(submitted.IsFollowUp);
        Assert.Equal(2, job.Messages.Count);
        Assert.Single(job.Events, e => e.Type == EventType.FollowUpAttached);
        Assert.Equal(Urgency.Urgent, job.Urgency);
    }

    [Fact]
    public async Task One_issue_can_be_a_follow_up_while_another_in_the_same_message_is_a_new_job()
    {
        var model = new FakeTriageModel()
            .Returns(Answer(Issue("Heating", "Urgent", "8 Carlton Street")))
            .Returns(Answer(
                Issue("Heating", "Urgent", "8 Carlton Street"),
                Issue("Pest", "Routine", "8 Carlton Street")));
        var pipeline = new Pipeline(model, SummerDay);
        await pipeline.SubmitAsync("tenant@example.com", "Boiler banging");

        var result = await pipeline.SubmitAsync("tenant@example.com", "Boiler still banging, and there are mice in the kitchen");

        Assert.Equal(2, pipeline.Jobs.Added.Count);
        Assert.Equal(new[] { true, false }, result.Jobs.Select(j => j.IsFollowUp));
        var message = pipeline.Jobs.Messages[1];
        Assert.Contains(message, pipeline.Jobs.Added[0].Messages);
        Assert.Same(message, Assert.Single(pipeline.Jobs.Added[1].Messages));
    }

    [Fact]
    public async Task A_model_failure_makes_one_Unsure_job_and_never_attaches()
    {
        var model = new FakeTriageModel()
            .Returns(Answer(Issue("Heating", "Urgent", "8 Carlton Street")))
            .Fails(TriageFailureReasons.Unavailable);
        var pipeline = new Pipeline(model, SummerDay);
        await pipeline.SubmitAsync("tenant@example.com", "Boiler banging");

        var result = await pipeline.SubmitAsync("tenant@example.com", "Boiler still banging, 8 Carlton Street");

        Assert.Equal(2, pipeline.Jobs.Added.Count);
        Assert.False(Assert.Single(result.Jobs).IsFollowUp);
        var failed = pipeline.Jobs.Added[1];
        Assert.Equal(Category.Unsure, failed.Category);
        Assert.Null(failed.PropertyId);
        Assert.Equal(EventType.ModelPreparationFailed, failed.Events[^2].Type);
        Assert.Equal(EventType.AcknowledgementSent, failed.Events[^1].Type);
    }

    internal static string Issue(string category, string urgency, string propertyText, string confidence = "high") =>
        new JsonObject
        {
            ["summary_en"] = $"A {category} problem.",
            ["category"] = category,
            ["urgency"] = urgency,
            ["property_text"] = propertyText,
            ["explanation"] = $"Because it is {category}.",
            ["vulnerability_mentioned"] = false,
            ["confidence"] = confidence,
        }.ToJsonString();

    internal static string Answer(params string[] issues) =>
        $$"""{"language":"en","issues":[{{string.Join(",", issues)}}]}""";

    private static StubTriageModel Stub() => StubTriageModel.FromDirectory(StubTriageModel.DefaultDirectory);

    /// <summary>Every job made from a message has its own JobPrepared, RulesApplied and model outcome, in that order.</summary>
    private static void AssertOwnPreparationEvents(Job job)
    {
        var types = job.Events.Select(e => e.Type).ToList();
        Assert.Equal(EventType.MessageReceived, types[0]);
        Assert.Equal(EventType.JobPrepared, types[1]);
        Assert.Equal(EventType.RulesApplied, types[2]);
        Assert.Single(types, t => t is EventType.ModelPrepared or EventType.ModelPreparationFailed);
        Assert.True(types.FindIndex(t => t is EventType.ModelPrepared or EventType.ModelPreparationFailed) > 2);
        Assert.All(job.Events, e => Assert.Equal(job.Id, e.JobId));
    }

    private static void AssertFollowUpAttached(Job job, Guid messageId)
    {
        var attached = Assert.Single(job.Events, e => e.Type == EventType.FollowUpAttached);
        using var payload = JsonDocument.Parse(attached.PayloadJson);
        Assert.Equal(messageId, payload.RootElement.GetProperty("messageId").GetGuid());
        Assert.Equal(job.Reference, payload.RootElement.GetProperty("reference").GetString());
    }

    private static JsonElement Applied(Job job)
    {
        using var document = JsonDocument.Parse(Assert.Single(job.Events, e => e.Type == EventType.ModelPrepared).PayloadJson);
        return document.RootElement.GetProperty("applied").Clone();
    }

    private static int IssueIndexOf(Job job) => Applied(job).GetProperty("issueIndex").GetInt32();

    private sealed class Pipeline
    {
        private readonly SubmitMessage _submit;

        public Pipeline(ITriageModel model, DateTime now)
        {
            Clock = new FakeClock(now);
            _submit = new SubmitMessage(Jobs, new FakePropertyRepository(), new FakeUnitOfWork(), Clock, new PrepareJob(model), new AcknowledgeTenant(new FakeOutbox()));
        }

        public FakeJobRepository Jobs { get; } = new();

        public FakeClock Clock { get; }

        public async Task<SubmitMessageResult> SubmitAsync(string sender, string text)
        {
            var result = await _submit.ExecuteAsync(new SubmitMessageCommand(sender, text), default);
            Assert.True(result.Succeeded);
            return result;
        }
    }
}
