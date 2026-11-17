using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Meridian.Tests.Api;

/// <summary>The numbers endpoint through the real database: empty, then one submitted message.</summary>
public sealed class MetricsApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task With_no_jobs_every_measure_is_no_data()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var metrics = await response.Content.ReadFromJsonAsync<JsonElement>();
        var allTime = metrics.GetProperty("allTime");
        Assert.Equal(0, allTime.GetProperty("jobCount").GetInt32());
        Assert.Equal(0, allTime.GetProperty("firstHumanLook").GetProperty("jobs").GetInt32());
        Assert.Equal(JsonValueKind.Null, allTime.GetProperty("firstHumanLook").GetProperty("medianMinutes").ValueKind);
        Assert.Equal(0, allTime.GetProperty("emergency24Hours").GetProperty("eligible").GetInt32());
        Assert.Equal(0, metrics.GetProperty("last30Days").GetProperty("jobCount").GetInt32());
    }

    [Fact]
    public async Task A_submitted_message_is_counted_in_both_windows_by_category_and_status()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var posted = await client.PostAsJsonAsync("/api/messages", new { sender = "a.nowak@example.com", text = "Mould on the bedroom wall, child has asthma." });
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);

        var metrics = await client.GetFromJsonAsync<JsonElement>("/api/metrics");

        foreach (var window in new[] { metrics.GetProperty("last30Days"), metrics.GetProperty("allTime") })
        {
            Assert.Equal(1, window.GetProperty("jobCount").GetInt32());
            var categories = window.GetProperty("byCategory").EnumerateArray().ToList();
            Assert.Equal(8, categories.Count);
            Assert.Equal(1, categories.Sum(c => c.GetProperty("count").GetInt32()));
            var prepared = window.GetProperty("openByStatus").EnumerateArray().Single(s => s.GetProperty("name").GetString() == "Prepared");
            Assert.Equal(1, prepared.GetProperty("count").GetInt32());
        }
    }

    [Fact]
    public async Task Rachels_first_look_at_a_submitted_job_shows_a_value_for_time_to_first_human_look()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var posted = await client.PostAsJsonAsync("/api/messages", new { sender = "a.nowak@example.com", text = "Mould on the bedroom wall, child has asthma." });
        var jobId = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobs")[0].GetProperty("jobId").GetGuid();

        using var edit = new HttpRequestMessage(HttpMethod.Patch, $"/api/jobs/{jobId}")
        {
            Content = JsonContent.Create(new { category = "Heating" }),
        };
        edit.Headers.Add("X-Acting-As", "rachel");
        var edited = await client.SendAsync(edit);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        var metrics = await client.GetFromJsonAsync<JsonElement>("/api/metrics");
        var firstLook = metrics.GetProperty("allTime").GetProperty("firstHumanLook");
        Assert.Equal(1, firstLook.GetProperty("jobs").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, firstLook.GetProperty("medianMinutes").ValueKind);
        var byCategory = metrics.GetProperty("allTime").GetProperty("byCategory").EnumerateArray();
        Assert.Equal(1, byCategory.Single(c => c.GetProperty("name").GetString() == "Heating").GetProperty("count").GetInt32());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Meridian", $"Data Source={_dbPath}");
            // Never call a real model, even when a developer has OPENROUTER_API_KEY set.
            builder.UseSetting("Triage:Provider", "stub");
            builder.UseSetting("Demo:SeedClientMessages", "false");
        });
}
