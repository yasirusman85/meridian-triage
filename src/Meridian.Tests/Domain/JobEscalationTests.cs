using System.Text.Json;
using Meridian.Application.Escalation;
using Meridian.Application.Templates;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;

namespace Meridian.Tests.Domain;

/// <summary>The escalation field's transitions (AD-6): <c>None → NotifiedRachel → NotifiedBackup → Failed</c>, plus Acknowledged.</summary>
public class JobEscalationTests
{
    private static readonly DateTime T0 = new(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc);

    [Fact]
    public void Notifying_twice_moves_from_primary_to_backup_and_failing_ends_it()
    {
        var job = Emergency();

        job.NotifyEscalation("rachel", Guid.NewGuid(), T0);
        Assert.Equal(EscalationState.NotifiedRachel, job.EscalationState);
        job.NotifyEscalation("dawn", Guid.NewGuid(), T0.AddMinutes(30));
        Assert.Equal(EscalationState.NotifiedBackup, job.EscalationState);
        Assert.Throws<InvalidOperationException>(() => job.NotifyEscalation("dawn", Guid.NewGuid(), T0.AddMinutes(31)));
        job.FailEscalation(T0.AddMinutes(60));

        Assert.Equal(EscalationState.Failed, job.EscalationState);
        Assert.False(job.IsEscalating);
        Assert.Throws<InvalidOperationException>(() => job.FailEscalation(T0.AddMinutes(61)));
        Assert.Throws<InvalidOperationException>(() => job.NotifyEscalation("rachel", Guid.NewGuid(), T0.AddMinutes(61)));
        // The status is untouched: escalation is a separate field.
        Assert.Equal(JobStatus.Prepared, job.Status);
    }

    [Fact]
    public void Failing_before_the_backup_was_notified_throws()
    {
        var job = Emergency();
        Assert.Throws<InvalidOperationException>(() => job.FailEscalation(T0));
        job.NotifyEscalation("rachel", Guid.NewGuid(), T0);
        Assert.Throws<InvalidOperationException>(() => job.FailEscalation(T0.AddMinutes(30)));
    }

    [Fact]
    public void A_non_emergency_cannot_be_notified_or_acknowledged()
    {
        var job = Job.CreateFromMessage(Message.Receive("t@example.com", "Tap", T0), "MER-000001", T0);
        job.RaiseUrgency(Urgency.Urgent, "model", T0);

        Assert.Throws<InvalidOperationException>(() => job.NotifyEscalation("rachel", Guid.NewGuid(), T0));
        Assert.Throws<InvalidOperationException>(() => job.AcknowledgeEscalation("rachel", T0));
        Assert.False(job.CanAcknowledgeEscalation);
        Assert.Equal(EscalationState.None, job.EscalationState);
        Assert.DoesNotContain(job.Events, e => e.Type.ToString().StartsWith("Escalation", StringComparison.Ordinal));
    }

    [Fact]
    public void Acknowledging_records_the_actor_and_cannot_be_repeated()
    {
        var job = Emergency();
        job.NotifyEscalation("rachel", Guid.NewGuid(), T0);

        job.AcknowledgeEscalation("rachel", T0.AddMinutes(5));

        Assert.Equal(EscalationState.Acknowledged, job.EscalationState);
        Assert.Equal("rachel", job.EscalationAcknowledgedBy);
        Assert.Equal(T0.AddMinutes(5), job.EscalationAcknowledgedAtUtc);
        var ack = job.Events[^1];
        Assert.Equal(EventType.EscalationAcknowledged, ack.Type);
        Assert.Equal("rachel", ack.Actor);
        using (var payload = JsonDocument.Parse(ack.PayloadJson))
        {
            Assert.Equal("NotifiedRachel", payload.RootElement.GetProperty("from").GetString());
        }

        Assert.Throws<InvalidOperationException>(() => job.AcknowledgeEscalation("dawn", T0.AddMinutes(6)));
        Assert.Throws<InvalidOperationException>(() => job.NotifyEscalation("dawn", Guid.NewGuid(), T0.AddMinutes(30)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("system")]
    public void The_system_is_not_a_staff_member(string actor)
    {
        var job = Emergency();
        Assert.Throws<ArgumentException>(() => job.AcknowledgeEscalation(actor, T0));
        Assert.Throws<ArgumentException>(() => job.NotifyEscalation(actor, Guid.NewGuid(), T0));
    }

    [Fact]
    public void Times_must_be_UTC_and_the_outbound_id_is_required()
    {
        var job = Emergency();
        Assert.Throws<ArgumentException>(() => job.NotifyEscalation("rachel", Guid.NewGuid(), DateTime.SpecifyKind(T0, DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(() => job.NotifyEscalation("rachel", Guid.Empty, T0));
        Assert.Equal(EscalationState.None, job.EscalationState);
    }

    [Fact]
    public void Staff_templates_fill_only_their_placeholders()
    {
        var body = StaffTemplates.Render(StaffTemplates.EscalationBackupId, "MER-000007", "8 Carlton Street", "Heating", new[] { "gas", "carbon-monoxide" });

        Assert.Equal(
            "EMERGENCY MER-000007 has not been acknowledged by the first contact: Heating at 8 Carlton Street. Rules: gas, carbon-monoxide. You are the backup: please open the Meridian queue and acknowledge it.",
            body);
        Assert.DoesNotContain("{", body);
        Assert.Throws<ArgumentOutOfRangeException>(() => StaffTemplates.Text("acknowledgement"));
    }

    [Fact]
    public void Options_have_a_window_of_at_least_one_minute()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EscalationOptions(0));
        Assert.Equal(TimeSpan.FromMinutes(30), EscalationOptions.Default.Window);
        Assert.Equal(30, EscalationOptions.Default.WindowMinutes);
    }

    private static Job Emergency()
    {
        var job = Job.CreateFromMessage(Message.Receive("t@example.com", "Water through the ceiling", T0), "MER-000001", T0);
        job.RaiseUrgency(Urgency.Emergency, "rule:water-through-ceiling", T0);
        return job;
    }
}
