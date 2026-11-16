namespace Meridian.Domain.Jobs;

/// <summary>
/// A review action the job's state does not allow (AD-6): an illegal transition such as approving an Approved job,
/// editing a job that is no longer Prepared, or approving a job that still lacks a category or contractor
/// (<see cref="Missing"/>). Nothing on the job changed.
/// </summary>
public sealed class JobStateException : InvalidOperationException
{
    public JobStateException(string message, JobStatus status, IReadOnlyList<string>? missing = null)
        : base(message)
    {
        Status = status;
        Missing = missing ?? Array.Empty<string>();
    }

    /// <summary>The job's status when the action was refused.</summary>
    public JobStatus Status { get; }

    /// <summary>What the job still needs before it can be approved, e.g. <c>category</c>, <c>contractor</c>; empty for an illegal transition.</summary>
    public IReadOnlyList<string> Missing { get; }
}
