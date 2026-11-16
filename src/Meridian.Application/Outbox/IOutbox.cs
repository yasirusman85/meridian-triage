using Meridian.Domain.Outbox;

namespace Meridian.Application.Outbox;

/// <summary>What to send: the job, channel, recipient and the already-rendered template body (AD-7).</summary>
public sealed record OutboundRequest(Guid JobId, string Channel, string Recipient, string TemplateId, string Body);

/// <summary>
/// The outbox port (AD-8). Every acknowledgement, notification and contractor message goes through it. The first
/// version's adapter only stores the row (status <see cref="OutboundStatus.Simulated"/>); nothing is really sent.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Writes the message to the outbox. Call inside a unit of work: the row is saved when it commits, in the same
    /// transaction as the job changes.
    /// </summary>
    OutboundMessage Send(OutboundRequest request, DateTime nowUtc);

    /// <summary>A job's outbound messages, oldest first.</summary>
    Task<IReadOnlyList<OutboundMessage>> ListForJobAsync(Guid jobId, CancellationToken cancellationToken);
}
