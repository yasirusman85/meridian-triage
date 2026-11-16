using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meridian.Domain.Properties;

namespace Meridian.Application.Triage;

/// <summary>How a <c>property_text</c> was resolved.</summary>
public enum PropertyResolution
{
    /// <summary>Exactly one seeded property matched.</summary>
    Resolved,

    /// <summary>The text names no address.</summary>
    NoAddress,

    /// <summary>No seeded building matched, or the building matched but the flat is not one Meridian knows.</summary>
    Unresolved,

    /// <summary>More than one property could be meant, e.g. a building with several flats and no flat number.</summary>
    Ambiguous,
}

/// <summary>The resolver's answer: the property when exactly one matched, otherwise null.</summary>
public sealed record PropertyMatch(PropertyResolution Resolution, Property? Property)
{
    public static readonly PropertyMatch NoAddress = new(PropertyResolution.NoAddress, null);
    public static readonly PropertyMatch Unresolved = new(PropertyResolution.Unresolved, null);
    public static readonly PropertyMatch Ambiguous = new(PropertyResolution.Ambiguous, null);

    public bool IsResolved => Resolution == PropertyResolution.Resolved;
}

/// <summary>
/// Matches the model's <c>property_text</c> against the seeded properties (AD-13). Plain code, no model call.
/// <para>
/// The text and each building name are normalised: lower case, accents removed, punctuation (any script) to spaces,
/// common abbreviations expanded (<c>st</c>, <c>rd</c>, <c>hse</c>, ...) and unit words dropped in the languages the
/// eval set uses (Flat, Apt, Mieszkanie, Apartamentul, فلیٹ). A building matches when its name words appear in order;
/// a name that starts with a number (<c>22 Pemberton Road</c>) also needs that number just before them. Remaining
/// number tokens (<c>4</c>, <c>14b</c>) pick the flat. Exactly one property → resolved; none or several → null.
/// </para>
/// </summary>
public sealed class PropertyResolver
{
    private static readonly Regex NumberToken = new("^[0-9]+[a-z]?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly IReadOnlyDictionary<string, string> Abbreviations = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["st"] = "street",
        ["str"] = "street",
        ["rd"] = "road",
        ["hse"] = "house",
        ["ave"] = "avenue",
        ["av"] = "avenue",
        ["ln"] = "lane",
        ["ct"] = "court",
        ["crt"] = "court",
        ["gr"] = "grove",
        ["gro"] = "grove",
        ["pl"] = "place",
        ["cl"] = "close",
    };

    private static readonly HashSet<string> UnitWords = new(StringComparer.Ordinal)
    {
        "flat", "flats", "apt", "apartment", "apartament", "apartamentul", "mieszkanie", "mieszkania", "mieszk",
        "unit", "no", "nr", "number",
        "\u0641\u0644\u06CC\u0679", // فلیٹ (Urdu, Farsi yeh)
        "\u0641\u0644\u064A\u062A", // فليت (Arabic)
    };

    private readonly IReadOnlyList<BuildingTokens> _buildings;

    /// <param name="properties">Every seeded property, each with its <see cref="Property.Building"/> loaded.</param>
    public PropertyResolver(IEnumerable<Property> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        _buildings = properties
            .GroupBy(p => p.BuildingId)
            .Select(g =>
            {
                var building = g.First().Building
                    ?? throw new ArgumentException("Every property needs its building loaded.", nameof(properties));
                return BuildingTokens.From(building, g.ToList());
            })
            .ToList();
    }

    public PropertyMatch Resolve(string? propertyText)
    {
        var tokens = Tokenize(propertyText);
        if (tokens.Count == 0)
        {
            return PropertyMatch.NoAddress;
        }

        var matched = _buildings
            .Select(b => (Building: b, Consumed: b.Match(tokens)))
            .Where(m => m.Consumed is not null)
            .ToList();
        switch (matched.Count)
        {
            case 0:
                return PropertyMatch.Unresolved;
            case > 1:
                // Two addresses in one text: a person picks.
                return PropertyMatch.Ambiguous;
        }

        var (building, consumed) = matched[0];
        var units = tokens
            .Where((t, i) => !consumed!.Contains(i) && NumberToken.IsMatch(t))
            .ToHashSet(StringComparer.Ordinal);
        if (building.HasFlats && units.Count == 0)
        {
            // A block named without a flat number could be any of its flats.
            return PropertyMatch.Ambiguous;
        }

        var candidates = building.PropertiesFor(units).ToList();
        return candidates.Count switch
        {
            1 => new PropertyMatch(PropertyResolution.Resolved, candidates[0]),
            0 => PropertyMatch.Unresolved,
            _ => PropertyMatch.Ambiguous,
        };
    }

    /// <summary>The normalised tokens of <paramref name="text"/>, as the resolver compares them.</summary>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var decomposed = text.Normalize(NormalizationForm.FormD).ToLowerInvariant();
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        var tokens = new List<string>();
        foreach (var raw in builder.ToString().Normalize(NormalizationForm.FormC).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = SplitUnitPrefix(raw);
            if (token is null || UnitWords.Contains(token))
            {
                continue;
            }

            tokens.Add(Abbreviations.TryGetValue(token, out var full) ? full : token);
        }

        return tokens;
    }

    /// <summary>Turns <c>flat4</c> into <c>4</c>; leaves other tokens as they are; drops a bare unit word.</summary>
    private static string? SplitUnitPrefix(string token)
    {
        foreach (var word in UnitWords)
        {
            if (token.Length > word.Length && token.StartsWith(word, StringComparison.Ordinal) && char.IsDigit(token[word.Length]))
            {
                return token[word.Length..];
            }
        }

        return token;
    }

    private sealed class BuildingTokens
    {
        private readonly string? _number;
        private readonly IReadOnlyList<string> _words;
        private readonly IReadOnlyList<Property> _properties;

        private BuildingTokens(string? number, IReadOnlyList<string> words, IReadOnlyList<Property> properties)
        {
            _number = number;
            _words = words;
            _properties = properties;
        }

        public bool HasFlats => _properties.Any(p => p.Unit is not null);

        public static BuildingTokens From(Building building, IReadOnlyList<Property> properties)
        {
            var tokens = Tokenize(building.Name);
            var number = tokens.Count > 1 && NumberToken.IsMatch(tokens[0]) ? tokens[0] : null;
            var words = number is null ? tokens : tokens.Skip(1).ToList();
            if (words.Count == 0)
            {
                throw new ArgumentException($"Building '{building.Name}' has no name words to match.");
            }

            return new BuildingTokens(number, words, properties);
        }

        /// <summary>The token indexes the building name used, or null when it does not appear.</summary>
        public HashSet<int>? Match(IReadOnlyList<string> tokens)
        {
            for (var start = 0; start + _words.Count <= tokens.Count; start++)
            {
                var all = true;
                for (var k = 0; k < _words.Count && all; k++)
                {
                    all = string.Equals(tokens[start + k], _words[k], StringComparison.Ordinal);
                }

                if (!all)
                {
                    continue;
                }

                var used = Enumerable.Range(start, _words.Count).ToHashSet();
                if (_number is null)
                {
                    return used;
                }

                if (start > 0 && string.Equals(tokens[start - 1], _number, StringComparison.Ordinal))
                {
                    used.Add(start - 1);
                    return used;
                }
            }

            return null;
        }

        public IEnumerable<Property> PropertiesFor(IReadOnlySet<string> units)
        {
            if (!HasFlats)
            {
                // A house: any extra flat number means a home Meridian does not know.
                return units.Count == 0 ? _properties : Array.Empty<Property>();
            }

            return _properties.Where(p => units.Contains(p.Unit!.ToLowerInvariant()));
        }
    }
}
