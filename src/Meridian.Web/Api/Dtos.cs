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
    ContractorRule? SuggestedContractorRule);

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

/// <summary>A person the "acting as" switch offers; send <c>Id</c> in the <c>X-Acting-As</c> header.</summary>
public sealed record StaffMemberDto(string Id, string Name);

/// <summary>A seeded contractor, for choosing one in review.</summary>
public sealed record ContractorDto(Guid Id, string Name, Category Trade, string Phone);

/// <summary>A seeded property, for choosing one in review.</summary>
public sealed record PropertyDto(Guid Id, string Name, Guid BuildingId, string? BuildingName);

public sealed record RejectJobRequest(string? Reason);

/// <summary>A person's edit of a Prepared job; null fields stay as they are. <c>UrgencyReason</c> is required to lower urgency.</summary>
public sealed record EditJobRequest(
    Category? Category,
    Urgency? Urgency,
    string? UrgencyReason,
    Guid? ContractorId,
    Guid? PropertyId);

public sealed record JobDetailMessageDto(Guid Id, string Sender, string Text, DateTime ReceivedAtUtc);

public sealed record JobDetailRuleDto(string Id, string MatchedPhrase);

/// <summary>The job's contractor; <c>Rule</c> is <c>Building</c>, <c>Default</c> (suggested by fixed code) or <c>Person</c> (chosen in review).</summary>
public sealed record JobDetailContractorDto(Guid Id, string Name, string Phone, Category Trade, ContractorRule? Rule);

public sealed record JobDetailEventDto(EventType Type, DateTime OccurredAtUtc, string Actor, string Summary);

/// <summary>
/// One job for the detail panel. <c>CanReview</c> is true while it is Prepared; <c>MissingForApproval</c> lists what
/// approval still needs (<c>category</c>, <c>contractor</c>).
/// </summary>
public sealed record JobDetailDto(
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
    JobDetailContractorDto? Contractor,
    IReadOnlyList<JobDetailRuleDto> Rules,
    IReadOnlyList<JobDetailMessageDto> Messages,
    IReadOnlyList<JobDetailEventDto> Events,
    bool CanReview,
    IReadOnlyList<string> MissingForApproval);
