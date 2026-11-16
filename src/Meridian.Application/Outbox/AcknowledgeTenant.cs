using Meridian.Application.Templates;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;
using Meridian.Domain.Outbox;
using Meridian.Domain.Rules;

namespace Meridian.Application.Outbox;

/// <summary>
/// The acknowledgement step of the pipeline (AD-3, R16): once a message's jobs are created or matched, its sender gets
/// one fixed message quoting the reference of every job the message touched (several joined with ", "; a follow-up
/// quotes the existing job's reference). When the gas or carbon-monoxide rule fired on the message, that message is
/// the gas safety message instead of the ordinary acknowledgement (RULE-6). Plain code, no model call; the body is a
/// template with only the reference filled in (AD-7).
/// </summary>
public sealed class AcknowledgeTenant
{
    public const string ReferenceSeparator = ", ";

    private readonly IOutbox _outbox;

    public AcknowledgeTenant(IOutbox outbox)
    {
        _outbox = outbox;
    }

    /// <summary>
    /// Writes one acknowledgement for <paramref name="message"/> to the outbox and records
    /// <see cref="EventType.AcknowledgementSent"/> (pointing to that one outbound row) on each of <paramref name="jobs"/>.
    /// Call once per incoming message, inside the unit of work that creates or matches its jobs, after the rules ran.
    /// </summary>
    /// <param name="hits">The emergency rules that fired on this message.</param>
    /// <param name="jobs">Every job the message made or was attached to, in the order of the model's issues.</param>
    public OutboundMessage Send(Message message, IReadOnlyList<RuleHit> hits, IReadOnlyList<Job> jobs, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(hits);
        ArgumentNullException.ThrowIfNull(jobs);
        var touched = jobs.DistinctBy(j => j.Id).ToList();
        if (touched.Count == 0)
        {
            throw new ArgumentException("An acknowledgement needs at least one job.", nameof(jobs));
        }

        var templateId = TenantTemplates.AcknowledgementFor(hits.Select(h => h.RuleId));
        var references = string.Join(ReferenceSeparator, touched.Select(j => j.Reference));
        var body = TenantTemplates.Render(templateId, references);
        var outbound = _outbox.Send(
            new OutboundRequest(touched[0].Id, OutboundChannels.Email, message.Sender, templateId, body, message.Id),
            nowUtc);
        foreach (var job in touched)
        {
            job.RecordAcknowledgement(outbound.Id, templateId, nowUtc);
        }

        return outbound;
    }
}
