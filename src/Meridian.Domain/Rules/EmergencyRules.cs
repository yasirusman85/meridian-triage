using System.Text.RegularExpressions;

namespace Meridian.Domain.Rules;

/// <summary>
/// The fixed emergency rules (R4, AD-3): plain code, no model call. They run on every message before the model, so
/// they still apply when the model fails (RULE-11).
/// <list type="bullet">
/// <item><c>gas</c>: any mention of gas or a gas smell, in English, Polish, Romanian or Urdu, or gas described
/// without the word (rotten eggs, sulphur).</item>
/// <item><c>carbon-monoxide</c>: carbon monoxide or a CO alarm.</item>
/// <item><c>water-through-ceiling</c>: water, a leak or a drip near a ceiling or a light fitting.</item>
/// <item><c>no-heating-winter</c>: no heating or no hot water, received October to April (UK local month).</item>
/// </list>
/// </summary>
public static class EmergencyRules
{
    /// <summary>First and last UK local month of the winter window, inclusive (October to April).</summary>
    public const int WinterStartMonth = 10;
    public const int WinterEndMonth = 4;

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>Europe/London, with a fall-back when the machine has no time-zone data (shared with the demo seed).</summary>
    public static readonly TimeZoneInfo London = FindLondon();

    private static readonly (string RuleId, Regex[] Patterns, bool WinterOnly)[] Rules =
    {
        (EmergencyRuleIds.Gas, Compile(EmergencyPhrases.Gas), false),
        (EmergencyRuleIds.CarbonMonoxide, Compile(EmergencyPhrases.CarbonMonoxide), false),
        (EmergencyRuleIds.WaterThroughCeiling, Compile(EmergencyPhrases.WaterThroughCeiling), false),
        (EmergencyRuleIds.NoHeatingWinter, Compile(EmergencyPhrases.NoHeating), true),
    };

    /// <summary>
    /// Evaluates every rule against one message. Returns one hit per rule that fired, in rule order, each with the
    /// exact span of <paramref name="text"/> that matched; an empty list when none fired.
    /// </summary>
    /// <param name="text">The message text as received.</param>
    /// <param name="receivedAtUtc">When the message was received (UTC); its Europe/London month sets the winter window.</param>
    public static IReadOnlyList<RuleHit> Evaluate(string text, DateTime receivedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(text);
        Guards.RequireUtc(receivedAtUtc, nameof(receivedAtUtc));

        var winter = IsWinter(receivedAtUtc);
        var hits = new List<RuleHit>();
        foreach (var (ruleId, patterns, winterOnly) in Rules)
        {
            if (winterOnly && !winter)
            {
                continue;
            }

            foreach (var pattern in patterns)
            {
                var match = pattern.Match(text);
                if (match.Success)
                {
                    hits.Add(new RuleHit(ruleId, match.Value.Trim()));
                    break;
                }
            }
        }

        return hits;
    }

    /// <summary>True when <paramref name="receivedAtUtc"/> falls in October to April, UK local time.</summary>
    public static bool IsWinter(DateTime receivedAtUtc)
    {
        Guards.RequireUtc(receivedAtUtc, nameof(receivedAtUtc));
        var month = TimeZoneInfo.ConvertTimeFromUtc(receivedAtUtc, London).Month;
        return month >= WinterStartMonth || month <= WinterEndMonth;
    }

    private static Regex[] Compile(IEnumerable<string> patterns) =>
        patterns.Select(p => new Regex(p, Options)).ToArray();

    private static TimeZoneInfo FindLondon()
    {
        foreach (var id in new[] { "Europe/London", "GMT Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        // No time-zone data on this machine: the UK rule since 1996 (BST from 01:00 UTC on the last Sunday of March
        // to 01:00 UTC on the last Sunday of October).
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 10, 5, DayOfWeek.Sunday));
        return TimeZoneInfo.CreateCustomTimeZone("Europe/London", TimeSpan.Zero, "UK", "GMT", "BST", new[] { rule });
    }
}
