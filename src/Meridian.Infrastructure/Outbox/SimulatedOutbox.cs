using Meridian.Application.Outbox;
using Meridian.Domain.Outbox;
using Meridian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Outbox;

/// <summary>
/// The first version's outbox adapter (AD-8): stores each message as an <see cref="OutboundMessage"/> row with status
/// <see cref="OutboundStatus.Simulated"/> so it can be shown on the job. No email or SMS is sent.
/// </summary>
internal sealed class SimulatedOutbox : IOutbox
{
    private readonly MeridianDbContext _db;

    public SimulatedOutbox(MeridianDbContext db)
    {
        _db = db;
    }

    public OutboundMessage Send(OutboundRequest request, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outbound = OutboundMessage.Create(
            request.JobId,
            request.Channel,
            request.Recipient,
            request.TemplateId,
            request.Body,
            OutboundStatus.Simulated,
            nowUtc,
            request.MessageId);

        // Tracked only: written when the surrounding unit of work commits, with the job changes.
        _db.OutboundMessages.Add(outbound);
        return outbound;
    }

    public async Task<IReadOnlyList<OutboundMessage>> ListForJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var messageIds = await _db.Jobs
            .Where(j => j.Id == jobId)
            .SelectMany(j => j.Messages.Select(m => (Guid?)m.Id))
            .ToListAsync(cancellationToken);
        return await _db.OutboundMessages
            .AsNoTracking()
            .Where(o => o.JobId == jobId || messageIds.Contains(o.MessageId))
            .OrderBy(o => o.CreatedAtUtc)
            .ThenBy(o => o.Id)
            .ToListAsync(cancellationToken);
    }
}
