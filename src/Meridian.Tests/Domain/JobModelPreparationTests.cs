using System.Text.Json;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;

namespace Meridian.Tests.Domain;

public class JobModelPreparationTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 8, 3, 0, DateTimeKind.Utc);
    private const string Payload = "{\"modelId\":\"stub\"}";

    [Fact]
    public void ApplyModelPreparation_sets_fields_and_appends_ModelPrepared_then_UrgencyRaised()
    {
        var job = NewJob();

        job.ApplyModelPreparation(Category.General, Urgency.Urgent, "Mould, child with asthma.", "Mould on bedroom wall.", "pl", Payload, Now.AddSeconds(2));

        Assert.Equal(Category.General, job.Category);
        Assert.Equal(Urgency.Urgent, job.Urgency);
        Assert.Equal("Mould, child with asthma.", job.Explanation);
        Assert.Equal("Mould on bedroom wall.", job.SummaryEn);
        Assert.Equal("pl", job.Language);
        Assert.Equal(
            new[] { EventType.MessageReceived, EventType.JobPrepared, EventType.ModelPrepared, EventType.UrgencyRaised },
            job.Events.Select(e => e.Type));
        Assert.Equal(Payload, job.Events[2].PayloadJson);
        using var raised = JsonDocument.Parse(job.Events[3].PayloadJson);
        Assert.Equal("Routine", raised.RootElement.GetProperty("from").GetString());
        Assert.Equal("Urgent", raised.RootElement.GetProperty("to").GetString());
        Assert.Equal("model", raised.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void ApplyModelPreparation_with_Routine_does_not_append_UrgencyRaised()
    {
        var job = NewJob();

        job.ApplyModelPreparation(Category.Plumbing, Urgency.Routine, "Dripping tap.", "Tap drips.", "en", Payload, Now);

        Assert.DoesNotContain(job.Events, e => e.Type == EventType.UrgencyRaised);
        Assert.Equal(Urgency.Routine, job.Urgency);
    }

    [Fact]
    public void RaiseUrgency_never_lowers()
    {
        var job = NewJob();
        Assert.True(job.RaiseUrgency(Urgency.Emergency, "test", Now));
        var count = job.Events.Count;

        Assert.False(job.RaiseUrgency(Urgency.Routine, "test", Now));
        Assert.False(job.RaiseUrgency(Urgency.Urgent, "test", Now));
        Assert.False(job.RaiseUrgency(Urgency.Emergency, "test", Now));

        Assert.Equal(Urgency.Emergency, job.Urgency);
        Assert.Equal(count, job.Events.Count);
    }

    [Fact]
    public void RecordModelFailure_appends_the_event_and_changes_nothing_else()
    {
        var job = NewJob();

        job.RecordModelFailure("{\"reason\":\"timeout\"}", Now);

        Assert.Equal(EventType.ModelPreparationFailed, job.Events[^1].Type);
        Assert.Equal(Category.Unsure, job.Category);
        Assert.Equal(Urgency.Routine, job.Urgency);
        Assert.Null(job.Explanation);
        Assert.Null(job.SummaryEn);
    }

    [Fact]
    public void Only_one_model_outcome_can_be_recorded()
    {
        var job = NewJob();
        job.RecordModelFailure(Payload, Now);

        Assert.Throws<InvalidOperationException>(() => job.RecordModelFailure(Payload, Now));
        Assert.Throws<InvalidOperationException>(() =>
            job.ApplyModelPreparation(Category.General, Urgency.Routine, "x", "y", "en", Payload, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1]")]
    public void Payload_must_be_a_json_object(string payload)
    {
        var job = NewJob();

        Assert.Throws<ArgumentException>(() => job.RecordModelFailure(payload, Now));
    }

    [Fact]
    public void Model_methods_reject_non_utc_times()
    {
        var job = NewJob();
        var local = DateTime.SpecifyKind(Now, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => job.RecordModelFailure(Payload, local));
        Assert.Throws<ArgumentException>(() => job.RaiseUrgency(Urgency.Urgent, "test", local));
    }

    private static Job NewJob() =>
        Job.CreateFromMessage(Message.Receive("tenant@example.com", "text", Now), "MER-000001", Now);
}
