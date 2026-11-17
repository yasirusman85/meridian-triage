using System.Text.Json;
using Meridian.Application.Abstractions;
using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;

namespace Meridian.Application.Review;

public sealed record JobDetailMessage(Guid Id, string Sender, string Text, DateTime ReceivedAtUtc);

/// <summary>An emergency rule that fired on one of the job's messages, with the phrase it matched (R4).</summary>
public sealed record JobDetailRule(string Id, string MatchedPhrase);

public sealed record JobDetailContractor(Guid Id, string Name, string Phone, Category Trade, ContractorRule? Rule);

/// <summary>
/// One history row in sequence order, with a one-line description for the job view (R23), the raw payload JSON as
/// stored, and for a model event the fields that explain the AI decision (R17).
/// </summary>
public sealed record JobDetailEvent(
    EventType Type,
    DateTime OccurredAtUtc,
    string Actor,
    string Summary,
    int Sequence,
    string PayloadJson,
    JobDetailModelRun? Model);

/// <summary>
/// What the model did for a <c>ModelPrepared</c> or <c>ModelPreparationFailed</c> event: model id, prompt version,
/// total latency, tokens summed over attempts (null when none were reported), the last raw output (null when the
/// model gave none), the failure reason (null on success) and the number of attempts.
/// </summary>
public sealed record JobDetailModelRun(
    string? ModelId,
    string? PromptVersion,
    long? LatencyMs,
    int? InputTokens,
    int? OutputTokens,
    string? RawOutput,
    string? Reason,
    int Attempts);

/// <summary>Everything the job detail panel shows (R13): the AI's explanation, messages, rules, contractor and history.</summary>
public sealed record JobDetail(
    Guid Id,
    string Reference,
    JobStatus Status,
    Urgency Urgency,
    Category Category,
    DateTime ReceivedAtUtc,
    string? Explanation,
    string? SummaryEn,
    string? Language,
    Guid? PropertyId,
    string? PropertyName,
    string? BuildingName,
    JobDetailContractor? Contractor,
    IReadOnlyList<JobDetailRule> Rules,
    IReadOnlyList<JobDetailMessage> Messages,
    IReadOnlyList<JobDetailEvent> Events,
    bool CanReview,
    IReadOnlyList<string> MissingForApproval);

/// <summary>The job detail query: one job with its messages, rules, contractor and event summary; null when it does not exist.</summary>
public sealed class GetJob
{
    private readonly IJobRepository _jobs;
    private readonly IPropertyRepository _properties;
    private readonly IContractorRepository _contractors;

    public GetJob(IJobRepository jobs, IPropertyRepository properties, IContractorRepository contractors)
    {
        _jobs = jobs;
        _properties = properties;
        _contractors = contractors;
    }

    public async Task<JobDetail?> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await _jobs.GetAsync(id, cancellationToken);
        if (job is null)
        {
            return null;
        }

        var properties = (await _properties.ListAsync(cancellationToken)).ToDictionary(p => p.Id);
        var contractors = (await _contractors.ListAsync(cancellationToken)).ToDictionary(c => c.Id);
        var property = job.PropertyId is { } propertyId ? properties.GetValueOrDefault(propertyId) : null;
        var contractorId = job.SentToContractorId ?? job.SuggestedContractorId;
        var contractor = contractorId is { } contractorKey ? contractors.GetValueOrDefault(contractorKey) : null;

        string PropertyName(Guid? value) =>
            value is not { } key ? "none" : properties.TryGetValue(key, out var p) ? p.Name : key.ToString();

        string ContractorName(Guid? value) =>
            value is not { } key ? "none" : contractors.TryGetValue(key, out var c) ? c.Name : key.ToString();

        var events = job.Events
            .OrderBy(e => e.Sequence)
            .Select(e => new JobDetailEvent(
                e.Type,
                e.OccurredAtUtc,
                e.Actor,
                Summarise(e, PropertyName, ContractorName),
                e.Sequence,
                e.PayloadJson,
                ModelRunOf(e)))
            .ToList();

        return new JobDetail(
            job.Id,
            job.Reference,
            job.Status,
            job.Urgency,
            job.Category,
            job.ReceivedAtUtc,
            job.Explanation,
            job.SummaryEn,
            job.Language,
            job.PropertyId,
            property?.Name,
            property?.Building?.Name,
            contractor is null
                ? null
                : new JobDetailContractor(
                    contractor.Id,
                    contractor.Name,
                    contractor.Phone,
                    contractor.Trade,
                    job.SentContractorRule),
            RulesOf(job),
            job.Messages
                .OrderBy(m => m.ReceivedAtUtc)
                .Select(m => new JobDetailMessage(m.Id, m.Sender, m.Text, m.ReceivedAtUtc))
                .ToList(),
            events,
            job.Status == JobStatus.Prepared,
            job.MissingForApproval());
    }

    private static IReadOnlyList<JobDetailRule> RulesOf(Job job)
    {
        var rules = new List<JobDetailRule>();
        foreach (var applied in job.Events.Where(e => e.Type == EventType.RulesApplied).OrderBy(e => e.Sequence))
        {
            using var payload = JsonDocument.Parse(applied.PayloadJson);
            if (!payload.RootElement.TryGetProperty("rules", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var rule in list.EnumerateArray())
            {
                var id = Text(rule, "id");
                if (id is not null && rules.All(r => r.Id != id))
                {
                    rules.Add(new JobDetailRule(id, Text(rule, "matchedPhrase") ?? string.Empty));
                }
            }
        }

        return rules;
    }

    private static string Summarise(JobEvent e, Func<Guid?, string> propertyName, Func<Guid?, string> contractorName)
    {
        using var payload = JsonDocument.Parse(e.PayloadJson);
        var root = payload.RootElement;
        return e.Type switch
        {
            EventType.MessageReceived => "Message received",
            EventType.JobPrepared => $"Job prepared as {Text(root, "category")}, {Text(root, "urgency")}",
            EventType.ModelPrepared => "AI prepared the job" + (Text(root, "modelId") is { } model ? $" ({model})" : string.Empty),
            EventType.ModelPreparationFailed => "AI gave no usable answer" + (Text(root, "reason") is { } reason ? $" ({reason})" : string.Empty),
            EventType.UrgencyRaised => $"Urgency raised {Text(root, "from")} → {Text(root, "to")} ({Text(root, "reason")})",
            EventType.UrgencySetByPerson => $"Urgency set {Text(root, "from")} → {Text(root, "to")}: {Text(root, "reason")}",
            EventType.RulesApplied => RulesSummary(root),
            EventType.FollowUpAttached => "Follow-up message attached",
            EventType.AcknowledgementSent => $"Tenant sent {Text(root, "templateId")}",
            EventType.ContractorSuggested =>
                $"Contractor suggested: {contractorName(GuidOf(root, "contractorId"))} ({Text(root, "rule")})",
            EventType.JobApproved => "Approved",
            EventType.JobRejected => $"Rejected: {Text(root, "reason")}",
            EventType.JobEdited => EditedSummary(root, propertyName, contractorName),
            EventType.SentToContractor => $"Sent to contractor: {contractorName(GuidOf(root, "contractorId"))}",
            EventType.JobAttended => "Marked attended by the contractor",
            EventType.AttendanceOverdue => "Attendance overdue: not attended in time (24 hours from the first message, 4 hours after a resend)",
            _ => e.Type.ToString(),
        };
    }

    private static JobDetailModelRun? ModelRunOf(JobEvent e)
    {
        if (e.Type is not (EventType.ModelPrepared or EventType.ModelPreparationFailed))
        {
            return null;
        }

        using var payload = JsonDocument.Parse(e.PayloadJson);
        var root = payload.RootElement;
        var attempts = root.TryGetProperty("attempts", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object).ToList()
            : new List<JsonElement>();

        int? Sum(string name)
        {
            var values = attempts.Select(a => Int(a, name)).OfType<int>().ToList();
            return values.Count == 0 ? null : values.Sum();
        }

        var count = Int(root, "attemptCount") ?? attempts.Count;
        return new JobDetailModelRun(
            Text(root, "modelId") ?? attempts.Select(a => Text(a, "modelId")).LastOrDefault(m => m is not null),
            Text(root, "promptVersion") ?? attempts.Select(a => Text(a, "promptVersion")).LastOrDefault(p => p is not null),
            Long(root, "latencyMs"),
            Sum("inputTokens"),
            Sum("outputTokens"),
            attempts.Select(a => Text(a, "rawOutput")).LastOrDefault(r => r is not null),
            Text(root, "reason"),
            count);
    }

    private static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;

    private static long? Long(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : null;

    private static string RulesSummary(JsonElement root)
    {
        var ids = root.TryGetProperty("rules", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(r => Text(r, "id")).OfType<string>().ToList()
            : new List<string>();
        return ids.Count == 0 ? "Emergency rules: none fired" : $"Emergency rules fired: {string.Join(", ", ids)}";
    }

    private static string EditedSummary(JsonElement root, Func<Guid?, string> propertyName, Func<Guid?, string> contractorName)
    {
        var field = Text(root, "field");
        var (before, after) = field switch
        {
            Job.EditedFields.Contractor => (contractorName(GuidOf(root, "before")), contractorName(GuidOf(root, "after"))),
            Job.EditedFields.Property => (propertyName(GuidOf(root, "before")), propertyName(GuidOf(root, "after"))),
            _ => (Text(root, "before") ?? "none", Text(root, "after") ?? "none"),
        };
        var cause = Text(root, "cause") is { } c ? $" (after {c} change)" : string.Empty;
        return $"Edited {field}: {before} → {after}{cause}";
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Guid? GuidOf(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && Guid.TryParse(value.GetString(), out var id)
            ? id
            : null;
}
