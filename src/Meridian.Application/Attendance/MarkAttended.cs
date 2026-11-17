using Meridian.Application.Abstractions;
using Meridian.Domain.Jobs;

namespace Meridian.Application.Attendance;

/// <summary>
/// The contractor marks the job attended through its link (R20, R21, AD-6): SentToContractor → Attended, the token is
/// invalidated, and the event is written in one unit of work. A wrong, used or expired token changes nothing.
/// </summary>
public sealed class MarkAttended
{
    private readonly IJobRepository _jobs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public MarkAttended(IJobRepository jobs, IUnitOfWork unitOfWork, IClock clock)
    {
        _jobs = jobs;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <returns>The job reference when it was marked attended; null when the token matched no sent job.</returns>
    public async Task<string?> ExecuteAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return await _unitOfWork.ExecuteAsync<string?>(async ct =>
        {
            var job = await _jobs.GetByContractorTokenHashAsync(Job.HashContractorToken(token), ct);
            if (job is null || !job.HasContractorToken(token))
            {
                return null;
            }

            job.MarkAttended(_clock.UtcNow);
            return job.Reference;
        }, cancellationToken);
    }
}
