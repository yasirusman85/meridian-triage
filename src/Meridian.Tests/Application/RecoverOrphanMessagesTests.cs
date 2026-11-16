using System.Text.Json;
using Meridian.Application.Messages;
using Meridian.Application.Outbox;
using Meridian.Application.Templates;
using Meridian.Application.Triage;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;
using static Meridian.Tests.Application.SubmitMessageTests;

namespace Meridian.Tests.Application;

/// <summary>Orphan safety (RULE-11): a message stored without any job still becomes one Unsure job and is acknowledged.</summary>
public class RecoverOrphanMessagesTests
{
    private static readonly DateTime Received = new(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc);

    [Fact]
    public async Task An_orphan_becomes_one_Unsure_job_with_rules_applied_and_its_acknowledgement()
    {
        var (recover, jobs, outbox, clock) = Create(Received.AddMinutes(10));
        var orphan = Message.Receive("tenant@example.com", "I can smell gas in the kitchen", Received);
        jobs.AddMessage(orphan);

        var recovered = await recover.ExecuteAsync(TimeSpan.Zero, default);

        var job = Assert.Single(jobs.Added);
        Assert.Equal(job.Id, Assert.Single(recovered).JobId);
        Assert.Same(orphan, Assert.Single(job.Messages));
        Assert.Equal(Category.Unsure, job.Category);
        Assert.Null(job.PropertyId);
        Assert.Equal(Urgency.Emergency, job.Urgency);
        Assert.Contains("gas", job.RuleIds);
        Assert.Equal(Received, job.ReceivedAtUtc);
        Assert.Equal(clock.UtcNow, job.CreatedAtUtc);

        var types = job.Events.Select(e => e.Type).ToList();
        Assert.Equal(
            new[]
            {
                EventType.MessageReceived, EventType.JobPrepared, EventType.RulesApplied, EventType.UrgencyRaised,
                EventType.ModelPreparationFailed, EventType.AcknowledgementSent,
            },
            types);
        using (var failure = JsonDocument.Parse(job.Events.Single(e => e.Type == EventType.ModelPreparationFailed).PayloadJson))
        {
            Assert.Equal(TriageFailureReasons.Interrupted, failure.RootElement.GetProperty("reason").GetString());
        }

        var sent = Assert.Single(outbox.Sent);
        Assert.Equal(TenantTemplates.GasSafetyId, sent.TemplateId);
        Assert.Equal(TenantTemplates.Render(TenantTemplates.GasSafetyId, job.Reference), sent.Body);
        Assert.Equal("tenant@example.com", sent.Recipient);
        Assert.Equal(orphan.Id, sent.MessageId);
        using var ack = JsonDocument.Parse(job.Events[^1].PayloadJson);
        Assert.Equal(sent.Id, ack.RootElement.GetProperty("outboundId").GetGuid());
    }

    [Fact]
    public async Task Messages_that_have_jobs_are_left_alone_and_a_second_run_does_nothing()
    {
        var (recover, jobs, outbox, _) = Create(Received.AddMinutes(10));
        var handled = Message.Receive("a@example.com", "Tap dripping", Received);
        jobs.AddMessage(handled);
        jobs.Add(Job.CreateFromMessage(handled, "MER-000099", Received));
        jobs.AddMessage(Message.Receive("b@example.com", "Hallway bulb out", Received.AddMinutes(1)));

        Assert.Single(await recover.ExecuteAsync(TimeSpan.Zero, default));
        Assert.Empty(await recover.ExecuteAsync(TimeSpan.Zero, default));

        Assert.Equal(2, jobs.Added.Count);
        Assert.Equal("b@example.com", Assert.Single(outbox.Sent).Recipient);
        Assert.Equal(TenantTemplates.AcknowledgementId, outbox.Sent[0].TemplateId);
    }

    [Fact]
    public async Task A_message_younger_than_the_minimum_age_may_still_be_in_its_intake_and_is_left_alone()
    {
        var (recover, jobs, outbox, clock) = Create(Received.AddSeconds(30));
        jobs.AddMessage(Message.Receive("tenant@example.com", "Boiler broken", Received));

        Assert.Empty(await recover.ExecuteAsync(RecoverOrphanMessages.DefaultMinimumAge, default));
        Assert.Empty(jobs.Added);
        Assert.Empty(outbox.Sent);

        clock.UtcNow = Received + RecoverOrphanMessages.DefaultMinimumAge + TimeSpan.FromSeconds(1);
        Assert.Single(await recover.ExecuteAsync(RecoverOrphanMessages.DefaultMinimumAge, default));
        Assert.Single(outbox.Sent);
    }

    [Fact]
    public async Task A_negative_minimum_age_is_rejected()
    {
        var (recover, _, _, _) = Create(Received);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => recover.ExecuteAsync(TimeSpan.FromSeconds(-1), default));
    }

    private static (RecoverOrphanMessages Recover, FakeJobRepository Jobs, FakeOutbox Outbox, FakeClock Clock) Create(DateTime now)
    {
        var jobs = new FakeJobRepository();
        var outbox = new FakeOutbox();
        var clock = new FakeClock(now);
        return (new RecoverOrphanMessages(jobs, new FakeUnitOfWork(), clock, new AcknowledgeTenant(outbox)), jobs, outbox, clock);
    }
}
