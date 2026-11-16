using System.Text.Json.Nodes;
using Meridian.Application.Triage;
using Meridian.Domain.Jobs;

namespace Meridian.Tests.Application;

public class TriageValidatorTests
{
    internal const string ValidJson =
        """{"language":"pl","issues":[{"summary_en":"Mould on the bedroom wall.","category":"General","urgency":"Urgent","property_text":"Flat 9, Beechwood House","explanation":"Mould, child with asthma.","vulnerability_mentioned":true,"confidence":"medium"}]}""";

    [Fact]
    public void A_valid_answer_is_parsed_into_typed_values()
    {
        var result = TriageValidator.Validate(ValidJson);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal("pl", result.Response!.Language);
        var issue = Assert.Single(result.Response.Issues);
        Assert.Equal("Mould on the bedroom wall.", issue.SummaryEn);
        Assert.Equal(Category.General, issue.Category);
        Assert.Equal(Urgency.Urgent, issue.Urgency);
        Assert.Equal("Flat 9, Beechwood House", issue.PropertyText);
        Assert.Equal("Mould, child with asthma.", issue.Explanation);
        Assert.True(issue.VulnerabilityMentioned);
        Assert.Equal(TriageConfidence.Medium, issue.Confidence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("```json\n{}\n```")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public void Unparseable_or_non_object_output_is_rejected(string? raw)
    {
        var result = TriageValidator.Validate(raw);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData("summary_en")]
    [InlineData("category")]
    [InlineData("urgency")]
    [InlineData("property_text")]
    [InlineData("explanation")]
    [InlineData("vulnerability_mentioned")]
    [InlineData("confidence")]
    public void A_missing_issue_field_is_rejected(string field)
    {
        var json = Mutate(issue => issue.Remove(field));

        var result = TriageValidator.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith($"issues[0].{field}:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("summary_en")]
    [InlineData("category")]
    [InlineData("urgency")]
    [InlineData("property_text")]
    [InlineData("explanation")]
    [InlineData("vulnerability_mentioned")]
    [InlineData("confidence")]
    public void A_null_issue_field_is_rejected(string field)
    {
        var json = Mutate(issue => issue[field] = null);

        Assert.False(TriageValidator.Validate(json).IsValid);
    }

    [Theory]
    [InlineData("category", "Roofing")]
    [InlineData("category", "plumbing")]
    [InlineData("category", "")]
    [InlineData("urgency", "Critical")]
    [InlineData("urgency", "emergency")]
    [InlineData("confidence", "certain")]
    [InlineData("confidence", "High")]
    public void A_value_outside_the_enum_is_rejected(string field, string value)
    {
        var json = Mutate(issue => issue[field] = value);

        var result = TriageValidator.Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith($"issues[0].{field}:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_numeric_enum_value_is_rejected()
    {
        var json = Mutate(issue => issue["category"] = 1);

        Assert.False(TriageValidator.Validate(json).IsValid);
    }

    [Theory]
    [InlineData("summary_en")]
    [InlineData("explanation")]
    public void An_empty_text_field_is_rejected(string field)
    {
        var json = Mutate(issue => issue[field] = "  ");

        Assert.False(TriageValidator.Validate(json).IsValid);
    }

    [Fact]
    public void Vulnerability_must_be_a_boolean()
    {
        var json = Mutate(issue => issue["vulnerability_mentioned"] = "true");

        Assert.False(TriageValidator.Validate(json).IsValid);
    }

    [Fact]
    public void An_empty_property_text_is_allowed()
    {
        var json = Mutate(issue => issue["property_text"] = "");

        Assert.True(TriageValidator.Validate(json).IsValid);
    }

    [Fact]
    public void Every_category_and_urgency_in_the_contract_is_accepted()
    {
        foreach (var category in TriageContract.Categories)
        {
            foreach (var urgency in TriageContract.Urgencies)
            {
                var json = Mutate(issue =>
                {
                    issue["category"] = category;
                    issue["urgency"] = urgency;
                });
                Assert.True(TriageValidator.Validate(json).IsValid, $"{category}/{urgency}");
            }
        }
    }

    [Fact]
    public void Empty_issues_is_valid_and_means_not_maintenance()
    {
        var root = JsonNode.Parse(ValidJson)!.AsObject();
        root["issues"] = new JsonArray();

        var result = TriageValidator.Validate(root.ToJsonString());

        Assert.True(result.IsValid);
        Assert.Empty(result.Response!.Issues);
    }

    [Theory]
    [InlineData("""{"language":"en"}""")]
    [InlineData("""{"language":"en","issues":{}}""")]
    [InlineData("""{"language":"en","issues":["x"]}""")]
    public void Missing_or_malformed_issues_is_rejected(string json)
    {
        Assert.False(TriageValidator.Validate(json).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a-language-code-far-too-long")]
    public void A_missing_empty_or_oversized_language_is_rejected(string? language)
    {
        var root = JsonNode.Parse(ValidJson)!.AsObject();
        if (language is null)
        {
            root.Remove("language");
        }
        else
        {
            root["language"] = language;
        }

        var result = TriageValidator.Validate(root.ToJsonString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith("language:", StringComparison.Ordinal));
    }

    [Fact]
    public void One_bad_issue_rejects_the_whole_answer()
    {
        var root = JsonNode.Parse(ValidJson)!.AsObject();
        var second = root["issues"]![0]!.DeepClone().AsObject();
        second["category"] = "Roofing";
        root["issues"]!.AsArray().Add(second);

        var result = TriageValidator.Validate(root.ToJsonString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith("issues[1].category:", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_extra_fields_are_ignored()
    {
        var json = Mutate(issue => issue["extra"] = "ignored");

        Assert.True(TriageValidator.Validate(json).IsValid);
    }

    internal static string Mutate(Action<JsonObject> change)
    {
        var root = JsonNode.Parse(ValidJson)!.AsObject();
        change(root["issues"]![0]!.AsObject());
        return root.ToJsonString();
    }
}
