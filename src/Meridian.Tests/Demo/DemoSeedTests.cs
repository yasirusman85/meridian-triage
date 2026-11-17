using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Application.Templates;
using Meridian.Infrastructure.Persistence;
using Meridian.Web.Demo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Tests.Demo;

/// <summary>The demo seed of the six client messages (<c>Demo:SeedClientMessages</c>) and the received times it gives them.</summary>
public sealed class DemoSeedTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"meridian-demo-{Guid.NewGuid():N}.db");

    [Fact]
    public void A_message_is_received_at_the_most_recent_past_weekday_and_time_in_London()
    {
        // Tuesday 17 November 2026, 09:00 UTC (GMT in London): Tuesday 10:42 has not come round yet today.
        var now = new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);
        var tuesday = new DemoClientMessages.DemoMessage("j@example.com", "t", DayOfWeek.Tuesday, new TimeOnly(10, 42));
        Assert.Equal(new DateTime(2026, 11, 10, 10, 42, 0, DateTimeKind.Utc), DemoSeed.ReceivedAtUtc(tuesday, now));

        // Once its time has passed today, today counts.
        Assert.Equal(new DateTime(2026, 11, 17, 10, 42, 0, DateTimeKind.Utc), DemoSeed.ReceivedAtUtc(tuesday, new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc)));

        // Friday evening in winter is GMT, so local and UTC agree and the message is in the winter window.
        var friday = new DemoClientMessages.DemoMessage("m@example.com", "t", DayOfWeek.Friday, new TimeOnly(19, 15));
        Assert.Equal(new DateTime(2026, 11, 13, 19, 15, 0, DateTimeKind.Utc), DemoSeed.ReceivedAtUtc(friday, now));

        // A summer time is BST (UTC+1): Monday 14:20 London on 6 July 2026 is 13:20 UTC.
        var summer = new DemoClientMessages.DemoMessage("r@example.com", "t", DayOfWeek.Monday, new TimeOnly(14, 20));
        Assert.Equal(
            new DateTime(2026, 7, 6, 13, 20, 0, DateTimeKind.Utc),
            DemoSeed.ReceivedAtUtc(summer, new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void The_six_messages_are_in_their_original_order_with_their_masked_senders()
    {
        var messages = DemoClientMessages.All;
        Assert.Equal(6, messages.Count);
        Assert.Equal(
            new[] { "j.okafor@example.com", "m.walsh@example.com", "m.walsh@example.com", "s.walsh@example.com", "a.nowak@example.com", "r.patel@example.com" },
            messages.Select(m => m.Sender));
        Assert.Equal(DayOfWeek.Friday, messages[1].Day);
        Assert.Equal(new TimeOnly(19, 15), messages[1].LocalTime);
    }

    [Fact]
    public async Task A_fresh_database_with_the_demo_on_queues_five_prepared_jobs_from_six_messages()
    {
        using var factory = Factory(demo: true);
        using var client = factory.CreateClient();

        var jobs = (await client.GetFromJsonAsync<JsonElement>("/api/jobs")).EnumerateArray().ToList();

        Assert.Equal(5, jobs.Count);
        Assert.All(jobs, j => Assert.Equal("Prepared", j.GetProperty("status").GetString()));
        Assert.Single(jobs, j => j.GetProperty("followUpCount").GetInt32() == 2);
        await using var db = Database();
        Assert.Equal(6, await db.Messages.CountAsync());
        Assert.Equal(6, await db.OutboundMessages.CountAsync(o => o.TemplateId == TenantTemplates.AcknowledgementId));
    }

    [Fact]
    public async Task A_second_start_seeds_nothing_again()
    {
        using (var first = Factory(demo: true))
        using (var client = first.CreateClient())
        {
            await client.GetAsync("/api/jobs");
        }

        using var second = Factory(demo: true);
        using var secondClient = second.CreateClient();

        Assert.Equal(5, (await secondClient.GetFromJsonAsync<JsonElement>("/api/jobs")).GetArrayLength());
        await using var db = Database();
        Assert.Equal(6, await db.Messages.CountAsync());
    }

    [Fact]
    public async Task With_the_demo_off_the_queue_starts_empty()
    {
        using var factory = Factory(demo: false);
        using var client = factory.CreateClient();

        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/jobs")).GetArrayLength());
    }

    [Fact]
    public async Task Seeded_jobs_are_approved_and_sent_to_a_contractor_with_the_contractor_audience()
    {
        using var factory = Factory(demo: true);
        using var client = factory.CreateClient();
        var joy = (await client.GetFromJsonAsync<JsonElement>("/api/jobs")).EnumerateArray()
            .First(j => j.GetProperty("sender").GetString() == "j.okafor@example.com");
        var jobId = joy.GetProperty("id").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/jobs/{jobId}/approve");
        request.Headers.Add("X-Acting-As", "rachel");
        var approved = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var outbound = (await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}/outbound")).EnumerateArray().ToList();
        Assert.Equal("tenant", outbound.Single(o => o.GetProperty("templateId").GetString() == TenantTemplates.AcknowledgementId).GetProperty("audience").GetString());
        Assert.Equal("contractor", outbound.Single(o => o.GetProperty("templateId").GetString() == ContractorTemplates.JobId).GetProperty("audience").GetString());
    }

    private WebApplicationFactory<Program> Factory(bool demo) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Meridian", $"Data Source={_dbPath}");
            builder.UseSetting("Triage:Provider", "stub");
            builder.UseSetting(DemoSeed.ConfigurationKey, demo ? "true" : "false");
        });

    private MeridianDbContext Database() =>
        new(new DbContextOptionsBuilder<MeridianDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

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
}
