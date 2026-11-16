using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;
using Meridian.Domain.Outbox;

namespace Meridian.Web.Api;

public sealed record SubmitMessageRequest(string? Sender, string? Text);

/// <summary>Every job the message made or was attached to, in the order of the model's issues.</summary>
public sealed record SubmitMessageResponse(Guid MessageId, IReadOnlyList<SubmittedJobDto> Jobs);

/// <param name="Outcome"><c>created</c> for a new job, <c>followUp</c> when attached to an existing open job.</param>
public sealed record SubmittedJobDto(Guid JobId, string Reference, string Outcome);

/// <summary>
/// A job in the review queue. <c>SuggestedContractor*</c> are null when a person must choose (Unsure, or no assignment);
/// <c>SuggestedContractorRule</c> says whether it is the building's usual contractor or the company default.
/// <c>EscalationState</c> is the emergency escalation (AD-6), with who acknowledged it and when.
/// </summary>
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
    int FollowUpCount,
    string? BuildingName,
    Guid? SuggestedContractorId,
    string? SuggestedContractorName,
    string? SuggestedContractorPhone,
    ContractorRule? SuggestedContractorRule,
    EscalationState EscalationState,
    string? EscalationAcknowledgedBy,
    DateTime? EscalationAcknowledgedAtUtc);

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
