using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Application.Abstractions;
using Meridian.Domain.Jobs;
using Meridian.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Api;

public sealed class MessagesApiTests : IDisposable
{
    // The Nowak message from the client input, in Polish with diacritics.
    private const string NowakText =
        "Dzień dobry, w mieszkaniu jest pleśń na ścianie w sypialni, coraz gorzej, dziecko ma astmę. Mieszkanie 9, Beechwood House. Dziękuję.";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Posting_a_Polish_message_stores_it_unchanged_with_its_MessageReceived_event()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/messages", new { sender = "a.nowak@example.com", text = NowakText });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var reference = created.GetProperty("reference").GetString();
        Assert.Matches("^MER-\\d{6}$", reference);
        var jobId = created.GetProperty("jobId").GetGuid();

        var jobs = await client.GetFromJsonAsync<JsonElement>("/api/jobs");
        var listed = Assert.Single(jobs.EnumerateArray());
        Assert.Equal(reference, listed.GetProperty("reference").GetString());
        Assert.Equal("Prepared", listed.GetProperty("status").GetString());
        // Prepared by the recorded stub answer for the Nowak message.
        Assert.Equal("Unsure", listed.GetProperty("category").GetString());
        Assert.Equal("Urgent", listed.GetProperty("urgency").GetString());
        Assert.False(string.IsNullOrWhiteSpace(listed.GetProperty("summaryEn").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(listed.GetProperty("explanation").GetString()));
        Assert.Equal(NowakText[..120], listed.GetProperty("preview").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var message = await db.Messages.SingleAsync();
        Assert.Equal(NowakText, message.Text);
        Assert.Equal(jobId, message.JobId);

        var events = await db.JobEvents.Where(e => e.JobId == jobId).ToListAsync();
        var received = Assert.Single(events, e => e.Type == EventType.MessageReceived);
        Assert.Equal("system", received.Actor);
        Assert.Equal(DateTimeKind.Utc, received.OccurredAtUtc.Kind);
        Assert.Contains(events, e => e.Type == EventType.JobPrepared);

        // Stored values are strings, and the raw text column holds the exact UTF-8 bytes.
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT m.Text, j.Status, e.Type FROM Messages m JOIN Jobs j ON j.Id = m.JobId JOIN JobEvents e ON e.JobId = j.Id WHERE e.Type = 'MessageReceived'";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(NowakText, reader.GetString(0));
        Assert.Equal("Prepared", reader.GetString(1));
        Assert.Equal("MessageReceived", reader.GetString(2));
    }

    [Theory]
    [InlineData(NowakText, "ModelPrepared")]
    [InlineData("A message with no recorded answer", "ModelPreparationFailed")]
    public async Task Exactly_one_model_outcome_event_follows_JobPrepared_with_stub_metadata(string text, string expectedType)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/messages", new { sender = "tenant@example.com", text });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var jobId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var events = (await db.JobEvents.Where(e => e.JobId == jobId).ToListAsync())
            .OrderBy(e => e.OccurredAtUtc)
            .ToList();
        var outcomes = events.Where(e => e.Type is EventType.ModelPrepared or EventType.ModelPreparationFailed).ToList();
        var outcome = Assert.Single(outcomes);
        Assert.Equal(expectedType, outcome.Type.ToString());
        Assert.True(events.FindIndex(e => e.Type == EventType.JobPrepared) < events.IndexOf(outcome));
        using var payload = JsonDocument.Parse(outcome.PayloadJson);
        Assert.Equal("stub", payload.RootElement.GetProperty("modelId").GetString());
        Assert.Equal("triage.v1", payload.RootElement.GetProperty("promptVersion").GetString());

        var job = await db.Jobs.SingleAsync(j => j.Id == jobId);
        if (outcome.Type == EventType.ModelPreparationFailed)
        {
            Assert.Equal(Category.Unsure, job.Category);
            Assert.Equal(Urgency.Routine, job.Urgency);
            Assert.Null(job.SummaryEn);
            Assert.Equal("no-recorded-response", payload.RootElement.GetProperty("reason").GetString());
        }
        else
        {
            Assert.Equal("pl", job.Language);
            Assert.Contains(events, e => e.Type == EventType.UrgencyRaised);
        }
    }

    [Fact]
    public async Task The_Walsh_message_received_on_a_Friday_in_January_is_Emergency_with_its_rule_in_the_queue()
    {
        const string walshText =
            "URGENT URGENT no hot water and no heating at all since this morning, I have a baby in the house, this is unacceptable, I have called three times. 14b Alder Court. Please someone call me TONIGHT.";
        var clock = new TickingClock(new DateTime(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc));
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/messages", new { sender = "m.walsh@example.com", text = walshText });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var jobId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();
        var listed = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/jobs")).EnumerateArray());
        Assert.Equal("Emergency", listed.GetProperty("urgency").GetString());
        Assert.Equal(
            new[] { "no-heating-winter" },
            listed.GetProperty("ruleIds").EnumerateArray().Select(r => r.GetString()));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var events = (await db.JobEvents.Where(e => e.JobId == jobId).ToListAsync())
            .OrderBy(e => e.OccurredAtUtc)
            .ToList();
        var rules = events.FindIndex(e => e.Type == EventType.RulesApplied);
        var outcome = events.FindIndex(e => e.Type is EventType.ModelPrepared or EventType.ModelPreparationFailed);
        Assert.True(rules >= 0 && outcome > rules, string.Join(", ", events.Select(e => e.Type)));
        using (var applied = JsonDocument.Parse(events[rules].PayloadJson))
        {
            var rule = Assert.Single(applied.RootElement.GetProperty("rules").EnumerateArray());
            Assert.Equal("no-heating-winter", rule.GetProperty("id").GetString());
            Assert.Equal("no hot water", rule.GetProperty("matchedPhrase").GetString());
        }

        var job = await db.Jobs.SingleAsync(j => j.Id == jobId);
        Assert.Equal(new[] { "no-heating-winter" }, job.RuleIds);
    }

    [Fact]
    public async Task A_job_with_no_rule_hit_lists_an_empty_rule_list()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/messages", new { sender = "tenant@example.com", text = "Tap dripping" });

        var listed = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/jobs")).EnumerateArray());
        Assert.Empty(listed.GetProperty("ruleIds").EnumerateArray());
    }

    [Theory]
    [InlineData("a.nowak@example.com", "")]
    [InlineData("a.nowak@example.com", "   \n ")]
    [InlineData("", "Boiler broken")]
    [InlineData("  ", "Boiler broken")]
    public async Task Blank_sender_or_text_is_400_problem_details_and_stores_nothing(string sender, string text)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/messages", new { sender, text });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var jobs = await client.GetFromJsonAsync<JsonElement>("/api/jobs");
        Assert.Empty(jobs.EnumerateArray());
    }

    [Fact]
    public async Task Malformed_json_is_400_problem_details()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var content = new StringContent("not json", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/messages", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_20000_character_message_is_stored_unchanged()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var text = string.Concat(Enumerable.Repeat("Zażółć gęślą jaźń. ", 20_000 / 19 + 1))[..20_000];

        var response = await client.PostAsJsonAsync("/api/messages", new { sender = "tenant@example.com", text });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        Assert.Equal(text, (await db.Messages.SingleAsync()).Text);
    }

    [Fact]
    public async Task Jobs_are_still_listed_after_a_restart_and_references_keep_counting()
    {
        using (var first = CreateFactory())
        using (var client = first.CreateClient())
        {
            var response = await client.PostAsJsonAsync("/api/messages", new { sender = "tenant@example.com", text = "Hallway bulb out" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        using var second = CreateFactory();
        using var client2 = second.CreateClient();
        var jobs = await client2.GetFromJsonAsync<JsonElement>("/api/jobs");
        Assert.Equal("MER-000001", Assert.Single(jobs.EnumerateArray()).GetProperty("reference").GetString());

        var next = await client2.PostAsJsonAsync("/api/messages", new { sender = "tenant@example.com", text = "Tap dripping" });
        var created = await next.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MER-000002", created.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task Unknown_api_route_is_404_not_the_spa()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    private WebApplicationFactory<Program> CreateFactory(IClock? clock = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Meridian", $"Data Source={_dbPath}");
            // Never call a real model, even when a developer has OPENROUTER_API_KEY set.
            builder.UseSetting("Triage:Provider", "stub");
            if (clock is not null)
            {
                builder.ConfigureTestServices(services => services.AddSingleton(clock));
            }
        });

    /// <summary>A fixed start time that moves on a millisecond per read, so events of different saves sort apart.</summary>
    private sealed class TickingClock : IClock
    {
        private DateTime _next;

        public TickingClock(DateTime start) => _next = start;

        public DateTime UtcNow
        {
            get
            {
                lock (this)
                {
                    var now = _next;
                    _next = _next.AddMilliseconds(1);
                    return now;
                }
            }
        }
    }
}
