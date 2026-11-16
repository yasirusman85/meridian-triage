using Meridian.Domain.Jobs;

namespace Meridian.Web.Api;

public sealed record SubmitMessageRequest(string? Sender, string? Text);

/// <summary>Every job the message made or was attached to, in the order of the model's issues.</summary>
public sealed record SubmitMessageResponse(Guid MessageId, IReadOnlyList<SubmittedJobDto> Jobs);

/// <param name="Outcome"><c>created</c> for a new job, <c>followUp</c> when attached to an existing open job.</param>
public sealed record SubmittedJobDto(Guid JobId, string Reference, string Outcome);

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
    IReadOnlyList<string> RuleIds,
    Guid? PropertyId,
    string? PropertyName,
    int FollowUpCount);
