using Meridian.Domain.Jobs;

namespace Meridian.Application.Abstractions;

public interface IJobRepository
{
    /// <summary>Takes the next job number from the reference sequence. Call inside a unit of work.</summary>
    Task<long> NextJobNumberAsync(CancellationToken cancellationToken);

    /// <summary>Tracks a new job with its messages and events; written when the unit of work commits.</summary>
    void Add(Job job);

    /// <summary>One job with its messages and events, tracked for changes; null when it does not exist.</summary>
    Task<Job?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>All jobs with their messages, read-only.</summary>
    Task<IReadOnlyList<Job>> ListAsync(CancellationToken cancellationToken);
}
