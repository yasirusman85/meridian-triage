using System.Text.Json;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;
using Meridian.Domain.Outbox;

namespace Meridian.Tests.Domain;

public class JobAcknowledgementTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 21, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RecordAcknowledgement_appends_AcknowledgementSent_with_template_and_outbound_id()
    {
        var job = NewJob();
        var outboundId = Guid.NewGuid();

        job.RecordAcknowledgement(outboundId, "gas-safety", Now);

        var ack = job.Events[^1];
        Assert.Equal(EventType.AcknowledgementSent, ack.Type);
        Assert.Equal(JobEvent.SystemActor, ack.Actor);
        Assert.Equal(Now, ack.OccurredAtUtc);
        using var payload = JsonDocument.Parse(ack.PayloadJson);
        Assert.Equal("gas-safety", payload.RootElement.GetProperty("templateId").GetString());
        Assert.Equal(outboundId, payload.RootElement.GetProperty("outboundId").GetGuid());
    }

    [Fact]
    public void RecordAcknowledgement_rejects_missing_ids_and_non_utc_time()
    {
        var job = NewJob();

        Assert.Throws<ArgumentException>(() => job.RecordAcknowledgement(Guid.Empty, "acknowledgement", Now));
        Assert.Throws<ArgumentException>(() => job.RecordAcknowledgement(Guid.NewGuid(), " ", Now));
        Assert.Throws<ArgumentException>(() => job.RecordAcknowledgement(Guid.NewGuid(), "acknowledgement", DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    [Fact]
    public void OutboundMessage_requires_every_field()
    {
        var jobId = Guid.NewGuid();

        var created = OutboundMessage.Create(jobId, OutboundChannels.Email, "t@example.com", "acknowledgement", "Body", OutboundStatus.Simulated, Now);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Throws<ArgumentException>(() => OutboundMessage.Create(Guid.Empty, "email", "t@example.com", "acknowledgement", "Body", OutboundStatus.Simulated, Now));
        Assert.Throws<ArgumentException>(() => OutboundMessage.Create(jobId, "email", "", "acknowledgement", "Body", OutboundStatus.Simulated, Now));
        Assert.Throws<ArgumentException>(() => OutboundMessage.Create(jobId, "email", "t@example.com", "acknowledgement", " ", OutboundStatus.Simulated, Now));
    }

    private static Job NewJob() => Job.CreateFromMessage(Message.Receive("t@example.com", "Tap dripping", Now), "MER-000001", Now);
}
