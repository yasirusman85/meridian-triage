using Meridian.Application.Abstractions;
using Meridian.Domain.Jobs;

namespace Meridian.Application.Queue;

public sealed record QueueItem(
    Guid Id,
    string Reference,
    DateTime ReceivedAtUtc,
    JobStatus Status,
    Urgency Urgency,
    Category Category,
    string Sender,
    string Preview,
    string? Explanation,
    string? SummaryEn);

/// <summary>The review queue: most urgent first, then oldest first (R13).</summary>
public sealed class ListQueue
{
    public const int PreviewLength = 120;

    private readonly IJobRepository _jobs;

    public ListQueue(IJobRepository jobs)
    {
        _jobs = jobs;
    }

    public async Task<IReadOnlyList<QueueItem>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var jobs = await _jobs.ListAsync(cancellationToken);
        return Order(jobs).Select(ToItem).ToList();
    }

    public static IEnumerable<Job> Order(IEnumerable<Job> jobs) =>
        jobs.OrderByDescending(j => j.Urgency)
            .ThenBy(j => j.ReceivedAtUtc)
            .ThenBy(j => j.Reference, StringComparer.Ordinal);

    public static string Preview(string text)
    {
        if (text.Length <= PreviewLength)
        {
            return text;
        }

        var length = PreviewLength;
        // Do not cut a surrogate pair in half.
        if (char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        return text[..length];
    }

    private static QueueItem ToItem(Job job)
    {
        var first = job.Messages.OrderBy(m => m.ReceivedAtUtc).FirstOrDefault();
        return new QueueItem(
            job.Id,
            job.Reference,
            job.ReceivedAtUtc,
            job.Status,
            job.Urgency,
            job.Category,
            first?.Sender ?? string.Empty,
            first is null ? string.Empty : Preview(first.Text),
            job.Explanation,
            job.SummaryEn);
    }
}
