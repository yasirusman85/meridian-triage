using Meridian.Application.Abstractions;
using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;
using Meridian.Domain.Properties;

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
    int FollowUpCount,
    string? BuildingName,
    Guid? SuggestedContractorId,
    string? SuggestedContractorName,
    string? SuggestedContractorPhone,
    ContractorRule? SuggestedContractorRule);

/// <summary>
/// The review queue: most urgent first, then oldest first (R13). Each item carries its building and suggested
/// contractor (R11); no contractor means a person must choose.
/// </summary>
public sealed class ListQueue
{
    public const int PreviewLength = 120;

    private readonly IJobRepository _jobs;
    private readonly IPropertyRepository _properties;
    private readonly IContractorRepository _contractors;

    public ListQueue(IJobRepository jobs, IPropertyRepository properties, IContractorRepository contractors)
    {
        _jobs = jobs;
        _properties = properties;
        _contractors = contractors;
    }

    public async Task<IReadOnlyList<QueueItem>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var jobs = await _jobs.ListAsync(cancellationToken);
        var properties = (await _properties.ListAsync(cancellationToken)).ToDictionary(p => p.Id);
        var contractors = (await _contractors.ListAsync(cancellationToken)).ToDictionary(c => c.Id);
        return Order(jobs).Select(j => ToItem(j, properties, contractors)).ToList();
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

    private static QueueItem ToItem(
        Job job,
        IReadOnlyDictionary<Guid, Property> properties,
        IReadOnlyDictionary<Guid, Contractor> contractors)
    {
        var first = job.Messages.OrderBy(m => m.ReceivedAtUtc).FirstOrDefault();
        var property = job.PropertyId is { } propertyId ? properties.GetValueOrDefault(propertyId) : null;
        var contractor = job.SuggestedContractorId is { } contractorId ? contractors.GetValueOrDefault(contractorId) : null;
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
            property?.Name,
            job.FollowUpCount,
            property?.Building?.Name,
            contractor?.Id,
            contractor?.Name,
            contractor?.Phone,
            contractor is null ? null : job.SuggestedContractorRule);
    }
}
