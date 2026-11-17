namespace Meridian.Application.Templates;

/// <summary>
/// The fixed text sent to a contractor when a job is approved or re-sent (AD-7, AD-11). Placeholders: the job
/// reference, the resolved address, the category, the English summary and the single-job link. The summary is model
/// output: it is shown to the contractor as job information only, never to a tenant (RULE-5 covers tenant text).
/// </summary>
public static class ContractorTemplates
{
    public const string ReferencePlaceholder = "{reference}";
    public const string AddressPlaceholder = "{address}";
    public const string CategoryPlaceholder = "{category}";
    public const string SummaryPlaceholder = "{summary}";
    public const string LinkPlaceholder = "{link}";

    /// <summary>The template id of a contractor job message.</summary>
    public const string JobId = "contractor-job";

    /// <summary>Shown for the summary when the job has none (no model outcome, e.g. a job a person created).</summary>
    public const string NoSummary = "no summary recorded";

    public const string Job =
        "Meridian job {reference}: {category} at {address}. Summary: {summary}. Please attend and, once the job is done, open {link} and press Mark attended.";

    /// <summary>Renders the contractor message.</summary>
    /// <param name="address">The property name, or null when unresolved.</param>
    /// <param name="summary">The English summary, or null when there is none.</param>
    /// <param name="link">The single-job link, e.g. <c>/c/&lt;token&gt;</c>.</param>
    public static string Render(string reference, string? address, string category, string? summary, string link)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A reference is required.", nameof(reference));
        }

        if (string.IsNullOrWhiteSpace(link))
        {
            throw new ArgumentException("A link is required.", nameof(link));
        }

        return Job
            .Replace(ReferencePlaceholder, reference, StringComparison.Ordinal)
            .Replace(AddressPlaceholder, string.IsNullOrWhiteSpace(address) ? StaffTemplates.UnresolvedAddress : address, StringComparison.Ordinal)
            .Replace(CategoryPlaceholder, category, StringComparison.Ordinal)
            .Replace(SummaryPlaceholder, string.IsNullOrWhiteSpace(summary) ? NoSummary : summary, StringComparison.Ordinal)
            .Replace(LinkPlaceholder, link, StringComparison.Ordinal);
    }
}
