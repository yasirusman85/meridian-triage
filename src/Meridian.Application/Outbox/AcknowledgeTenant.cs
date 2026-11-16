using Meridian.Application.Templates;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;
using Meridian.Domain.Outbox;

namespace Meridian.Application.Outbox;

/// <summary>
/// The acknowledgement step of the pipeline (AD-3, R16): after the emergency rules and before the model, the sender of
/// an incoming message gets one fixed message quoting the job reference. When the gas or carbon-monoxide rule has
/// fired on the job, that message is the gas safety message instead of the ordinary acknowledgement (RULE-6). Plain
/// code, no model call; the body is a template with only the reference filled in (AD-7).
/// </summary>
public sealed class AcknowledgeTenant
{
    private readonly IOutbox _outbox;

    public AcknowledgeTenant(IOutbox outbox)
    {
        _outbox = outbox;
    }

    /// <summary>
    /// Writes the acknowledgement for <paramref name="message"/> to the outbox and records
    /// <see cref="EventType.AcknowledgementSent"/> on <paramref name="job"/>. Call once per incoming message, inside
    /// the same unit of work as the message and the rules, after <see cref="Job.ApplyRules"/>.
    /// </summary>
    public OutboundMessage Send(Job job, Message message, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(message);

        var templateId = TenantTemplates.AcknowledgementFor(job.RuleIds);
        var body = TenantTemplates.Render(templateId, job.Reference);
        var outbound = _outbox.Send(
            new OutboundRequest(job.Id, OutboundChannels.Email, message.Sender, templateId, body),
            nowUtc);
        job.RecordAcknowledgement(outbound.Id, templateId, nowUtc);
        return outbound;
    }
}
