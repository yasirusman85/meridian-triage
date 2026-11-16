namespace Meridian.Domain.Rules;

/// <summary>One emergency rule that fired on a message: the rule id and the text that matched it.</summary>
/// <param name="RuleId">One of <see cref="EmergencyRuleIds"/>.</param>
/// <param name="MatchedPhrase">The exact span of the message text that matched, as written by the tenant.</param>
public sealed record RuleHit(string RuleId, string MatchedPhrase);

/// <summary>Stable ids of the emergency rules (R4). They are stored on jobs and in events, so never rename one.</summary>
public static class EmergencyRuleIds
{
    public const string Gas = "gas";
    public const string CarbonMonoxide = "carbon-monoxide";
    public const string WaterThroughCeiling = "water-through-ceiling";
    public const string NoHeatingWinter = "no-heating-winter";

    public static readonly IReadOnlyList<string> All = new[] { Gas, CarbonMonoxide, WaterThroughCeiling, NoHeatingWinter };
}
