using System.Text.Json;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;

namespace Meridian.Tests.Domain;

public class JobFollowUpTests
{
    private static readonly DateTime Now = new(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc);

    [Fact]
    public void AttachFollowUp_links_the_message_and_appends_FollowUpAttached_with_message_id_and_reference()
    {
        var job = NewJob();
        var chaser = Message.Receive("m.walsh@example.com", "Still nothing.", Now.AddMinutes(36));

        job.AttachFollowUp(chaser, Urgency.Routine, Now.AddMinutes(36));

        Assert.Equal(2, job.Messages.Count);
        Assert.Same(chaser, job.Messages[1]);
        Assert.Equal(1, job.FollowUpCount);
        var attached = job.Events[^1];
        Assert.Equal(EventType.FollowUpAttached, attached.Type);
        Assert.Equal(JobEvent.SystemActor, attached.Actor);
        using var payload = JsonDocument.Parse(attached.PayloadJson);
        Assert.Equal(chaser.Id, payload.RootElement.GetProperty("messageId").GetGuid());
        Assert.Equal(job.Reference, payload.RootElement.GetProperty("reference").GetString());
    }

    [Fact]
    public void A_follow_up_raises_urgency_through_RaiseUrgency()
    {
        var job = NewJob();
        job.RaiseUrgency(Urgency.Urgent, "model", Now);

        job.AttachFollowUp(Message.Receive("a@example.com", "Now it is pouring through", Now), Urgency.Emergency, Now);

        Assert.Equal(Urgency.Emergency, job.Urgency);
        var raised = job.Events[^1];
        Assert.Equal(EventType.UrgencyRaised, raised.Type);
        using var payload = JsonDocument.Parse(raised.PayloadJson);
        Assert.Equal("follow-up", payload.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void A_lower_follow_up_proposal_never_lowers_urgency()
    {
        var job = NewJob();
        job.RaiseUrgency(Urgency.Emergency, "rule:no-heating-winter", Now);

        job.AttachFollowUp(Message.Receive("a@example.com", "Still nothing.", Now), Urgency.Routine, Now);

        Assert.Equal(Urgency.Emergency, job.Urgency);
        Assert.Equal(EventType.FollowUpAttached, job.Events[^1].Type);
    }

    [Fact]
    public void The_same_message_cannot_be_attached_twice()
    {
        var job = NewJob();
        var chaser = Message.Receive("a@example.com", "Still nothing.", Now);
        job.AttachFollowUp(chaser, Urgency.Routine, Now);

        Assert.Throws<InvalidOperationException>(() => job.AttachFollowUp(chaser, Urgency.Routine, Now));
        Assert.Throws<InvalidOperationException>(() => job.AttachFollowUp(job.Messages[0], Urgency.Routine, Now));
    }

    [Theory]
    [InlineData(JobStatus.Rejected)]
    [InlineData(JobStatus.Attended)]
    public void A_closed_job_takes_no_follow_ups(JobStatus status)
    {
        var job = NewJob();
        TestJobs.SetStatus(job, status);

        Assert.False(job.IsOpen);
        Assert.Throws<InvalidOperationException>(() =>
            job.AttachFollowUp(Message.Receive("a@example.com", "Still nothing.", Now), Urgency.Routine, Now));
    }

    [Fact]
    public void A_job_records_its_resolved_property_in_JobPrepared()
    {
        var propertyId = Guid.NewGuid();

        var job = Job.CreateFromMessage(Message.Receive("a@example.com", "Tap", Now), "MER-000001", Now, propertyId);

        Assert.Equal(propertyId, job.PropertyId);
        using var payload = JsonDocument.Parse(job.Events[1].PayloadJson);
        Assert.Equal(propertyId, payload.RootElement.GetProperty("propertyId").GetGuid());
    }

    [Fact]
    public void ApplyNotMaintenance_keeps_Unsure_flags_the_job_for_a_person_and_records_the_model_outcome()
    {
        var job = NewJob();

        job.ApplyNotMaintenance("en", """{"applied":{"notMaintenance":true}}""", Now);

        Assert.Equal(Category.Unsure, job.Category);
        Assert.Equal(Urgency.Routine, job.Urgency);
        Assert.Equal(Job.NotMaintenanceExplanation, job.Explanation);
        Assert.Null(job.SummaryEn);
        Assert.Equal("en", job.Language);
        Assert.Equal(EventType.ModelPrepared, job.Events[^1].Type);
        Assert.Throws<InvalidOperationException>(() => job.RecordModelFailure("{}", Now));
    }

    private static Job NewJob() =>
        Job.CreateFromMessage(Message.Receive("m.walsh@example.com", "No heating", Now), "MER-000002", Now, Guid.NewGuid());
}

/// <summary>Test-only helpers for job states that have no domain method yet (review and attendance stories).</summary>
internal static class TestJobs
{
    public static void SetStatus(Job job, JobStatus status) =>
        typeof(Job).GetProperty(nameof(Job.Status))!.SetValue(job, status);
}
