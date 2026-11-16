using Meridian.Application.Abstractions;
using Meridian.Domain.Outbox;

namespace Meridian.Application.Outbox;

/// <summary>A job's outbound messages for the job view, oldest first; null when the job does not exist.</summary>
public sealed class ListOutbound
{
    private readonly IJobRepository _jobs;
    private readonly IOutbox _outbox;

    public ListOutbound(IJobRepository jobs, IOutbox outbox)
    {
        _jobs = jobs;
        _outbox = outbox;
    }

    public async Task<IReadOnlyList<OutboundMessage>?> ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (await _jobs.GetAsync(jobId, cancellationToken) is null)
        {
            return null;
        }

        return await _outbox.ListForJobAsync(jobId, cancellationToken);
    }
}
