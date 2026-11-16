using Meridian.Domain.Jobs;
using Meridian.Domain.Outbox;

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

/// <summary>A message written to the outbox for a job (AD-8); <c>status</c> is <c>Simulated</c> in the first version.</summary>
public sealed record OutboundMessageDto(
    Guid Id,
    Guid JobId,
    string Channel,
    string Recipient,
    string TemplateId,
    string Body,
    OutboundStatus Status,
    DateTime CreatedAtUtc);
