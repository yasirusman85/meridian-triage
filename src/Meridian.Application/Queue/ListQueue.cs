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
    string? SummaryEn,
    IReadOnlyList<string> RuleIds,
    Guid? PropertyId,
    string? PropertyName,
    int FollowUpCount);

/// <summary>The review queue: most urgent first, then oldest first (R13).</summary>
public sealed class ListQueue
{
    public const int PreviewLength = 120;

    private readonly IJobRepository _jobs;
    private readonly IPropertyRepository _properties;

    public ListQueue(IJobRepository jobs, IPropertyRepository properties)
    {
        _jobs = jobs;
        _properties = properties;
    }

    public async Task<IReadOnlyList<QueueItem>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var jobs = await _jobs.ListAsync(cancellationToken);
        var names = (await _properties.ListAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
        return Order(jobs).Select(j => ToItem(j, names)).ToList();
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

    private static QueueItem ToItem(Job job, IReadOnlyDictionary<Guid, string> propertyNames)
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
            job.SummaryEn,
            job.RuleIds,
            job.PropertyId,
            job.PropertyId is { } id && propertyNames.TryGetValue(id, out var name) ? name : null,
            job.FollowUpCount);
    }
}
