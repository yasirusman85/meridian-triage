using Meridian.Application.Abstractions;

namespace Meridian.Application.Attendance;

/// <summary>
/// The overdue check, run by the escalation worker each tick (RULE-8, AD-9): every Emergency job sent to a contractor
/// that is more than 24 hours past its first message and not yet flagged gets <c>AttendanceOverdue</c> once and the
/// queue alert. Idempotent: a second run flags nothing.
/// </summary>
public sealed class CheckOverdueAttendance
{
    private readonly IJobRepository _jobs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CheckOverdueAttendance(IJobRepository jobs, IUnitOfWork unitOfWork, IClock clock)
    {
        _jobs = jobs;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <returns>The references of the jobs flagged by this run, in queue order; empty when none were due.</returns>
    public Task<IReadOnlyList<string>> ExecuteAsync(CancellationToken cancellationToken) =>
        _unitOfWork.ExecuteAsync<IReadOnlyList<string>>(async ct =>
        {
            var now = _clock.UtcNow;
            var flagged = new List<string>();
            foreach (var job in await _jobs.ListAwaitingAttendanceAsync(ct))
            {
                if (job.FlagAttendanceOverdue(now))
                {
                    flagged.Add(job.Reference);
                }
            }

            return flagged;
        }, cancellationToken);
}
