using Meridian.Domain.Jobs;

namespace Meridian.Application.Triage;

/// <summary>
/// Decides whether an issue is a follow-up of an existing job (AD-13, R8). Plain code, no model call. An issue
/// attaches to a job when all of these hold:
/// <list type="bullet">
/// <item>the issue's property is resolved and is the job's property (an unresolved property never matches);</item>
/// <item>the categories are the same, or either is <see cref="Category.Unsure"/>;</item>
/// <item>the job is open (not <see cref="JobStatus.Attended"/> or <see cref="JobStatus.Rejected"/>);</item>
/// <item>the job was opened at most <see cref="Window"/> before <c>nowUtc</c>.</item>
/// </list>
/// When several jobs qualify, a same-category job wins over an Unsure match, then the most recently opened job,
/// then the lowest reference.
/// </summary>
public static class FollowUpMatcher
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    public static Job? Match(IEnumerable<Job> existingJobs, Guid? propertyId, Category category, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(existingJobs);
        if (propertyId is null)
        {
            return null;
        }

        var openedSince = nowUtc - Window;
        return existingJobs
            .Where(j => j.PropertyId == propertyId)
            .Where(j => j.IsOpen)
            .Where(j => j.CreatedAtUtc >= openedSince && j.CreatedAtUtc <= nowUtc)
            .Where(j => j.Category == category || j.Category == Category.Unsure || category == Category.Unsure)
            .OrderByDescending(j => j.Category == category)
            .ThenByDescending(j => j.CreatedAtUtc)
            .ThenBy(j => j.Reference, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
