using Meridian.Domain.Jobs;

namespace Meridian.Web.Api;

public sealed record SubmitMessageRequest(string? Sender, string? Text);

public sealed record SubmitMessageResponse(Guid JobId, string Reference);

public sealed record JobListItemDto(
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
    IReadOnlyList<string> RuleIds);
