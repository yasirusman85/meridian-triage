using System.Text.Json;
using Meridian.Application.Triage;
using Meridian.Infrastructure.Triage;

namespace Meridian.Tests.Infrastructure;

public sealed class StubTriageModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"meridian-stub-{Guid.NewGuid():N}");

    [Fact]
    public void The_six_forwarded_messages_have_recorded_answers_that_pass_the_validator()
    {
        var files = Directory.GetFiles(StubTriageModel.DefaultDirectory, "*.json");
        Assert.Equal(6, files.Length);
        Assert.Equal(6, StubTriageModel.FromDirectory(StubTriageModel.DefaultDirectory).Count);

        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var response = document.RootElement.GetProperty("response").GetRawText();
            var result = TriageValidator.Validate(response);
            Assert.True(result.IsValid, $"{Path.GetFileName(file)}: {string.Join("; ", result.Errors)}");
        }
    }

    [Fact]
    public async Task Matches_on_trimmed_text_and_reports_stub_and_triage_v1()
    {
        var stub = Write("""{"text":"Tap dripping","response":{"language":"en"}}""");

        var result = await stub.PrepareAsync(new TriageRequest("  Tap dripping\n", DateTime.UtcNow), default);

        Assert.True(result.Succeeded);
        Assert.Equal("""{"language":"en"}""", result.RawOutput);
        Assert.Equal("stub", result.ModelId);
        Assert.Equal("triage.v1", result.PromptVersion);
        Assert.Equal("stub", stub.ModelId);
        Assert.Equal("triage.v1", stub.PromptVersion);
    }

    [Fact]
    public async Task Unknown_text_is_a_no_recorded_response_failure()
    {
        var stub = Write("""{"text":"Tap dripping","response":{}}""");

        var result = await stub.PrepareAsync(new TriageRequest("Tap dripping badly", DateTime.UtcNow), default);

        Assert.False(result.Succeeded);
        Assert.Null(result.RawOutput);
        Assert.Equal(TriageFailureReasons.NoRecordedResponse, result.FailureReason);
    }

    [Fact]
    public async Task A_string_response_is_returned_as_is()
    {
        var stub = Write("""{"text":"Tap dripping","response":"{not json"}""");

        var result = await stub.PrepareAsync(new TriageRequest("Tap dripping", DateTime.UtcNow), default);

        Assert.Equal("{not json", result.RawOutput);
    }

    [Fact]
    public void A_recording_without_text_is_rejected_at_load()
    {
        Assert.Throws<InvalidDataException>(() => Write("""{"response":{}}"""));
    }

    [Fact]
    public void The_prompt_file_is_copied_and_lists_every_allowed_value()
    {
        var prompt = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Prompts", "triage.v1.md"));

        foreach (var value in TriageContract.Categories.Concat(TriageContract.Urgencies).Concat(TriageContract.Confidences))
        {
            Assert.Contains(value, prompt, StringComparison.Ordinal);
        }

        foreach (var field in new[] { "language", "issues", "summary_en", "category", "urgency", "property_text", "explanation", "vulnerability_mentioned", "confidence" })
        {
            Assert.Contains($"\"{field}\"", prompt, StringComparison.Ordinal);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private StubTriageModel Write(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "one.json"), json);
        return StubTriageModel.FromDirectory(_dir);
    }
}
