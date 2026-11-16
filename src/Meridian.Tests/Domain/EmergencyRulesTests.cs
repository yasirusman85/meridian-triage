using System.Text.Json;
using Meridian.Domain.Rules;

namespace Meridian.Tests.Domain;

/// <summary>The story's I/O matrix rows for the rules, near misses, and the labelled eval set.</summary>
public class EmergencyRulesTests
{
    private static readonly DateTime January = new(2026, 1, 14, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime July = new(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Expected emergencies that no fixed rule covers (eval README "How the labels follow the rules" and "Limits"):
    /// the AI must raise these. <c>real-04</c> is a heating complaint that does not say "no heating" (its notes).
    /// </summary>
    private static readonly HashSet<string> AiOnlyEmergencies = new(StringComparer.Ordinal)
    {
        "var-15", "var-30", "var-31", "var-38", "real-04",
    };

    [Theory]
    [InlineData("I can smell gas in the kitchen", "gas")]
    [InlineData("I CAN SMELL GAS!!!", "gas")]
    [InlineData("smells like rotten eggs near the boiler", "gas")]
    [InlineData("there's an eggy smell in the hall", "gas")]
    [InlineData("a strong smell of sulphur by the meter", "gas")]
    [InlineData("i think theres a gass leak", "gas")]
    [InlineData("CO alarm going off", "carbon-monoxide")]
    [InlineData("the carbon monoxide detector is beeping", "carbon-monoxide")]
    [InlineData("c.o. alarm keeps beeping", "carbon-monoxide")]
    [InlineData("czujnik czadu piszczy", "carbon-monoxide")]
    [InlineData("water coming through the ceiling", "water-through-ceiling")]
    [InlineData("dripping from the light fitting upstairs", "water-through-ceiling")]
    [InlineData("the ceiling is leaking onto the bed", "water-through-ceiling")]
    [InlineData("theres a leek from the cieling", "water-through-ceiling")]
    [InlineData("woda kapie z sufitu", "water-through-ceiling")]
    [InlineData("curge apă din tavan", "water-through-ceiling")]
    public void A_rule_fires_on_its_phrases_at_any_time_of_year(string text, string ruleId)
    {
        foreach (var received in new[] { January, July })
        {
            var hit = Assert.Single(EmergencyRules.Evaluate(text, received));
            Assert.Equal(ruleId, hit.RuleId);
            Assert.False(string.IsNullOrWhiteSpace(hit.MatchedPhrase));
            Assert.Contains(hit.MatchedPhrase, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_matched_phrase_is_the_span_of_the_message_as_written()
    {
        var hit = Assert.Single(EmergencyRules.Evaluate("Hi, I can smell Gas in the kitchen.", July));

        Assert.Equal("Gas", hit.MatchedPhrase);
    }

    [Theory]
    [InlineData("tap dripping")]
    [InlineData("the bathroom tap has been dripping for two weeks")]
    [InlineData("leak under the sink")]
    [InlineData("there's a small drip under the kitchen sink, I've put a bowl under it")]
    [InlineData("condensation on the window sill, water pooling")]
    [InlineData("black mould on the ceiling above the shower")]
    [InlineData("the boiler is making a loud banging noise when it comes on. It's still working and we've got hot water. Didn't do this before.")]
    [InlineData("boiler is making a whistling and knocking noise every time the heating kicks in. Everything still works.")]
    [InlineData("the light in the hallway cupboard doesn't work")]
    [InlineData("my company & co. sent a letter")]
    public void Near_misses_do_not_fire_even_in_winter(string text)
    {
        Assert.Empty(EmergencyRules.Evaluate(text, January));
    }

    [Theory]
    [InlineData("no heating")]
    [InlineData("No hot water and no heating at all since this morning")]
    [InlineData("heating isn't working at all")]
    [InlineData("heating isn’t working")]
    [InlineData("Boiler's stopped completely, radiators stone cold")]
    [InlineData("the heating has packed in")]
    [InlineData("we are without hot water")]
    [InlineData("nie ma ogrzewania od wczoraj")]
    [InlineData("ogrzewanie nie działa")]
    [InlineData("nu avem căldură de ieri")]
    [InlineData("centrala nu merge")]
    public void No_heating_in_January_is_an_emergency(string text)
    {
        var hit = Assert.Single(EmergencyRules.Evaluate(text, January));
        Assert.Equal(EmergencyRuleIds.NoHeatingWinter, hit.RuleId);
    }

    [Theory]
    [InlineData("no heating")]
    [InlineData("heating isn't working at all")]
    [InlineData("no hot water")]
    public void No_heating_in_July_is_not_a_rule_hit(string text)
    {
        Assert.Empty(EmergencyRules.Evaluate(text, July));
    }

    [Theory]
    // 08:10 BST on 1 October is 07:10 UTC: inside the window (matrix row "Winter edge").
    [InlineData("2026-10-01T07:10:00Z", true)]
    // 00:30 BST on 1 October is still 30 September in UTC; the UK month decides.
    [InlineData("2026-09-30T23:30:00Z", true)]
    [InlineData("2026-09-30T22:30:00Z", false)]
    // 00:30 BST on 1 May is still 30 April in UTC: outside the window.
    [InlineData("2026-04-30T23:30:00Z", false)]
    [InlineData("2026-04-30T22:30:00Z", true)]
    [InlineData("2026-01-14T00:00:00Z", true)]
    [InlineData("2026-12-31T23:59:59Z", true)]
    [InlineData("2026-07-14T12:00:00Z", false)]
    [InlineData("2026-05-02T10:00:00Z", false)]
    public void The_winter_window_uses_the_Europe_London_month(string receivedAt, bool inWindow)
    {
        var received = DateTime.Parse(receivedAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

        Assert.Equal(inWindow, EmergencyRules.IsWinter(received));
        Assert.Equal(inWindow, EmergencyRules.Evaluate("no heating", received).Count == 1);
    }

    [Fact]
    public void Several_rules_can_fire_on_one_message_in_rule_order()
    {
        var hits = EmergencyRules.Evaluate("No heating, the CO alarm is going off and I can smell gas", January);

        Assert.Equal(
            new[] { EmergencyRuleIds.Gas, EmergencyRuleIds.CarbonMonoxide, EmergencyRuleIds.NoHeatingWinter },
            hits.Select(h => h.RuleId));
    }

    [Fact]
    public void Times_must_be_utc()
    {
        Assert.Throws<ArgumentException>(() =>
            EmergencyRules.Evaluate("gas", DateTime.SpecifyKind(January, DateTimeKind.Local)));
    }

    [Fact]
    public void A_long_message_is_evaluated_quickly()
    {
        var text = string.Concat(Enumerable.Repeat("heating boiler ceiling water the a ", 2_000));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        EmergencyRules.Evaluate(text, January);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    [Theory]
    [MemberData(nameof(RuleEmergencyCases))]
    public void Every_eval_case_with_a_rule_emergency_gets_a_hit(string id, string text, DateTime receivedAtUtc)
    {
        Assert.True(EmergencyRules.Evaluate(text, receivedAtUtc).Count > 0, $"{id}: no rule fired on \"{text}\"");
    }

    [Theory]
    [MemberData(nameof(NonEmergencyCases))]
    public void No_eval_case_without_an_expected_emergency_gets_a_hit(string id, string text, DateTime receivedAtUtc)
    {
        var hits = EmergencyRules.Evaluate(text, receivedAtUtc);
        Assert.True(hits.Count == 0, $"{id}: {string.Join(", ", hits)} would over-escalate");
    }

    [Fact]
    public void The_eval_set_is_found()
    {
        Assert.Equal(52, EvalCases().Count());
    }

    public static IEnumerable<object[]> RuleEmergencyCases() =>
        EvalCases()
            .Where(c => c.HasEmergency && !AiOnlyEmergencies.Contains(c.Id))
            .Select(c => new object[] { c.Id, c.Text, c.ReceivedAtUtc });

    public static IEnumerable<object[]> NonEmergencyCases() =>
        EvalCases()
            .Where(c => !c.HasEmergency)
            .Select(c => new object[] { c.Id, c.Text, c.ReceivedAtUtc });

    private static IEnumerable<EvalCase> EvalCases()
    {
        var directory = FindEvalCases();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var hasEmergency = root.GetProperty("expected").GetProperty("issues").EnumerateArray()
                .Any(i => i.GetProperty("urgency").GetString() == "Emergency");
            yield return new EvalCase(
                root.GetProperty("id").GetString()!,
                root.GetProperty("text").GetString()!,
                root.GetProperty("received_at").GetDateTime().ToUniversalTime(),
                hasEmergency);
        }
    }

    private static string FindEvalCases()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "eval", "cases");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("eval/cases not found above " + AppContext.BaseDirectory);
    }

    private sealed record EvalCase(string Id, string Text, DateTime ReceivedAtUtc, bool HasEmergency);
}
