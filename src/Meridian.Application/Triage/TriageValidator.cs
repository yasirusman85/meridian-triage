using System.Text.Json;
using Meridian.Domain.Jobs;

namespace Meridian.Application.Triage;

/// <summary>The outcome of validating raw model output: a parsed answer or a list of errors, never both.</summary>
public sealed record TriageValidationResult(TriageResponse? Response, IReadOnlyList<string> Errors)
{
    public bool IsValid => Response is not null;
}

/// <summary>
/// The single parser and validator for <c>triage.v1</c> (AD-2). The code trusts nothing from the model
/// until it passes here: values outside the enums and missing fields are rejected (AD-10). An empty <c>issues</c>
/// array is valid: it means the message reports no maintenance problem, and the pipeline still makes one Unsure job.
/// Unknown extra fields are ignored. Enum values are matched exactly (case-sensitive).
/// </summary>
public static class TriageValidator
{
    public const int MaxLanguageLength = 16;

    public static TriageValidationResult Validate(string? rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return Invalid("Output is empty.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawOutput);
        }
        catch (JsonException ex)
        {
            return Invalid("Output is not valid JSON: " + ex.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid("Output must be a JSON object.");
            }

            var errors = new List<string>();
            var language = RequireString(root, "language", string.Empty, errors, allowEmpty: false);
            if (language is not null && language.Length > MaxLanguageLength)
            {
                errors.Add($"language: must be at most {MaxLanguageLength} characters.");
                language = null;
            }

            var issues = new List<TriageIssue>();
            if (!root.TryGetProperty("issues", out var issuesElement))
            {
                errors.Add("issues: is required.");
            }
            else if (issuesElement.ValueKind != JsonValueKind.Array)
            {
                errors.Add("issues: must be an array.");
            }
            else
            {
                var index = 0;
                foreach (var issueElement in issuesElement.EnumerateArray())
                {
                    var issue = ValidateIssue(issueElement, $"issues[{index}]", errors);
                    if (issue is not null)
                    {
                        issues.Add(issue);
                    }

                    index++;
                }
            }

            return errors.Count > 0 || language is null
                ? new TriageValidationResult(null, errors)
                : new TriageValidationResult(new TriageResponse(language, issues), Array.Empty<string>());
        }
    }

    private static TriageIssue? ValidateIssue(JsonElement element, string path, List<string> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path}: must be an object.");
            return null;
        }

        var before = errors.Count;
        var summary = RequireString(element, "summary_en", path, errors, allowEmpty: false);
        var category = RequireEnum(element, "category", path, TriageContract.Categories, errors);
        var urgency = RequireEnum(element, "urgency", path, TriageContract.Urgencies, errors);
        // property_text may be empty when the message names no property; it must still be present.
        var propertyText = RequireString(element, "property_text", path, errors, allowEmpty: true);
        var explanation = RequireString(element, "explanation", path, errors, allowEmpty: false);
        var vulnerability = RequireBool(element, "vulnerability_mentioned", path, errors);
        var confidence = RequireEnum(element, "confidence", path, TriageContract.Confidences, errors);

        if (errors.Count > before)
        {
            return null;
        }

        return new TriageIssue(
            summary!,
            Enum.Parse<Category>(category!),
            Enum.Parse<Urgency>(urgency!),
            propertyText!,
            explanation!,
            vulnerability!.Value,
            Enum.Parse<TriageConfidence>(confidence!, ignoreCase: true));
    }

    private static string? RequireString(JsonElement parent, string name, string path, List<string> errors, bool allowEmpty)
    {
        var field = Field(path, name);
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            errors.Add($"{field}: is required.");
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{field}: must be a string.");
            return null;
        }

        var text = value.GetString()!;
        if (!allowEmpty && string.IsNullOrWhiteSpace(text))
        {
            errors.Add($"{field}: must not be empty.");
            return null;
        }

        return text;
    }

    private static string? RequireEnum(JsonElement parent, string name, string path, IReadOnlyList<string> allowed, List<string> errors)
    {
        var text = RequireString(parent, name, path, errors, allowEmpty: false);
        if (text is null)
        {
            return null;
        }

        if (!allowed.Contains(text, StringComparer.Ordinal))
        {
            errors.Add($"{Field(path, name)}: '{text}' is not one of {string.Join(", ", allowed)}.");
            return null;
        }

        return text;
    }

    private static bool? RequireBool(JsonElement parent, string name, string path, List<string> errors)
    {
        var field = Field(path, name);
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            errors.Add($"{field}: is required.");
            return null;
        }

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            errors.Add($"{field}: must be true or false.");
            return null;
        }

        return value.GetBoolean();
    }

    private static string Field(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    private static TriageValidationResult Invalid(string error) => new(null, new[] { error });
}
