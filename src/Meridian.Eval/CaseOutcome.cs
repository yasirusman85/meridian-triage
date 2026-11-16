using Meridian.Domain.Jobs;

namespace Meridian.Eval;

/// <summary>
/// One issue as the system prepared it, after the rules, the model, the validator and the merge.
/// For an issue that became its own job, category and urgency are the job's. For an issue attached to an earlier job
/// as a follow-up, they are the message read on its own (the README's rule): the category the pipeline gives the issue
/// and the higher of the rule result and the model's urgency; the job's own urgency is the earlier case's.
/// </summary>
public sealed record ActualIssue(
    Category Category,
    Urgency Urgency,
    bool VulnerabilityMentioned,
    string? PropertyText,
    string JobReference,
    bool IsFollowUp);

/// <summary>What the pipeline did with one case's message.</summary>
/// <param name="NewJobReferences">Jobs this message started.</param>
/// <param name="AttachedToReferences">Earlier jobs this message was attached to as a follow-up.</param>
/// <param name="ModelPrepared">True when the model gave a valid answer.</param>
/// <param name="ModelFailureReason">Why the model step failed (e.g. <c>no-recorded-response</c>), when it did.</param>
/// <param name="Error">An exception the pipeline threw for this case; the case scores as nothing produced.</param>
public sealed record CaseOutcome(
    string CaseId,
    IReadOnlyList<ActualIssue> Issues,
    IReadOnlyList<string> NewJobReferences,
    IReadOnlyList<string> AttachedToReferences,
    bool ModelPrepared,
    string? ModelFailureReason,
    int ModelCalls,
    long? LatencyMs,
    int? InputTokens,
    int? OutputTokens,
    string? Error = null);
