using Meridian.Application.Outbox;
using Meridian.Domain.Outbox;

namespace Meridian.Tests;

/// <summary>An in-memory <see cref="IOutbox"/> that keeps every message it was given.</summary>
internal sealed class FakeOutbox : IOutbox
{
    public List<OutboundMessage> Sent { get; } = new();

    public OutboundMessage Send(OutboundRequest request, DateTime nowUtc)
    {
        var outbound = OutboundMessage.Create(
            request.JobId, request.Channel, request.Recipient, request.TemplateId, request.Body, OutboundStatus.Simulated, nowUtc);
        Sent.Add(outbound);
        return outbound;
    }

    public Task<IReadOnlyList<OutboundMessage>> ListForJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OutboundMessage>>(Sent.Where(o => o.JobId == jobId).ToList());
}
