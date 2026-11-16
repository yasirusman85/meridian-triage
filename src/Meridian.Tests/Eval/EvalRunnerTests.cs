using System.Text.Json;
using Meridian.Eval;

namespace Meridian.Tests.Eval;

/// <summary>Runs the real runner with the stub (no network): the key reader always returns null.</summary>
public sealed class EvalRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-eval-test-{Guid.NewGuid():N}");
    private readonly string _cases;
    private readonly string _results;

    public EvalRunnerTests()
    {
        _cases = Path.Combine(_root, "cases");
        _results = Path.Combine(_root, "results");
        Directory.CreateDirectory(_cases);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Three_client_cases_run_through_the_pipeline_with_the_stub()
    {
        var repoCases = Path.Combine(
            EvalCommand.FindRepositoryRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Repository root not found."),
            "eval",
            "cases");
        foreach (var id in new[] { "real-01", "real-02", "real-03" })
        {
            File.Copy(Path.Combine(repoCases, id + ".json"), Path.Combine(_cases, id + ".json"));
        }

        var output = new StringWriter();
        var exit = await EvalCommand.RunAsync(
            new[] { "--cases", _cases, "--out", _results },
            output,
            new StringWriter(),
            openRouterApiKey: () => null,
            date: new DateOnly(2026, 11, 16));

        Assert.Equal(EvalCommand.Passed, exit);
        var json = Path.Combine(_results, "2026-11-16-stub-triage.v1.json");
        Assert.True(File.Exists(json));
        Assert.True(File.Exists(Path.Combine(_results, "2026-11-16-stub-triage.v1.md")));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(json));
        var root = document.RootElement;
        Assert.Equal("stub", root.GetProperty("modelId").GetString());
        var cases = root.GetProperty("cases").EnumerateArray().ToList();
        Assert.Equal(new[] { "real-01", "real-02", "real-03" }, cases.Select(c => c.GetProperty("id").GetString()));
        Assert.All(cases, c => Assert.True(c.GetProperty("correct").GetBoolean()));

        // real-01 is split into two jobs; real-03 attaches to real-02's job and makes none.
        Assert.Equal(2, cases[0].GetProperty("actual").GetProperty("newJobs").GetArrayLength());
        var real02Job = cases[1].GetProperty("actual").GetProperty("newJobs")[0].GetString();
        var real03 = cases[2].GetProperty("actual");
        Assert.Equal(0, real03.GetProperty("newJobs").GetArrayLength());
        Assert.Equal(real02Job, real03.GetProperty("attachedTo")[0].GetString());
    }

    [Fact]
    public async Task A_case_without_a_recorded_answer_falls_back_to_unsure_and_a_missed_emergency_exits_1()
    {
        await File.WriteAllTextAsync(Path.Combine(_cases, "x-01.json"), """
            {"id": "x-01", "source": "written", "received_at": "2026-07-10T10:00:00Z", "sender": "t@example.com",
             "text": "My socket is sparking and smoking, 27 Ashby Grove.",
             "expected": {"is_maintenance": true,
                          "issues": [{"category": "Electrical", "urgency": "Emergency", "vulnerability_mentioned": false, "property_text": "27 Ashby Grove"}],
                          "follow_up_of": null},
             "harm_if_wrong": "critical"}
            """);

        var exit = await EvalCommand.RunAsync(
            new[] { "--cases", _cases, "--out", _results },
            new StringWriter(),
            new StringWriter(),
            openRouterApiKey: () => null,
            date: new DateOnly(2026, 11, 16));

        Assert.Equal(EvalCommand.Failed, exit);
        var markdown = await File.ReadAllTextAsync(Path.Combine(_results, "2026-11-16-stub-triage.v1.md"));
        Assert.Contains("| x-01 | urgency | Emergency | Routine |", markdown);
        Assert.Contains("### Critical (1)", markdown);
        Assert.Contains("`no-recorded-response` 1", markdown);
    }

    [Fact]
    public async Task A_case_file_missing_expected_stops_the_run_with_its_name_and_exit_2()
    {
        await File.WriteAllTextAsync(Path.Combine(_cases, "broken.json"), """
            {"id": "broken", "received_at": "2026-01-16T19:00:00Z", "sender": "t@example.com", "text": "Hello"}
            """);
        var error = new StringWriter();

        var exit = await EvalCommand.RunAsync(
            new[] { "--cases", _cases, "--out", _results },
            new StringWriter(),
            error,
            openRouterApiKey: () => null);

        Assert.Equal(EvalCommand.BadInput, exit);
        Assert.Contains("broken.json", error.ToString());
        Assert.Contains("expected", error.ToString());
        Assert.False(Directory.Exists(_results));
    }

    [Fact]
    public void Loader_rejects_a_follow_up_of_a_case_that_runs_later()
    {
        File.WriteAllText(Path.Combine(_cases, "a.json"), CaseJson("a", followUpOf: "b"));
        File.WriteAllText(Path.Combine(_cases, "b.json"), CaseJson("b", followUpOf: null));

        var ex = Assert.Throws<EvalCaseException>(() => EvalCaseLoader.LoadDirectory(_cases));

        Assert.Equal("a.json", ex.FileName);
    }

    private static string CaseJson(string id, string? followUpOf)
    {
        var followUp = followUpOf is null ? "null" : $"\"{followUpOf}\"";
        return $$$"""
            {"id": "{{{id}}}", "received_at": "2026-01-16T19:00:00Z", "sender": "t@example.com", "text": "Tap drips",
             "expected": {"is_maintenance": true,
                          "issues": [{"category": "Plumbing", "urgency": "Routine", "vulnerability_mentioned": false, "property_text": null}],
                          "follow_up_of": {{{followUp}}}}}
            """;
    }
}
