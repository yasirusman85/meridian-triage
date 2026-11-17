using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;

namespace Meridian.Tests.Domain;

/// <summary>Contractor attendance on the job (AD-6, AD-11, RULE-8): send, token hash, attended, overdue and resend.</summary>
public class JobAttendanceTests
{
    private static readonly DateTime T0 = new(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc);
    private static readonly Guid Contractor1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Contractor2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Token1 = "token-one-abcdefghijklmnopqrstuvwxyz0123456789";
    private const string Token2 = "token-two-abcdefghijklmnopqrstuvwxyz0123456789";

    [Fact]
    public void Sending_an_approved_job_stores_only_the_token_hash_and_records_the_contractor_and_outbound()
    {
        var job = Approved(urgency: Urgency.Emergency);
        var outbound = Guid.NewGuid();

        job.SendToContractor(Contractor1, Token1, outbound, "rachel", T0.AddMinutes(1));

        Assert.Equal(JobStatus.SentToContractor, job.Status);
        Assert.Equal(Job.HashContractorToken(Token1), job.ContractorTokenHash);
        Assert.DoesNotContain(Token1, job.ContractorTokenHash);
        Assert.True(job.HasContractorToken(Token1));
        Assert.False(job.HasContractorToken("wrong"));
        var sent = job.Events[^1];
        Assert.Equal(EventType.SentToContractor, sent.Type);
        Assert.Equal("rachel", sent.Actor);
        Assert.Contains(Contractor1.ToString(), sent.PayloadJson);
        Assert.Contains(outbound.ToString(), sent.PayloadJson);
    }

    [Fact]
    public void Sending_a_job_that_is_not_approved_throws_and_changes_nothing()
    {
        var job = Prepared();
        var before = job.Events.Count;

        Assert.Throws<Meridian.Domain.Jobs.JobStateException>(() => job.SendToContractor(Contractor1, Token1, Guid.NewGuid(), "rachel", T0));

        Assert.Equal(JobStatus.Prepared, job.Status);
        Assert.Null(job.ContractorTokenHash);
        Assert.Equal(before, job.Events.Count);
    }

    [Fact]
    public void Marking_attended_moves_to_Attended_appends_JobAttended_and_invalidates_the_token()
    {
        var job = Approved(urgency: Urgency.Emergency);
        job.SendToContractor(Contractor1, Token1, Guid.NewGuid(), "rachel", T0.AddMinutes(1));

        job.MarkAttended(T0.AddHours(3));

        Assert.Equal(JobStatus.Attended, job.Status);
        Assert.Equal(T0.AddHours(3), job.AttendedAtUtc);
        Assert.Null(job.ContractorTokenHash);
        Assert.False(job.HasContractorToken(Token1));
        var attended = job.Events[^1];
        Assert.Equal(EventType.JobAttended, attended.Type);
        Assert.Equal("contractor:" + Contractor1.ToString("D"), attended.Actor);
        Assert.Equal(new[] { EventType.JobApproved, EventType.SentToContractor, EventType.JobAttended }, job.Events.TakeLast(3).Select(e => e.Type));
    }

    [Fact]
    public void A_second_attended_mark_throws_and_changes_nothing()
    {
        var job = Approved(urgency: Urgency.Emergency);
        job.SendToContractor(Contractor1, Token1, Guid.NewGuid(), "rachel", T0.AddMinutes(1));
        job.MarkAttended(T0.AddHours(1));
        var before = job.Events.Count;

        Assert.Throws<Meridian.Domain.Jobs.JobStateException>(() => job.MarkAttended(T0.AddHours(2)));

        Assert.Equal(before, job.Events.Count);
        Assert.Equal(JobStatus.Attended, job.Status);
    }

    [Fact]
    public void An_emergency_25_hours_after_its_first_message_is_overdue_and_flagged_once()
    {
        var job = Approved(urgency: Urgency.Emergency);
        job.SendToContractor(Contractor1, Token1, Guid.NewGuid(), "rachel", T0.AddMinutes(1));
        var now = T0.AddHours(25);

        Assert.True(job.IsAttendanceOverdue(now));
        Assert.True(job.FlagAttendanceOverdue(now));
        Assert.False(job.FlagAttendanceOverdue(now.AddMinutes(1)));
        Assert.False(job.IsAttendanceOverdue(now.AddMinutes(1)));
        Assert.Single(job.Events, e => e.Type == EventType.AttendanceOverdue);
        Assert.Equal(JobEvent.SystemActor, job.Events.Single(e => e.Type == EventType.AttendanceOverdue).Actor);
    }

    [Fact]
    public void An_emergency_exactly_24_hours_old_is_not_yet_overdue()
    {
        var job = Approved(urgency: Urgency.Emergency);
        job.SendToContractor(Contractor1, Token1, Guid.NewGuid(), "rachel", T0.AddMinutes(1));

        Assert.False(job.IsAttendanceOverdue(T0.AddHours(24)));
        Assert.False(job.FlagAttendanceOverdue(T0.AddHours(24)));
        Assert.DoesNotContain(job.Events, e => e.Type == EventType.AttendanceOverdue);
    }

    [Fact]
    public void An_urgent_job_30_hours_old_is_not_flagged()
    {
        var job = Approved(urgency: Urgency.Urgent);
        job.SendToContractor(Contractor1, Token1, Guid.NewGuid(), "rachel", T0.AddMinutes(1));

        Assert.False(job.IsAttendanceOverdue(T0.AddHours(30)));
        Assert.False(job.FlagAttendanceOverdue(T0.AddHours(30)));
        Assert.DoesNotContain(job.Events, e => e.Type == EventType.AttendanceOverdue);
    }

    [Fact]
    public void After_a_resend_the_alert_waits_4_hours_from_the_resend_not_24_from_the_first_message()
    {
        var job = Approved(urgency: Urgency.Emergency);
        job.SendToContractor(Contractor1, Token1, Guid.NewGuid(), "rachel", T0.AddMinutes(1));
        job.FlagAttendanceOverdue(T0.AddHours(25));
        var resent = T0.AddHours(26);
        job.ResendToContractor(Contractor2, Token2, Guid.NewGuid(), "dawn", resent);

        Assert.False(job.IsAttendanceOverdue(resent.AddHours(1)));
        Assert.False(job.IsAttendanceOverdue(resent.AddHours(4)));
        Assert.True(job.FlagAttendanceOverdue(resent.AddHours(4).AddMinutes(1)));
        Assert.False(job.FlagAttendanceOverdue(resent.AddHours(5)));
        Assert.Equal(2, job.Events.Count(e => e.Type == EventType.AttendanceOverdue));
    }

    [Fact]
    public void Resend_replaces_the_token_clears_the_flag_and_records_the_send_again()
    {
        var job = Approved(urgency: Urgency.Emergency);
        job.SendToContractor(Contractor1, Token1, Guid.NewGuid(), "rachel", T0.AddMinutes(1));
        job.FlagAttendanceOverdue(T0.AddHours(25));

        job.ResendToContractor(Contractor2, Token2, Guid.NewGuid(), "dawn", T0.AddHours(26));

        Assert.Equal(JobStatus.SentToContractor, job.Status);
        Assert.False(job.HasContractorToken(Token1));
        Assert.True(job.HasContractorToken(Token2));
        Assert.Equal(Contractor2, job.SentToContractorId);
        Assert.Null(job.AttendanceOverdueFlaggedAtUtc);
        Assert.Equal(2, job.Events.Count(e => e.Type == EventType.SentToContractor));
        Assert.Equal("dawn", job.Events[^1].Actor);
    }

    [Fact]
    public void Resend_is_only_for_a_job_sent_to_a_contractor()
    {
        var job = Approved(urgency: Urgency.Emergency);

        Assert.Throws<Meridian.Domain.Jobs.JobStateException>(() =>
            job.ResendToContractor(Contractor2, Token2, Guid.NewGuid(), "dawn", T0));
        Assert.Equal(JobStatus.Approved, job.Status);
    }

    [Fact]
    public void Empty_tokens_and_ids_are_refused()
    {
        var job = Approved(urgency: Urgency.Emergency);

        Assert.Throws<ArgumentException>(() => job.SendToContractor(Contractor1, " ", Guid.NewGuid(), "rachel", T0));
        Assert.Throws<ArgumentException>(() => job.SendToContractor(Guid.Empty, Token1, Guid.NewGuid(), "rachel", T0));
        Assert.Throws<ArgumentException>(() => job.SendToContractor(Contractor1, Token1, Guid.Empty, "rachel", T0));
        Assert.Equal(JobStatus.Approved, job.Status);
    }

    private static Job Prepared()
    {
        var job = Job.CreateFromMessage(Message.Receive("m.walsh@example.com", "No heating", T0), "MER-000001", T0);
        job.EditCategory(Category.Heating, "rachel", T0);
        job.ChooseContractor(Contractor1, "rachel", T0);
        return job;
    }

    private static Job Approved(Urgency urgency)
    {
        var job = Prepared();
        if (urgency != Urgency.Routine)
        {
            job.RaiseUrgency(urgency, "test", T0);
        }

        job.Approve("rachel", T0);
        return job;
    }
}
