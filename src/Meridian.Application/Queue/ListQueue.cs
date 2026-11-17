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
    ContractorRule? SuggestedContractorRule,
    EscalationState EscalationState = EscalationState.None,
    string? EscalationAcknowledgedBy = null,
    DateTime? EscalationAcknowledgedAtUtc = null,
    bool AttendanceOverdue = false);

/// <summary>
/// The review queue: jobs sent to a contractor past the 24-hour emergency promise first (R21), then jobs whose
/// escalation failed (R19), then most urgent, then oldest first (R13). Each item carries its building and contractor
/// (R11), where no contractor means a person must choose, and its escalation state. A job sent to a contractor shows
/// that contractor, not the one first suggested.
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
        jobs.OrderByDescending(j => j.AttendanceOverdueFlaggedAtUtc is not null)
            .ThenByDescending(j => j.EscalationState == EscalationState.Failed)
            .ThenByDescending(j => j.Urgency)
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
        var contractorId = job.SentToContractorId ?? job.SuggestedContractorId;
        var contractor = contractorId is { } id ? contractors.GetValueOrDefault(id) : null;
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
            contractor is null ? null : job.SentContractorRule,
            job.EscalationState,
            job.EscalationAcknowledgedBy,
            job.EscalationAcknowledgedAtUtc,
            job.AttendanceOverdueFlaggedAtUtc is not null);
    }
}
