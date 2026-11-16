using Meridian.Domain.Rules;

namespace Meridian.Application.Templates;

/// <summary>
/// The fixed texts sent to tenants (AD-7, RULE-5, RULE-6). The only placeholder is <c>{reference}</c>, filled with the
/// job reference; model output, addresses and tenant text are never put into a tenant message.
/// </summary>
public static class TenantTemplates
{
    public const string ReferencePlaceholder = "{reference}";

    public const string AcknowledgementId = "acknowledgement";
    public const string GasSafetyId = "gas-safety";

    public const string Acknowledgement =
        "Thank you, we have received your maintenance report. Your reference is {reference}. If anything changes or gets worse, reply to this message quoting your reference. If you smell gas, call 0800 111 999 now.";

    /// <summary>The gas safety message (RULE-6). A constant: never reworded.</summary>
    public const string GasSafety =
        "If you can smell gas or your carbon monoxide alarm is sounding: leave the property now, do not use light switches or anything electrical, do not use flames, and call the National Gas Emergency line on 0800 111 999. Your report reference is {reference}. We have been told and will contact you.";

    /// <summary>The template text for <paramref name="templateId"/>.</summary>
    public static string Text(string templateId) => templateId switch
    {
        AcknowledgementId => Acknowledgement,
        GasSafetyId => GasSafety,
        _ => throw new ArgumentOutOfRangeException(nameof(templateId), templateId, "Unknown tenant template."),
    };

    /// <summary>Renders a template with only the job reference filled in.</summary>
    public static string Render(string templateId, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A reference is required.", nameof(reference));
        }

        return Text(templateId).Replace(ReferencePlaceholder, reference, StringComparison.Ordinal);
    }

    /// <summary>
    /// The acknowledgement template for a job: <see cref="GasSafetyId"/> when the gas or carbon-monoxide rule fired,
    /// otherwise <see cref="AcknowledgementId"/>.
    /// </summary>
    public static string AcknowledgementFor(IEnumerable<string> firedRuleIds)
    {
        ArgumentNullException.ThrowIfNull(firedRuleIds);
        return firedRuleIds.Any(id => id is EmergencyRuleIds.Gas or EmergencyRuleIds.CarbonMonoxide)
            ? GasSafetyId
            : AcknowledgementId;
    }
}
