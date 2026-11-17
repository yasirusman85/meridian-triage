using Meridian.Application.Abstractions;
using Meridian.Application.Staff;
using Meridian.Domain.Jobs;

namespace Meridian.Application.Escalation;

public enum AcknowledgeOutcome
{
    Acknowledged,
    JobNotFound,

    /// <summary>The acting person is not a configured staff member.</summary>
    UnknownStaff,

    /// <summary>The job is not an emergency, or its emergency is already acknowledged; nothing changed.</summary>
    NotAcknowledgeable,
}

public sealed record AcknowledgeResult(AcknowledgeOutcome Outcome, Job? Job, string? Detail = null);

/// <summary>
/// A staff member acknowledges an emergency from the queue (R18, R19): escalation stops and
/// <see cref="EventType.EscalationAcknowledged"/> records who. Reads and saves the job in one unit of work, so a tick
/// running at the same moment either sees the acknowledgement or finishes first.
/// </summary>
public sealed class AcknowledgeEscalation
{
    private readonly IJobRepository _jobs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly StaffDirectory _staff;

    public AcknowledgeEscalation(IJobRepository jobs, IUnitOfWork unitOfWork, IClock clock, StaffDirectory staff)
    {
        _jobs = jobs;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _staff = staff;
    }

    /// <param name="actingAs">The acting-as id, e.g. <c>rachel</c> or <c>dawn</c>.</param>
    public async Task<AcknowledgeResult> ExecuteAsync(Guid jobId, string? actingAs, CancellationToken cancellationToken)
    {
        var staff = _staff.Find(actingAs);
        if (staff is null)
        {
            return new AcknowledgeResult(AcknowledgeOutcome.UnknownStaff, null);
        }

        return await _unitOfWork.ExecuteAsync(
            async ct =>
            {
                var job = await _jobs.GetAsync(jobId, ct);
                if (job is null)
                {
                    return new AcknowledgeResult(AcknowledgeOutcome.JobNotFound, null);
                }

                if (!job.CanAcknowledgeEscalation)
                {
                    var detail = job.Urgency != Urgency.Emergency
                        ? $"Job {job.Reference} is {job.Urgency}, not an emergency."
                        : $"Job {job.Reference} is already acknowledged by {job.EscalationAcknowledgedBy}.";
                    return new AcknowledgeResult(AcknowledgeOutcome.NotAcknowledgeable, job, detail);
                }

                job.AcknowledgeEscalation(staff.Id, _clock.UtcNow);
                return new AcknowledgeResult(AcknowledgeOutcome.Acknowledged, job);
            },
            cancellationToken);
    }
}
