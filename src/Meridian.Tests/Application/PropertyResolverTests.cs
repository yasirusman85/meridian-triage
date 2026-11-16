using System.Text.Json;
using Meridian.Application.Triage;
using Meridian.Domain.Properties;
using Meridian.Tests.Domain;
using static Meridian.Tests.Application.SubmitMessageTests;

namespace Meridian.Tests.Application;

/// <summary><see cref="PropertyResolver"/> against the seeded properties (AD-13).</summary>
public class PropertyResolverTests
{
    private static readonly PropertyResolver Resolver = new(FakePropertyRepository.Seeded);

    [Fact]
    public void Joys_address_resolves_to_flat_4_22_Pemberton_Road()
    {
        AssertResolves("Flat 4, 22 Pemberton Road", "Flat 4, 22 Pemberton Road");
    }

    [Fact]
    public void Unknown_address_is_unresolved()
    {
        var match = Resolver.Resolve("Flat 3, Nowhere Street");

        Assert.Equal(PropertyResolution.Unresolved, match.Resolution);
        Assert.Null(match.Property);
    }

    [Fact]
    public void A_block_named_without_a_flat_is_ambiguous()
    {
        var match = Resolver.Resolve("Alder Court");

        Assert.Equal(PropertyResolution.Ambiguous, match.Resolution);
        Assert.Null(match.Property);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData(", .")]
    public void No_address_is_no_address(string? text)
    {
        var match = Resolver.Resolve(text);

        Assert.Equal(PropertyResolution.NoAddress, match.Resolution);
        Assert.Null(match.Property);
    }

    [Theory]
    [InlineData("Mieszkanie 9, Beechwood House", "Flat 9, Beechwood House")]
    [InlineData("Apartamentul 12, Orchard Mill", "Flat 12, Orchard Mill")]
    [InlineData("فلیٹ 2، 9 Wellington Grove", "Flat 2, 9 Wellington Grove")]
    public void A_translated_unit_word_resolves(string text, string expected)
    {
        AssertResolves(text, expected);
    }

    [Theory]
    [InlineData("14b Alder Court", "14b Alder Court")]
    [InlineData("14B ALDER COURT.", "14b Alder Court")]
    [InlineData("Michael Walsh, 14b Alder Court", "14b Alder Court")]
    [InlineData("flat 5 31 Dover st", "Flat 5, 31 Dover Street")]
    [InlineData("Flat 8 Hawthorn Hse", "Flat 8, Hawthorn House")]
    [InlineData("Pennine House, flat 4", "Flat 4, Pennine House")]
    [InlineData("flat4, 22 pemberton rd", "Flat 4, 22 Pemberton Road")]
    [InlineData("8 Carlton Street", "8 Carlton Street")]
    [InlineData("17 Reed Lane, Levenshulme", "17 Reed Lane")]
    public void Case_punctuation_abbreviations_and_word_order_are_tolerated(string text, string expected)
    {
        AssertResolves(text, expected);
    }

    [Fact]
    public void The_house_number_tells_two_buildings_on_the_same_street_apart()
    {
        AssertResolves("Flat 6, 18 Clarendon Road", "Flat 6, 18 Clarendon Road");
        AssertResolves("Apartamentul 5, 30 Clarendon Road", "Flat 5, 30 Clarendon Road");
        Assert.Equal(PropertyResolution.Unresolved, Resolver.Resolve("Flat 6, 99 Clarendon Road").Resolution);
    }

    [Theory]
    [InlineData("Flat 99, 22 Pemberton Road")] // the building has no flat 99
    [InlineData("Flat 14, Alder Court")] // 14a and 14b exist, 14 does not
    [InlineData("Flat 2, 8 Carlton Street")] // a house has no flats
    [InlineData("Pemberton Road")] // the building's number is part of its address
    public void A_known_building_with_an_unknown_home_is_unresolved(string text)
    {
        var match = Resolver.Resolve(text);

        Assert.False(match.IsResolved);
        Assert.Null(match.Property);
    }

    [Theory]
    [InlineData("22 Pemberton Road")]
    [InlineData("Flat 4 or 5, 22 Pemberton Road")]
    [InlineData("14b Alder Court and 8 Carlton Street")]
    public void More_than_one_possible_home_is_ambiguous(string text)
    {
        Assert.Equal(PropertyResolution.Ambiguous, Resolver.Resolve(text).Resolution);
    }

    public static IEnumerable<object[]> EvalPropertyTexts()
    {
        foreach (var path in Directory.EnumerateFiles(EmergencyRulesTests.FindEvalCases(), "*.json").Order(StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            foreach (var issue in root.GetProperty("expected").GetProperty("issues").EnumerateArray())
            {
                var text = issue.GetProperty("property_text");
                if (text.ValueKind == JsonValueKind.String)
                {
                    yield return new object[] { root.GetProperty("id").GetString()!, text.GetString()! };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(EvalPropertyTexts))]
    public void Every_eval_case_address_resolves_to_a_seeded_property(string id, string propertyText)
    {
        var match = Resolver.Resolve(propertyText);

        Assert.True(match.IsResolved, $"{id}: '{propertyText}' → {match.Resolution}");
    }

    [Fact]
    public void Seeded_property_ids_are_stable_and_unique()
    {
        var again = Meridian.Infrastructure.Persistence.SeedData.CreateBuildings().SelectMany(b => b.Properties).ToList();

        Assert.Equal(FakePropertyRepository.Seeded.Select(p => p.Id), again.Select(p => p.Id));
        Assert.Equal(again.Count, again.Select(p => p.Id).Distinct().Count());
        Assert.Equal(again.Count, again.Select(p => p.Name).Distinct().Count());
    }

    private static void AssertResolves(string text, string expectedName)
    {
        var match = Resolver.Resolve(text);

        Assert.Equal(PropertyResolution.Resolved, match.Resolution);
        Property property = match.Property!;
        Assert.Equal(expectedName, property.Name);
    }
}
