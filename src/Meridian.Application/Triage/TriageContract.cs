using Meridian.Domain.Jobs;

namespace Meridian.Application.Triage;

/// <summary>
/// Contract <c>triage.v1</c> (AD-2): the typed shape of a validated model answer.
/// Only <see cref="TriageValidator"/> creates these, from raw model output.
/// </summary>
public static class TriageContract
{
    public const string Version = "triage.v1";

    /// <summary>Wire names of the allowed categories, exactly as the prompt lists them.</summary>
    public static readonly IReadOnlyList<string> Categories =
        new[] { "Plumbing", "Electrical", "Heating", "Appliances", "LocksAndDoors", "Pest", "General", "Unsure" };

    /// <summary>Wire names of the allowed urgencies.</summary>
    public static readonly IReadOnlyList<string> Urgencies = new[] { "Routine", "Urgent", "Emergency" };

    /// <summary>Wire names of the allowed confidence values.</summary>
    public static readonly IReadOnlyList<string> Confidences = new[] { "high", "medium", "low" };
}

public enum TriageConfidence
{
    High,
    Medium,
    Low,
}

/// <summary>A validated <c>triage.v1</c> answer.</summary>
public sealed record TriageResponse(string Language, IReadOnlyList<TriageIssue> Issues);

/// <summary>One issue in a validated <c>triage.v1</c> answer.</summary>
public sealed record TriageIssue(
    string SummaryEn,
    Category Category,
    Urgency Urgency,
    string PropertyText,
    string Explanation,
    bool VulnerabilityMentioned,
    TriageConfidence Confidence);
