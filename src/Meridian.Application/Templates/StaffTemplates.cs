namespace Meridian.Application.Templates;

/// <summary>
/// The fixed texts sent to staff when an emergency escalates (AD-7, R18, R19). Placeholders are the job reference,
/// the resolved address, the category and the fired emergency rules; model output and tenant text are never put in.
/// </summary>
public static class StaffTemplates
{
    public const string ReferencePlaceholder = "{reference}";
    public const string AddressPlaceholder = "{address}";
    public const string CategoryPlaceholder = "{category}";
    public const string RulesPlaceholder = "{rules}";

    /// <summary>Sent to the primary contact when an emergency arrives.</summary>
    public const string EscalationPrimaryId = "escalation-primary";

    /// <summary>Sent to the backup when the primary has not acknowledged in time.</summary>
    public const string EscalationBackupId = "escalation-backup";

    /// <summary>Shown for the address when the job's property is unresolved.</summary>
    public const string UnresolvedAddress = "address not resolved";

    /// <summary>Shown for the rules when none fired (the emergency came from the AI's proposal or a follow-up).</summary>
    public const string NoRules = "none";

    public const string EscalationPrimary =
        "EMERGENCY {reference}: {category} at {address}. Rules: {rules}. Please open the Meridian queue and acknowledge it.";

    public const string EscalationBackup =
        "EMERGENCY {reference} has not been acknowledged by the first contact: {category} at {address}. Rules: {rules}. You are the backup: please open the Meridian queue and acknowledge it.";

    /// <summary>Every staff template id, for listing staff notifications.</summary>
    public static readonly IReadOnlyList<string> Ids = new[] { EscalationPrimaryId, EscalationBackupId };

    public static string Text(string templateId) => templateId switch
    {
        EscalationPrimaryId => EscalationPrimary,
        EscalationBackupId => EscalationBackup,
        _ => throw new ArgumentOutOfRangeException(nameof(templateId), templateId, "Unknown staff template."),
    };

    /// <summary>Renders a staff template with only its four placeholders filled in.</summary>
    /// <param name="address">The resolved property name, or null when unresolved.</param>
    /// <param name="ruleIds">The fired emergency rule ids; empty when none fired.</param>
    public static string Render(string templateId, string reference, string? address, string category, IEnumerable<string> ruleIds)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A reference is required.", nameof(reference));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("A category is required.", nameof(category));
        }

        ArgumentNullException.ThrowIfNull(ruleIds);
        var rules = string.Join(", ", ruleIds);
        return Text(templateId)
            .Replace(ReferencePlaceholder, reference, StringComparison.Ordinal)
            .Replace(AddressPlaceholder, string.IsNullOrWhiteSpace(address) ? UnresolvedAddress : address, StringComparison.Ordinal)
            .Replace(CategoryPlaceholder, category, StringComparison.Ordinal)
            .Replace(RulesPlaceholder, rules.Length == 0 ? NoRules : rules, StringComparison.Ordinal);
    }
}
