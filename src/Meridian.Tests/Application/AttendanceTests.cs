using System.Text.RegularExpressions;
using Meridian.Application.Attendance;
using Meridian.Application.Review;
using Meridian.Application.Templates;
using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;
using Meridian.Domain.Outbox;

namespace Meridian.Tests.Application;

/// <summary>Send on approval, the contractor's link, mark attended, resend and the overdue check, with a fake clock (AD-9).</summary>
public class AttendanceTests
{
    private static readonly DateTime T0 = new(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc);
    private static readonly Regex LinkToken = new(@"/c/([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant);

    private readonly SubmitMessageTests.FakeJobRepository _jobs = new();
    private readonly SubmitMessageTests.FakeUnitOfWork _uow = new();
    private readonly FakeClock _clock = new(T0);
    private readonly FakeOutbox _outbox = new();
    private readonly SubmitMessageTests.FakePropertyRepository _properties = new();
    private readonly SubmitMessageTests.FakeContractorRepository _contractors = new();

    [Fact]
    public async Task Approving_sends_the_job_to_its_contractor_with_one_link_that_opens_only_that_job()
    {
        var job = Prepared();
        var warmhold = SubmitMessageTests.FakeContractorRepository.Named("Warmhold Heating").Id;
        await Review().EditAsync(job.Id, "rachel", new JobEdit(Category: Category.Heating, ContractorId: warmhold), default);

        var approved = await Review().ApproveAsync(job.Id, "rachel", default);

        Assert.Equal(ReviewOutcome.Done, approved.Outcome);
        Assert.Equal(JobStatus.SentToContractor, job.Status);
        Assert.Equal(new[] { EventType.JobApproved, EventType.SentToContractor }, job.Events.TakeLast(2).Select(e => e.Type));
        var message = Assert.Single(_outbox.Sent);
        Assert.Equal(ContractorTemplates.JobId, message.TemplateId);
        Assert.Equal(OutboundChannels.Sms, message.Channel);
        Assert.Equal("Warmhold Heating (07700 900301)", message.Recipient);
        Assert.Equal(job.Id, message.JobId);
        Assert.Contains("Meridian job " + job.Reference + ": Heating at 14b Alder Court.", message.Body);
        Assert.Contains("Summary: no summary recorded.", message.Body);
        var token = LinkToken.Match(message.Body).Groups[1].Value;
        Assert.True(job.HasContractorToken(token));
        Assert.Contains(message.Id.ToString(), job.Events[^1].PayloadJson);
    }

    [Fact]
    public async Task The_contractor_view_shows_only_that_job_and_a_wrong_token_shows_nothing()
    {
        var job = await SentAsync();
        var token = LatestToken();

        var view = await View().ExecuteAsync(token, default);

        Assert.NotNull(view);
        Assert.Equal(job.Reference, view.Reference);
        Assert.Equal("14b Alder Court", view.Address);
        Assert.Equal("Heating", view.Category);
        Assert.Equal("Warmhold Heating", view.ContractorName);
        Assert.Null(await View().ExecuteAsync("not-a-real-token", default));
        Assert.Null(await View().ExecuteAsync(" ", default));
    }

    [Fact]
    public async Task Marking_attended_once_works_and_the_same_link_is_then_refused()
    {
        var job = await SentAsync();
        var token = LatestToken();
        var eventsBefore = job.Events.Count;

        var reference = await Attended().ExecuteAsync(token, default);

        Assert.Equal(job.Reference, reference);
        Assert.Equal(JobStatus.Attended, job.Status);
        Assert.Equal(eventsBefore + 1, job.Events.Count);
        Assert.Equal(EventType.JobAttended, job.Events[^1].Type);
        Assert.Null(await Attended().ExecuteAsync(token, default));
        Assert.Null(await View().ExecuteAsync(token, default));
        Assert.Equal(eventsBefore + 1, job.Events.Count);
    }

    [Fact]
    public async Task A_wrong_token_marks_nothing()
    {
        var job = await SentAsync();

        Assert.Null(await Attended().ExecuteAsync("random-string", default));

        Assert.Equal(JobStatus.SentToContractor, job.Status);
    }

    [Fact]
    public async Task An_emergency_25_hours_old_is_flagged_once_however_often_the_check_runs()
    {
        var job = await SentAsync();
        _clock.UtcNow = T0.AddHours(25);

        var first = await Overdue().ExecuteAsync(default);
        var second = await Overdue().ExecuteAsync(default);

        Assert.Equal(new[] { job.Reference }, first.ToArray());
        Assert.Empty(second);
        Assert.Single(job.Events, e => e.Type == EventType.AttendanceOverdue);
    }

    [Fact]
    public async Task Resend_sends_a_new_link_and_the_old_one_stops_working()
    {
        var job = await SentAsync();
        var oldToken = LatestToken();
        _clock.UtcNow = T0.AddHours(25);
        await Overdue().ExecuteAsync(default);
        var cuthbert = SubmitMessageTests.FakeContractorRepository.Named("Cottonfield Boiler Services").Id;

        var result = await Resend().ExecuteAsync(job.Id, "dawn", cuthbert, default);

        Assert.Equal(ReviewOutcome.Done, result.Outcome);
        Assert.Equal(2, _outbox.Sent.Count);
        var newMessage = _outbox.Sent[1];
        Assert.Equal("Cottonfield Boiler Services (07700 900302)", newMessage.Recipient);
        var newToken = LinkToken.Match(newMessage.Body).Groups[1].Value;
        Assert.NotEqual(oldToken, newToken);
        Assert.Null(await View().ExecuteAsync(oldToken, default));
        Assert.Null(await Attended().ExecuteAsync(oldToken, default));
        Assert.Equal("Cottonfield Boiler Services", (await View().ExecuteAsync(newToken, default))?.ContractorName);
        Assert.Null(job.AttendanceOverdueFlaggedAtUtc);
        Assert.Equal(JobStatus.SentToContractor, job.Status);
        Assert.Equal("dawn", job.Events[^1].Actor);
    }

    [Fact]
    public async Task Resending_clears_the_alert_and_the_resend_is_not_flagged_one_hour_later()
    {
        var job = await SentAsync();
        _clock.UtcNow = T0.AddHours(25);
        await Overdue().ExecuteAsync(default);
        Assert.NotNull(job.AttendanceOverdueFlaggedAtUtc);
        await Resend().ExecuteAsync(job.Id, "dawn", SubmitMessageTests.FakeContractorRepository.Named("Cottonfield Boiler Services").Id, default);
        Assert.Null(job.AttendanceOverdueFlaggedAtUtc);

        _clock.UtcNow = T0.AddHours(26);
        Assert.Empty(await Overdue().ExecuteAsync(default));

        Assert.Null(job.AttendanceOverdueFlaggedAtUtc);
        Assert.Single(job.Events, e => e.Type == EventType.AttendanceOverdue);
    }

    [Fact]
    public async Task A_resent_job_not_attended_4_hours_01_after_the_resend_is_flagged_again_once_per_send()
    {
        var job = await SentAsync();
        var cuthbert = SubmitMessageTests.FakeContractorRepository.Named("Cottonfield Boiler Services").Id;
        _clock.UtcNow = T0.AddHours(25);
        await Overdue().ExecuteAsync(default);
        await Resend().ExecuteAsync(job.Id, "dawn", cuthbert, default);

        _clock.UtcNow = T0.AddHours(29);
        Assert.Empty(await Overdue().ExecuteAsync(default));
        _clock.UtcNow = T0.AddHours(29).AddMinutes(1);
        Assert.Equal(new[] { job.Reference }, (await Overdue().ExecuteAsync(default)).ToArray());
        Assert.Empty(await Overdue().ExecuteAsync(default));
        Assert.Equal(2, job.Events.Count(e => e.Type == EventType.AttendanceOverdue));

        await Resend().ExecuteAsync(job.Id, "dawn", cuthbert, default);
        Assert.Null(job.AttendanceOverdueFlaggedAtUtc);
        _clock.UtcNow = T0.AddHours(33).AddMinutes(2);
        Assert.Single(await Overdue().ExecuteAsync(default));
        Assert.Equal(3, job.Events.Count(e => e.Type == EventType.AttendanceOverdue));
    }

    [Fact]
    public async Task A_resent_job_the_fallback_attended_is_never_flagged_again()
    {
        var job = await SentAsync();
        _clock.UtcNow = T0.AddHours(25);
        await Overdue().ExecuteAsync(default);
        await Resend().ExecuteAsync(job.Id, "dawn", SubmitMessageTests.FakeContractorRepository.Named("Cottonfield Boiler Services").Id, default);
        await Attended().ExecuteAsync(LatestToken(), default);

        _clock.UtcNow = T0.AddHours(40);

        Assert.Empty(await Overdue().ExecuteAsync(default));
        Assert.Single(job.Events, e => e.Type == EventType.AttendanceOverdue);
    }

    [Fact]
    public async Task Resend_refuses_an_unknown_person_a_missing_contractor_and_a_job_not_sent()
    {
        var job = await SentAsync();
        var cuthbert = SubmitMessageTests.FakeContractorRepository.Named("Cottonfield Boiler Services").Id;
        var eventsBefore = job.Events.Count;

        Assert.Equal("actingAs", (await Resend().ExecuteAsync(job.Id, "mallory", cuthbert, default)).Errors.Keys.Single());
        Assert.Equal("contractorId", (await Resend().ExecuteAsync(job.Id, "dawn", null, default)).Errors.Keys.Single());
        Assert.Equal(ReviewOutcome.NotFound, (await Resend().ExecuteAsync(Guid.NewGuid(), "dawn", cuthbert, default)).Outcome);
        Assert.Equal(eventsBefore, job.Events.Count);

        var prepared = Prepared();
        Assert.Equal(ReviewOutcome.Conflict, (await Resend().ExecuteAsync(prepared.Id, "dawn", cuthbert, default)).Outcome);
    }

    /// <summary>The token in the most recent contractor message: the only place the raw token is written.</summary>
    private string LatestToken() =>
        LinkToken.Match(_outbox.Sent.Last(o => o.TemplateId == ContractorTemplates.JobId).Body).Groups[1].Value;

    private async Task<Job> SentAsync()
    {
        var job = Prepared();
        var warmhold = SubmitMessageTests.FakeContractorRepository.Named("Warmhold Heating").Id;
        await Review().EditAsync(job.Id, "rachel", new JobEdit(Category: Category.Heating, ContractorId: warmhold), default);
        await Review().ApproveAsync(job.Id, "rachel", default);
        return job;
    }

    private Job Prepared()
    {
        var alder = SubmitMessageTests.FakePropertyRepository.Named("14b Alder Court").Id;
        var job = Job.CreateFromMessage(Message.Receive("m.walsh@example.com", "No heating", T0), "MER-000001", T0, alder);
        job.RaiseUrgency(Urgency.Emergency, "rule:no-heating-winter", T0);
        _jobs.Add(job);
        return job;
    }

    private ReviewJob Review() => new(_jobs, _properties, _contractors, _uow, _clock, new StaffDirectory(), _outbox);

    private ResendToContractor Resend() => new(_jobs, _properties, _contractors, _uow, _clock, _outbox, new StaffDirectory());

    private MarkAttended Attended() => new(_jobs, _uow, _clock);

    private GetContractorJob View() => new(_jobs, _properties, _contractors);

    private CheckOverdueAttendance Overdue() => new(_jobs, _uow, _clock);
}
