using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;

namespace Meridian.Application.Abstractions;

public interface IJobRepository
{
    /// <summary>Takes the next job number from the reference sequence. Call inside a unit of work.</summary>
    Task<long> NextJobNumberAsync(CancellationToken cancellationToken);

    /// <summary>Tracks a newly received message (stored once, before any job exists); written when the unit of work commits.</summary>
    void AddMessage(Message message);

    /// <summary>Tracks a new job with its new events; written when the unit of work commits.</summary>
    void Add(Job job);

    /// <summary>One job with its messages and events, tracked for changes; null when it does not exist.</summary>
    Task<Job?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Open jobs (not Attended or Rejected) at one of <paramref name="propertyIds"/> opened at or after
    /// <paramref name="openedSinceUtc"/>, with messages and events, tracked for changes: the follow-up candidates.
    /// </summary>
    Task<IReadOnlyList<Job>> ListOpenAtPropertiesAsync(
        IReadOnlyCollection<Guid> propertyIds,
        DateTime openedSinceUtc,
        CancellationToken cancellationToken);

    /// <summary>All jobs with their messages, read-only.</summary>
    Task<IReadOnlyList<Job>> ListAsync(CancellationToken cancellationToken);
}
