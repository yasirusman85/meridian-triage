using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Domain.Jobs;
using Meridian.Infrastructure.Persistence;
using Meridian.Infrastructure.Triage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Infrastructure;

/// <summary>
/// Runs only when <c>OPENROUTER_LIVE=1</c> and <c>OPENROUTER_API_KEY</c> are set; skipped in the default test run,
/// so <c>dotnet test</c> never uses the network.
/// </summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OPENROUTER_LIVE") != "1" || TriageOptions.ReadApiKey() is null)
        {
            Skip = "Live OpenRouter test: set OPENROUTER_LIVE=1 and OPENROUTER_API_KEY to run.";
        }
    }
}

[Trait("Category", "Live")]
public sealed class OpenRouterLiveTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"meridian-live-{Guid.NewGuid():N}.db");

    [LiveFact]
    public async Task One_Walsh_message_is_prepared_by_OpenRouter_with_model_id_and_token_counts()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Meridian", $"Data Source={_dbPath}");
            builder.UseSetting("Demo:SeedClientMessages", "false");
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/messages", new { sender = "d.walsh@example.com", text = OpenRouterTriageModelTests.WalshText });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var jobId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var events = await db.JobEvents.Where(e => e.JobId == jobId).ToListAsync();
        var outcome = Assert.Single(events, e => e.Type is EventType.ModelPrepared or EventType.ModelPreparationFailed);
        Assert.True(outcome.Type == EventType.ModelPrepared, outcome.PayloadJson);

        using var payload = JsonDocument.Parse(outcome.PayloadJson);
        Assert.Equal(TriageOptions.DefaultModel, payload.RootElement.GetProperty("modelId").GetString());
        Assert.Equal("triage.v1", payload.RootElement.GetProperty("promptVersion").GetString());
        var attempt = payload.RootElement.GetProperty("attempts").EnumerateArray().Last();
        Assert.True(attempt.GetProperty("inputTokens").GetInt32() > 0);
        Assert.True(attempt.GetProperty("outputTokens").GetInt32() > 0);
    }

    public void Dispose()
    {
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
