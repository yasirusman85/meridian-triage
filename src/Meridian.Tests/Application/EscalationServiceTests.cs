using System.Text.Json;
using Meridian.Application.Escalation;
using Meridian.Application.Queue;
using Meridian.Application.Templates;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;
using Meridian.Domain.Outbox;
using Meridian.Domain.Rules;
using static Meridian.Tests.Application.SubmitMessageTests;

namespace Meridian.Tests.Application;

/// <summary>The escalation I/O matrix (R18, R19) with a fake clock.</summary>
public class EscalationServiceTests
{
    // The Walsh emergency: Friday 16 January 2026, 19:15.
    private static readonly DateTime Received = new(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc);

    private const string WalshText =
        "URGENT URGENT no hot water and no heating at all since this morning, I have a baby in the house, this is unacceptable, I have called three times. 14b Alder Court. Please someone call me TONIGHT.";

    [Fact]
    public async Task The_Walsh_emergency_with_no_acknowledgement_notifies_Rachel_then_Dawn_then_fails_each_step_in_its_history()
    {
        var (service, jobs, outbox, clock) = Create(Received);
        var job = Walsh();
        jobs.Add(job);

        // +0: Rachel.
        var first = Assert.Single(await service.TickAsync(default));
        Assert.Equal(EscalationService.NotifiedPrimaryStep, first.Step);
        Assert.Equal(EscalationState.NotifiedRachel, job.EscalationState);
        var rachel = Assert.Single(outbox.Sent);
        Assert.Equal(OutboundChannels.Sms, rachel.Channel);
        Assert.Equal("Rachel (07700 900461)", rachel.Recipient);
        Assert.Equal(StaffTemplates.EscalationPrimaryId, rachel.TemplateId);
        Assert.Equal(OutboundStatus.Simulated, rachel.Status);
        Assert.Equal(job.Id, rachel.JobId);
        Assert.Null(rachel.MessageId);
        Assert.Equal(
            StaffTemplates.Render(StaffTemplates.EscalationPrimaryId, job.Reference, "14b Alder Court", "Heating", new[] { "no-heating-winter" }),
            rachel.Body);
        Assert.Equal(
            "EMERGENCY MER-000001: Heating at 14b Alder Court. Rules: no-heating-winter. Please open the Meridian queue and acknowledge it.",
            rachel.Body);

        // +29: nothing yet.
        clock.UtcNow = Received.AddMinutes(29);
        Assert.Empty(await service.TickAsync(default));

        // +30: Dawn.
        clock.UtcNow = Received.AddMinutes(30);
        Assert.Equal(EscalationService.NotifiedBackupStep, Assert.Single(await service.TickAsync(default)).Step);
        Assert.Equal(EscalationState.NotifiedBackup, job.EscalationState);
        Assert.Equal(2, outbox.Sent.Count);
        var dawn = outbox.Sent[1];
        Assert.Equal("Dawn (07700 900872)", dawn.Recipient);
        Assert.Equal(StaffTemplates.EscalationBackupId, dawn.TemplateId);
        Assert.Equal(OutboundChannels.Sms, dawn.Channel);

        // +60: failed, no further message.
        clock.UtcNow = Received.AddMinutes(60);
        Assert.Equal(EscalationService.FailedStep, Assert.Single(await service.TickAsync(default)).Step);
        Assert.Equal(EscalationState.Failed, job.EscalationState);
        Assert.Equal(2, outbox.Sent.Count);
        Assert.Equal(Received, job.EscalationPrimaryNotifiedAtUtc);
        Assert.Equal(Received.AddMinutes(30), job.EscalationBackupNotifiedAtUtc);
        Assert.Equal(Received.AddMinutes(60), job.EscalationFailedAtUtc);

        // Each step is in the job's history.
        var steps = job.Events.Where(e => e.Type is EventType.EscalationNotified or EventType.EscalationFailed).ToList();
        Assert.Equal(new[] { EventType.EscalationNotified, EventType.EscalationNotified, EventType.EscalationFailed }, steps.Select(e => e.Type));
        Assert.Equal(new[] { Received, Received.AddMinutes(30), Received.AddMinutes(60) }, steps.Select(e => e.OccurredAtUtc));
        using (var payload = JsonDocument.Parse(steps[0].PayloadJson))
        {
            Assert.Equal("rachel", payload.RootElement.GetProperty("recipient").GetString());
            Assert.Equal(rachel.Id, payload.RootElement.GetProperty("outboundId").GetGuid());
        }

        using (var payload = JsonDocument.Parse(steps[1].PayloadJson))
        {
            Assert.Equal("dawn", payload.RootElement.GetProperty("recipient").GetString());
            Assert.Equal(dawn.Id, payload.RootElement.GetProperty("outboundId").GetGuid());
        }

        // Later ticks do nothing more.
        clock.UtcNow = Received.AddHours(5);
        Assert.Empty(await service.TickAsync(default));
        Assert.Equal(2, outbox.Sent.Count);
    }

    [Fact]
    public async Task Rachel_acknowledging_stops_escalation_and_Dawn_is_never_messaged()
    {
        var (service, jobs, outbox, clock) = Create(Received);
        var job = Walsh();
        jobs.Add(job);
        await service.TickAsync(default);

        clock.UtcNow = Received.AddMinutes(10);
        var result = await Acknowledge(jobs, clock).ExecuteAsync(job.Id, "rachel", default);

        Assert.Equal(AcknowledgeOutcome.Acknowledged, result.Outcome);
        Assert.Equal(EscalationState.Acknowledged, job.EscalationState);
        Assert.Equal("rachel", job.EscalationAcknowledgedBy);
        var ack = Assert.Single(job.Events, e => e.Type == EventType.EscalationAcknowledged);
        Assert.Equal("rachel", ack.Actor);

        foreach (var minutes in new[] { 30, 60, 120 })
        {
            clock.UtcNow = Received.AddMinutes(minutes);
            Assert.Empty(await service.TickAsync(default));
        }

        Assert.Equal("Rachel (07700 900461)", Assert.Single(outbox.Sent).Recipient);
    }

    [Fact]
    public async Task Dawn_acknowledging_after_her_notification_stops_escalation()
    {
        var (service, jobs, outbox, clock) = Create(Received);
        var job = Walsh();
        jobs.Add(job);
        await service.TickAsync(default);
        clock.UtcNow = Received.AddMinutes(30);
        await service.TickAsync(default);

        clock.UtcNow = Received.AddMinutes(45);
        var result = await Acknowledge(jobs, clock).ExecuteAsync(job.Id, "Dawn", default);

        Assert.Equal(AcknowledgeOutcome.Acknowledged, result.Outcome);
        Assert.Equal(EscalationState.Acknowledged, job.EscalationState);
        Assert.Equal("dawn", job.EscalationAcknowledgedBy);
        Assert.Equal(Received.AddMinutes(45), job.EscalationAcknowledgedAtUtc);
        clock.UtcNow = Received.AddMinutes(60);
        Assert.Empty(await service.TickAsync(default));
        Assert.Equal(2, outbox.Sent.Count);
        Assert.DoesNotContain(job.Events, e => e.Type == EventType.EscalationFailed);
    }

    [Fact]
    public async Task Two_ticks_in_the_same_minute_send_nothing_twice()
    {
        var (service, jobs, outbox, clock) = Create(Received);
        jobs.Add(Walsh());

        await service.TickAsync(default);
        clock.UtcNow = Received.AddSeconds(30);
        Assert.Empty(await service.TickAsync(default));

        clock.UtcNow = Received.AddMinutes(30);
        await service.TickAsync(default);
        clock.UtcNow = Received.AddMinutes(30).AddSeconds(20);
        Assert.Empty(await service.TickAsync(default));

        Assert.Equal(2, outbox.Sent.Count);
    }

    [Theory]
    [InlineData(Urgency.Urgent)]
    [InlineData(Urgency.Routine)]
    public async Task Urgent_and_Routine_jobs_are_never_escalated(Urgency urgency)
    {
        var (service, jobs, outbox, clock) = Create(Received);
        var job = Job.CreateFromMessage(Message.Receive("tenant@example.com", "Boiler banging", Received), "MER-000001", Received);
        job.RaiseUrgency(urgency, "model", Received);
        jobs.Add(job);

        foreach (var minutes in new[] { 0, 30, 60, 90 })
        {
            clock.UtcNow = Received.AddMinutes(minutes);
            Assert.Empty(await service.TickAsync(default));
        }

        Assert.Empty(outbox.Sent);
        Assert.Equal(EscalationState.None, job.EscalationState);
    }

    [Fact]
    public async Task An_Urgent_job_raised_to_Emergency_by_a_follow_up_starts_escalation_then()
    {
        var (service, jobs, outbox, clock) = Create(Received);
        var job = Job.CreateFromMessage(Message.Receive("tenant@example.com", "Heating is weak", Received), "MER-000001", Received);
        job.RaiseUrgency(Urgency.Urgent, "model", Received);
        jobs.Add(job);
        Assert.Empty(await service.TickAsync(default));

        var later = Received.AddHours(2);
        job.AttachFollowUp(Message.Receive("tenant@example.com", "Now there is water through the ceiling", later), Urgency.Emergency, later);
        clock.UtcNow = later.AddSeconds(40);
        Assert.Equal(EscalationService.NotifiedPrimaryStep, Assert.Single(await service.TickAsync(default)).Step);

        Assert.Equal(later.AddSeconds(40), job.EscalationPrimaryNotifiedAtUtc);
        Assert.Single(outbox.Sent);
        // Unresolved address and no rule fired: the template says so.
        Assert.Equal(
            "EMERGENCY MER-000001: Unsure at address not resolved. Rules: none. Please open the Meridian queue and acknowledge it.",
            outbox.Sent[0].Body);
    }

    [Fact]
    public async Task Acknowledging_a_Routine_job_is_refused_and_nothing_changes()
    {
        var (_, jobs, _, clock) = Create(Received);
        var job = Job.CreateFromMessage(Message.Receive("tenant@example.com", "Tap dripping", Received), "MER-000001", Received);
        jobs.Add(job);
        var events = job.Events.Count;

        var result = await Acknowledge(jobs, clock).ExecuteAsync(job.Id, "rachel", default);

        Assert.Equal(AcknowledgeOutcome.NotAcknowledgeable, result.Outcome);
        Assert.Equal(EscalationState.None, job.EscalationState);
        Assert.Null(job.EscalationAcknowledgedBy);
        Assert.Equal(events, job.Events.Count);
    }

    [Fact]
    public async Task Acknowledging_twice_is_refused_and_an_unknown_person_or_job_is_reported()
    {
        var (service, jobs, _, clock) = Create(Received);
        var job = Walsh();
        jobs.Add(job);
        await service.TickAsync(default);
        var acknowledge = Acknowledge(jobs, clock);

        Assert.Equal(AcknowledgeOutcome.UnknownStaff, (await acknowledge.ExecuteAsync(job.Id, "system", default)).Outcome);
        Assert.Equal(AcknowledgeOutcome.UnknownStaff, (await acknowledge.ExecuteAsync(job.Id, null, default)).Outcome);
        Assert.Equal(AcknowledgeOutcome.JobNotFound, (await acknowledge.ExecuteAsync(Guid.NewGuid(), "rachel", default)).Outcome);
        Assert.Equal(AcknowledgeOutcome.Acknowledged, (await acknowledge.ExecuteAsync(job.Id, "rachel", default)).Outcome);
        Assert.Equal(AcknowledgeOutcome.NotAcknowledgeable, (await acknowledge.ExecuteAsync(job.Id, "dawn", default)).Outcome);
        Assert.Equal("rachel", job.EscalationAcknowledgedBy);
        Assert.Single(job.Events, e => e.Type == EventType.EscalationAcknowledged);
    }

    [Fact]
    public async Task The_window_comes_from_options()
    {
        var jobs = new FakeJobRepository();
        var outbox = new FakeOutbox();
        var clock = new FakeClock(Received);
        var options = new EscalationOptions(
            new StaffMember("rachel", "Rachel", "07700 900461"),
            new StaffMember("dawn", "Dawn", "07700 900872"),
            5);
        var service = new EscalationService(jobs, new FakePropertyRepository(), new FakeUnitOfWork(), clock, outbox, options);
        jobs.Add(Walsh());

        await service.TickAsync(default);
        clock.UtcNow = Received.AddMinutes(5);
        Assert.Equal(EscalationService.NotifiedBackupStep, Assert.Single(await service.TickAsync(default)).Step);
    }

    [Fact]
    public async Task Escalation_failed_jobs_sort_to_the_top_then_Emergency_then_by_age()
    {
        var (service, jobs, _, clock) = Create(Received);
        var older = Job.CreateFromMessage(Message.Receive("a@example.com", "Water through the ceiling", Received.AddHours(-3)), "MER-000001", Received.AddHours(-3));
        older.RaiseUrgency(Urgency.Emergency, "model", Received.AddHours(-3));
        older.AcknowledgeEscalation("rachel", Received.AddHours(-3));
        var routine = Job.CreateFromMessage(Message.Receive("b@example.com", "Tap", Received.AddHours(-4)), "MER-000002", Received.AddHours(-4));
        var failed = Walsh("MER-000003");
        jobs.Add(older);
        jobs.Add(routine);
        jobs.Add(failed);

        await service.TickAsync(default);
        clock.UtcNow = Received.AddMinutes(30);
        await service.TickAsync(default);
        clock.UtcNow = Received.AddMinutes(60);
        await service.TickAsync(default);

        var items = await new ListQueue(jobs, new FakePropertyRepository(), new FakeContractorRepository()).ExecuteAsync(default);
        Assert.Equal(new[] { "MER-000003", "MER-000001", "MER-000002" }, items.Select(i => i.Reference));
        Assert.Equal(EscalationState.Failed, items[0].EscalationState);
        Assert.Equal(EscalationState.Acknowledged, items[1].EscalationState);
        Assert.Equal("rachel", items[1].EscalationAcknowledgedBy);
    }

    [Fact]
    public async Task Notifications_are_listed_newest_first_with_their_job_reference()
    {
        var (service, jobs, outbox, clock) = Create(Received);
        jobs.Add(Walsh());
        await service.TickAsync(default);
        clock.UtcNow = Received.AddMinutes(30);
        await service.TickAsync(default);

        var listed = await new ListNotifications(outbox, jobs).ExecuteAsync(default);

        Assert.Equal(new[] { StaffTemplates.EscalationBackupId, StaffTemplates.EscalationPrimaryId }, listed.Select(n => n.Message.TemplateId));
        Assert.All(listed, n => Assert.Equal("MER-000001", n.JobReference));
    }

    internal static Job Walsh(string reference = "MER-000001")
    {
        var alder = FakePropertyRepository.Named("14b Alder Court");
        var job = Job.CreateFromMessage(Message.Receive("m.walsh@example.com", WalshText, Received), reference, Received, alder.Id);
        job.ApplyRules(EmergencyRules.Evaluate(WalshText, Received), Received);
        job.ApplyModelPreparation(
            Category.Heating, Urgency.Emergency, "No heating with a baby in January.", "No heating or hot water.", "en", "{}", Received);
        Assert.Equal(Urgency.Emergency, job.Urgency);
        return job;
    }

    private static AcknowledgeEscalation Acknowledge(FakeJobRepository jobs, FakeClock clock) =>
        new(jobs, new FakeUnitOfWork(), clock, EscalationOptions.Default);

    private static (EscalationService Service, FakeJobRepository Jobs, FakeOutbox Outbox, FakeClock Clock) Create(DateTime now)
    {
        var jobs = new FakeJobRepository();
        var outbox = new FakeOutbox();
        var clock = new FakeClock(now);
        var service = new EscalationService(jobs, new FakePropertyRepository(), new FakeUnitOfWork(), clock, outbox, EscalationOptions.Default);
        return (service, jobs, outbox, clock);
    }
}
