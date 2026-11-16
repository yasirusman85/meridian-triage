using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Domain.Jobs;

namespace Meridian.Eval;

/// <summary>One expected issue of a case (see <c>eval/README.md</c>).</summary>
public sealed record ExpectedIssue(Category Category, Urgency Urgency, bool VulnerabilityMentioned, string? PropertyText);

/// <summary>One labelled case from <c>eval/cases/*.json</c>.</summary>
public sealed record EvalCase(
    string Id,
    string FileName,
    DateTime ReceivedAtUtc,
    string Sender,
    string Text,
    bool IsMaintenance,
    IReadOnlyList<ExpectedIssue> Issues,
    string? FollowUpOf,
    string? HarmIfWrong);

/// <summary>A case file that cannot be used. The run stops with the file name (exit 2).</summary>
public sealed class EvalCaseException : Exception
{
    public EvalCaseException(string fileName, string problem)
        : base($"Case file {fileName}: {problem}")
    {
        FileName = fileName;
    }

    public string FileName { get; }
}

/// <summary>Reads and checks every case file; returns the cases in id order (ordinal).</summary>
public static class EvalCaseLoader
{
    public static IReadOnlyList<EvalCase> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Cases directory not found: {directory}");
        }

        var cases = Directory.EnumerateFiles(directory, "*.json")
            .Select(path => Parse(Path.GetFileName(path), File.ReadAllText(path, Encoding.UTF8)))
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

        var seen = new Dictionary<string, EvalCase>(StringComparer.Ordinal);
        foreach (var evalCase in cases)
        {
            if (!seen.TryAdd(evalCase.Id, evalCase))
            {
                throw new EvalCaseException(evalCase.FileName, $"duplicate id '{evalCase.Id}' (also in {seen[evalCase.Id].FileName}).");
            }

            if (evalCase.FollowUpOf is not null && !seen.ContainsKey(evalCase.FollowUpOf) && evalCase.FollowUpOf != evalCase.Id)
            {
                // Only an earlier case (in id order) has run when this one runs.
                var known = cases.Any(c => c.Id == evalCase.FollowUpOf);
                throw new EvalCaseException(
                    evalCase.FileName,
                    known
                        ? $"follow_up_of '{evalCase.FollowUpOf}' sorts after this case, so it would not have run first."
                        : $"follow_up_of '{evalCase.FollowUpOf}' names no case.");
            }
        }

        return cases;
    }

    public static EvalCase Parse(string fileName, string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new EvalCaseException(fileName, $"not valid JSON ({ex.Message}).");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new EvalCaseException(fileName, "must be a JSON object.");
            }

            var id = RequireString(root, "id", fileName);
            var receivedText = RequireString(root, "received_at", fileName);
            if (!DateTime.TryParse(receivedText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var received))
            {
                throw new EvalCaseException(fileName, $"received_at '{receivedText}' is not an ISO-8601 time.");
            }

            var sender = RequireString(root, "sender", fileName);
            var text = RequireString(root, "text", fileName);
            if (!root.TryGetProperty("expected", out var expected) || expected.ValueKind != JsonValueKind.Object)
            {
                throw new EvalCaseException(fileName, "missing 'expected'.");
            }

            if (!expected.TryGetProperty("is_maintenance", out var isMaintenance)
                || isMaintenance.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new EvalCaseException(fileName, "expected.is_maintenance must be true or false.");
            }

            if (!expected.TryGetProperty("issues", out var issuesElement) || issuesElement.ValueKind != JsonValueKind.Array)
            {
                throw new EvalCaseException(fileName, "expected.issues must be an array.");
            }

            var issues = new List<ExpectedIssue>();
            var index = 0;
            foreach (var issue in issuesElement.EnumerateArray())
            {
                issues.Add(ParseIssue(issue, $"expected.issues[{index}]", fileName));
                index++;
            }

            if (isMaintenance.GetBoolean() && issues.Count == 0)
            {
                throw new EvalCaseException(fileName, "a maintenance case needs at least one expected issue.");
            }

            if (!isMaintenance.GetBoolean() && issues.Count > 0)
            {
                throw new EvalCaseException(fileName, "a non-maintenance case must have no expected issues.");
            }

            var followUpOf = OptionalString(expected, "follow_up_of", fileName);
            var harm = root.TryGetProperty("harm_if_wrong", out var harmElement) && harmElement.ValueKind == JsonValueKind.String
                ? harmElement.GetString()
                : null;

            return new EvalCase(id, fileName, received, sender, text, isMaintenance.GetBoolean(), issues, followUpOf, harm);
        }
    }

    private static ExpectedIssue ParseIssue(JsonElement issue, string path, string fileName)
    {
        if (issue.ValueKind != JsonValueKind.Object)
        {
            throw new EvalCaseException(fileName, $"{path} must be an object.");
        }

        var categoryText = RequireString(issue, "category", fileName, path);
        if (!Enum.TryParse<Category>(categoryText, ignoreCase: false, out var category) || !Enum.IsDefined(category))
        {
            throw new EvalCaseException(fileName, $"{path}.category '{categoryText}' is not one of the eight categories.");
        }

        var urgencyText = RequireString(issue, "urgency", fileName, path);
        if (!Enum.TryParse<Urgency>(urgencyText, ignoreCase: false, out var urgency) || !Enum.IsDefined(urgency))
        {
            throw new EvalCaseException(fileName, $"{path}.urgency '{urgencyText}' is not Routine, Urgent or Emergency.");
        }

        if (!issue.TryGetProperty("vulnerability_mentioned", out var vulnerability)
            || vulnerability.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new EvalCaseException(fileName, $"{path}.vulnerability_mentioned must be true or false.");
        }

        var property = OptionalString(issue, "property_text", fileName, path);
        return new ExpectedIssue(category, urgency, vulnerability.GetBoolean(), property);
    }

    private static string RequireString(JsonElement element, string name, string fileName, string? path = null)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new EvalCaseException(fileName, $"missing '{Qualified(path, name)}'.");
        }

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string name, string fileName, string? path = null)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw new EvalCaseException(fileName, $"missing '{Qualified(path, name)}' (use null when there is none).");
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => throw new EvalCaseException(fileName, $"'{Qualified(path, name)}' must be a string or null."),
        };
    }

    private static string Qualified(string? path, string name) => path is null ? name : $"{path}.{name}";
}
