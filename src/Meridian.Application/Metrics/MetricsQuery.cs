using Meridian.Application.Abstractions;
using Meridian.Domain.Jobs;

namespace Meridian.Application.Metrics;

/// <summary>A count for one category, urgency or status; <paramref name="Name"/> is the enum name, e.g. "Heating".</summary>
public sealed record MetricCount(string Name, int Count);

/// <summary>
/// A duration measure in minutes over <paramref name="Jobs"/> jobs. The medians and 90th percentiles are null when there
/// is no data yet (never 0).
/// </summary>
public sealed record DurationMeasure(int Jobs, double? MedianMinutes, double? Percentile90Minutes);

/// <summary>
/// A promise measure: <paramref name="Met"/> of <paramref name="Eligible"/> jobs. Eligible 0 means no data yet, and the
/// rate is not shown.
/// </summary>
public sealed record PromiseMeasure(int Eligible, int Met);

/// <summary>The numbers for one window of jobs (R24).</summary>
public sealed record MetricsWindow(
    int JobCount,
    IReadOnlyList<MetricCount> ByCategory,
    IReadOnlyList<MetricCount> ByUrgency,
    IReadOnlyList<MetricCount> OpenByStatus,
    DurationMeasure FirstHumanLook,
    DurationMeasure TimeToApproval,
    DurationMeasure TimeToAttendance,
    PromiseMeasure Emergency24Hours,
    PromiseMeasure NonEmergency5WorkingDays);

public sealed record MetricsReport(DateTime GeneratedAtUtc, MetricsWindow Last30Days, MetricsWindow AllTime);

/// <summary>
/// The numbers page (R24, AD-5): computed from job events only, except that categories and urgencies are grouped by the
/// job's current fields. A job's window is set by its first event. Measures with no data stay null or zero-eligible so the
/// page can show "no data yet".
/// </summary>
public sealed class MetricsQuery
{
    public const int LastDays = 30;

    /// <summary>The status-changing events and the status each one moves the job to (AD-6).</summary>
    private static readonly IReadOnlyDictionary<EventType, JobStatus> StatusByEvent = new Dictionary<EventType, JobStatus>
    {
        [EventType.JobPrepared] = JobStatus.Prepared,
        [EventType.JobApproved] = JobStatus.Approved,
        [EventType.JobRejected] = JobStatus.Rejected,
        [EventType.SentToContractor] = JobStatus.SentToContractor,
        [EventType.JobAttended] = JobStatus.Attended,
    };

    /// <summary>Events where a person first looks at a prepared job: approve, edit, reject, acknowledge an escalation.</summary>
    private static readonly HashSet<EventType> HumanLookTypes = new()
    {
        EventType.JobApproved,
        EventType.JobRejected,
        EventType.JobEdited,
        EventType.UrgencySetByPerson,
        EventType.EscalationAcknowledged,
    };

    private readonly IJobRepository _jobs;
    private readonly IClock _clock;

    public MetricsQuery(IJobRepository jobs, IClock clock)
    {
        _jobs = jobs;
        _clock = clock;
    }

    public async Task<MetricsReport> ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var timelines = (await _jobs.ListWithEventsAsync(cancellationToken))
            .Select(j => Timeline.From(j))
            .OfType<Timeline>()
            .ToList();

        var cutoff = now.AddDays(-LastDays);
        var recent = timelines.Where(t => t.FirstEventAtUtc >= cutoff).ToList();
        return new MetricsReport(now, Summarize(recent, now), Summarize(timelines, now));
    }

    /// <summary>
    /// Adds <paramref name="days"/> Monday-to-Friday working days to a UTC instant, keeping its time of day. Bank holidays are
    /// not counted (see the architecture's Deferred list).
    /// </summary>
    public static DateTime AddWorkingDays(DateTime utc, int days)
    {
        if (utc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Time must be UTC.", nameof(utc));
        }

        var date = utc;
        var remaining = days;
        while (remaining > 0)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                remaining--;
            }
        }

        return date;
    }

    /// <summary>Nearest-rank 90th percentile and the median (mean of the two middle values for an even count).</summary>
    public static DurationMeasure Measure(IReadOnlyList<double> minutes)
    {
        if (minutes.Count == 0)
        {
            return new DurationMeasure(0, null, null);
        }

        var sorted = minutes.OrderBy(m => m).ToList();
        var n = sorted.Count;
        var median = n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        var rank = (9 * n + 9) / 10; // ceil(0.9 * n) in integers, 1-based
        return new DurationMeasure(n, median, sorted[rank - 1]);
    }

    private static MetricsWindow Summarize(IReadOnlyList<Timeline> jobs, DateTime nowUtc)
    {
        var byCategory = Enum.GetValues<Category>()
            .Select(c => new MetricCount(c.ToString(), jobs.Count(j => j.Category == c)))
            .ToList();
        var byUrgency = Enum.GetValues<Urgency>()
            .Select(u => new MetricCount(u.ToString(), jobs.Count(j => j.Urgency == u)))
            .ToList();
        var openByStatus = new[] { JobStatus.Prepared, JobStatus.Approved, JobStatus.SentToContractor }
            .Select(s => new MetricCount(s.ToString(), jobs.Count(j => j.Status == s)))
            .ToList();

        var firstLook = Measure(Minutes(jobs, j => j.PreparedAtUtc, j => j.FirstHumanLookAtUtc));
        var approval = Measure(Minutes(jobs, j => j.ReceivedAtUtc, j => j.ApprovedAtUtc));
        var toAttendance = Measure(Minutes(jobs, j => j.ReceivedAtUtc, j => j.AttendedAtUtc));

        return new MetricsWindow(
            jobs.Count,
            byCategory,
            byUrgency,
            openByStatus,
            firstLook,
            approval,
            toAttendance,
            Emergency24Hours(jobs, nowUtc),
            NonEmergencyFiveWorkingDays(jobs, nowUtc));
    }

    /// <summary>Emergency jobs attended within 24 hours of the first message, out of those attended or already past 24 hours.</summary>
    private static PromiseMeasure Emergency24Hours(IReadOnlyList<Timeline> jobs, DateTime nowUtc)
    {
        var window = TimeSpan.FromHours(24);
        var decided = jobs
            .Where(j => j.Urgency == Urgency.Emergency && j.ReceivedAtUtc is { } received
                && (j.AttendedAtUtc is not null || nowUtc - received > window))
            .ToList();
        var met = decided.Count(j => j.AttendedAtUtc is { } attended && attended - j.ReceivedAtUtc!.Value <= window);
        return new PromiseMeasure(decided.Count, met);
    }

    /// <summary>Non-emergency jobs attended within five working days of the first message, out of those attended or already past it.</summary>
    private static PromiseMeasure NonEmergencyFiveWorkingDays(IReadOnlyList<Timeline> jobs, DateTime nowUtc)
    {
        var decided = new List<(Timeline Job, DateTime Deadline)>();
        foreach (var job in jobs.Where(j => j.Urgency != Urgency.Emergency && j.ReceivedAtUtc is not null))
        {
            var deadline = AddWorkingDays(job.ReceivedAtUtc!.Value, 5);
            if (job.AttendedAtUtc is not null || nowUtc > deadline)
            {
                decided.Add((job, deadline));
            }
        }

        var met = decided.Count(d => d.Job.AttendedAtUtc is { } attended && attended <= d.Deadline);
        return new PromiseMeasure(decided.Count, met);
    }

    private static IReadOnlyList<double> Minutes(
        IReadOnlyList<Timeline> jobs,
        Func<Timeline, DateTime?> from,
        Func<Timeline, DateTime?> to) =>
        jobs.Select(j => (from(j), to(j)))
            .Where(p => p.Item1 is not null && p.Item2 is not null)
            .Select(p => (p.Item2!.Value - p.Item1!.Value).TotalMinutes)
            .Where(m => m >= 0)
            .ToList();

    /// <summary>The moments of one job's history the measures need, read from its events in sequence order.</summary>
    private sealed record Timeline(
        Category Category,
        Urgency Urgency,
        JobStatus? Status,
        DateTime FirstEventAtUtc,
        DateTime? ReceivedAtUtc,
        DateTime? PreparedAtUtc,
        DateTime? FirstHumanLookAtUtc,
        DateTime? ApprovedAtUtc,
        DateTime? AttendedAtUtc)
    {
        public static Timeline? From(Job job)
        {
            var events = job.Events;
            if (events.Count == 0)
            {
                return null;
            }

            JobStatus? status = null;
            DateTime? received = null;
            DateTime? prepared = null;
            DateTime? firstLook = null;
            DateTime? approved = null;
            DateTime? attended = null;
            var preparedSeen = false;

            foreach (var e in events)
            {
                if (StatusByEvent.TryGetValue(e.Type, out var next))
                {
                    status = next;
                }

                if (e.Type == EventType.MessageReceived)
                {
                    received ??= e.OccurredAtUtc;
                }

                if (e.Type == EventType.JobPrepared && !preparedSeen)
                {
                    prepared = e.OccurredAtUtc;
                    preparedSeen = true;
                    continue;
                }

                if (preparedSeen && firstLook is null && HumanLookTypes.Contains(e.Type) && e.Actor != JobEvent.SystemActor)
                {
                    firstLook = e.OccurredAtUtc;
                }

                if (e.Type == EventType.JobApproved)
                {
                    approved ??= e.OccurredAtUtc;
                }

                if (e.Type == EventType.JobAttended)
                {
                    attended ??= e.OccurredAtUtc;
                }
            }

            return new Timeline(
                job.Category,
                job.Urgency,
                status,
                events[0].OccurredAtUtc,
                received,
                prepared,
                firstLook,
                approved,
                attended);
        }
    }
}
