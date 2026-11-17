using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Application.Abstractions;
using Meridian.Domain.Jobs;
using Meridian.Infrastructure.Persistence;
using Meridian.Web.Workers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Api;

/// <summary>Escalation through the real database, worker tick, acknowledge endpoint and notifications list.</summary>
public sealed class EscalationApiTests : IDisposable
{
    private const string WalshText =
        "URGENT URGENT no hot water and no heating at all since this morning, I have a baby in the house, this is unacceptable, I have called three times. 14b Alder Court. Please someone call me TONIGHT.";

    private static readonly DateTime Received = new(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"meridian-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task The_Walsh_emergency_with_no_acknowledgement_escalates_to_Rachel_then_Dawn_then_fails_at_the_top_of_the_queue()
    {
        var clock = new FakeClock(Received);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        var walshId = await PostAsync(client, "m.walsh@example.com", WalshText);
        await PostAsync(client, "tenant@example.com", "Tap dripping");
        var worker = Worker(factory);

        await worker.RunOnceAsync(default);
        await worker.RunOnceAsync(default);
        var notifications = (await client.GetFromJsonAsync<JsonElement>("/api/notifications")).EnumerateArray().ToList();
        var rachel = Assert.Single(notifications);
        Assert.Equal("sms", rachel.GetProperty("channel").GetString());
        Assert.Equal("Rachel (07700 900461)", rachel.GetProperty("recipient").GetString());
        Assert.Equal("Simulated", rachel.GetProperty("status").GetString());
        Assert.Equal("escalation-primary", rachel.GetProperty("templateId").GetString());
        Assert.Equal("MER-000001", rachel.GetProperty("jobReference").GetString());
        Assert.Equal(
            "EMERGENCY MER-000001: Heating at 14b Alder Court. Rules: no-heating-winter. Please open the Meridian queue and acknowledge it.",
            rachel.GetProperty("body").GetString());
        Assert.Equal("NotifiedRachel", (await JobAsync(client, walshId)).GetProperty("escalationState").GetString());

        clock.UtcNow = Received.AddMinutes(30);
        await worker.RunOnceAsync(default);
        notifications = (await client.GetFromJsonAsync<JsonElement>("/api/notifications")).EnumerateArray().ToList();
        Assert.Equal(2, notifications.Count);
        Assert.Equal("Dawn (07700 900872)", notifications[0].GetProperty("recipient").GetString());
        Assert.Equal("escalation-backup", notifications[0].GetProperty("templateId").GetString());

        clock.UtcNow = Received.AddMinutes(60);
        await worker.RunOnceAsync(default);
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement>("/api/notifications")).GetArrayLength());

        var queue = (await client.GetFromJsonAsync<JsonElement>("/api/jobs")).EnumerateArray().ToList();
        Assert.Equal(walshId, queue[0].GetProperty("id").GetGuid());
        Assert.Equal("Failed", queue[0].GetProperty("escalationState").GetString());
        Assert.Equal("None", queue[1].GetProperty("escalationState").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var job = await db.Jobs.Include(j => j.Events).SingleAsync(j => j.Id == walshId);
        Assert.Equal(
            new[] { EventType.EscalationNotified, EventType.EscalationNotified, EventType.EscalationFailed },
            job.Events.Where(e => e.Type.ToString().StartsWith("Escalation", StringComparison.Ordinal))
                .OrderBy(e => e.OccurredAtUtc)
                .Select(e => e.Type));
        Assert.Equal(Received, job.EscalationPrimaryNotifiedAtUtc);
        Assert.Equal(DateTimeKind.Utc, job.EscalationPrimaryNotifiedAtUtc!.Value.Kind);
        Assert.Equal(Received.AddMinutes(30), job.EscalationBackupNotifiedAtUtc);
        Assert.Equal(Received.AddMinutes(60), job.EscalationFailedAtUtc);
        // One tenant acknowledgement and two staff notifications are filed under the job.
        Assert.Equal(3, await db.OutboundMessages.CountAsync(o => o.JobId == walshId));
    }

    [Fact]
    public async Task Rachel_acknowledges_from_the_queue_and_Dawn_is_never_messaged()
    {
        var clock = new FakeClock(Received);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        var walshId = await PostAsync(client, "m.walsh@example.com", WalshText);
        var worker = Worker(factory);
        await worker.RunOnceAsync(default);

        clock.UtcNow = Received.AddMinutes(12);
        var response = await AcknowledgeAsync(client, walshId, "rachel");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Acknowledged", body.GetProperty("escalationState").GetString());
        Assert.Equal("rachel", body.GetProperty("acknowledgedBy").GetString());
        Assert.EndsWith("Z", body.GetProperty("acknowledgedAtUtc").GetString());

        clock.UtcNow = Received.AddMinutes(90);
        await worker.RunOnceAsync(default);
        var notifications = (await client.GetFromJsonAsync<JsonElement>("/api/notifications")).EnumerateArray().ToList();
        Assert.Equal("Rachel (07700 900461)", Assert.Single(notifications).GetProperty("recipient").GetString());
        var listed = await JobAsync(client, walshId);
        Assert.Equal("Acknowledged", listed.GetProperty("escalationState").GetString());
        Assert.Equal("rachel", listed.GetProperty("escalationAcknowledgedBy").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var ack = await db.JobEvents.SingleAsync(e => e.JobId == walshId && e.Type == EventType.EscalationAcknowledged);
        Assert.Equal("rachel", ack.Actor);

        // A second acknowledgement is a conflict.
        Assert.Equal(HttpStatusCode.Conflict, (await AcknowledgeAsync(client, walshId, "dawn")).StatusCode);
    }

    [Fact]
    public async Task Acknowledging_a_Routine_job_is_409_and_nothing_changes()
    {
        var clock = new FakeClock(Received);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "tenant@example.com", "Tap dripping");
        int events;
        using (var scope = factory.Services.CreateScope())
        {
            events = await scope.ServiceProvider.GetRequiredService<MeridianDbContext>().JobEvents.CountAsync(e => e.JobId == jobId);
        }

        var response = await AcknowledgeAsync(client, jobId, "rachel");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("None", (await JobAsync(client, jobId)).GetProperty("escalationState").GetString());
        using (var scope = factory.Services.CreateScope())
        {
            Assert.Equal(events, await scope.ServiceProvider.GetRequiredService<MeridianDbContext>().JobEvents.CountAsync(e => e.JobId == jobId));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mallory")]
    public async Task Acknowledging_without_a_known_acting_person_is_400(string? actingAs)
    {
        using var factory = CreateFactory(new FakeClock(Received));
        using var client = factory.CreateClient();
        var walshId = await PostAsync(client, "m.walsh@example.com", WalshText);

        var response = await AcknowledgeAsync(client, walshId, actingAs);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Acknowledging_an_unknown_job_is_404()
    {
        using var factory = CreateFactory(new FakeClock(Received));
        using var client = factory.CreateClient();

        var response = await AcknowledgeAsync(client, Guid.NewGuid(), "rachel");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_worker_also_recovers_orphan_messages_older_than_the_default_minimum_age()
    {
        var clock = new FakeClock(Received);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
            db.Messages.Add(Meridian.Domain.Messages.Message.Receive("tenant@example.com", "Hallway bulb out", Received));
            await db.SaveChangesAsync();
        }

        var worker = Worker(factory);
        clock.UtcNow = Received.AddMinutes(1);
        await worker.RunOnceAsync(default);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/jobs")).GetArrayLength());

        clock.UtcNow = Received.AddMinutes(3);
        await worker.RunOnceAsync(default);
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/jobs")).GetArrayLength());
    }

    [Fact]
    public async Task Escalation_staff_and_window_come_from_configuration()
    {
        var clock = new FakeClock(Received);
        using var factory = CreateFactory(clock, builder =>
        {
            builder.UseSetting("Escalation:WindowMinutes", "10");
            builder.UseSetting("Escalation:Backup:Name", "Dee");
            builder.UseSetting("Escalation:Backup:Phone", "07700 900999");
        });
        using var client = factory.CreateClient();
        await PostAsync(client, "m.walsh@example.com", WalshText);
        var worker = Worker(factory);
        await worker.RunOnceAsync(default);

        clock.UtcNow = Received.AddMinutes(10);
        await worker.RunOnceAsync(default);

        var latest = (await client.GetFromJsonAsync<JsonElement>("/api/notifications")).EnumerateArray().First();
        Assert.Equal("Dee (07700 900999)", latest.GetProperty("recipient").GetString());
    }

    private static EscalationWorker Worker(WebApplicationFactory<Program> factory) =>
        new(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<EscalationWorker>.Instance);

    private static async Task<Guid> PostAsync(HttpClient client, string sender, string text)
    {
        var response = await client.PostAsJsonAsync("/api/messages", new { sender, text });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("jobs")[0].GetProperty("jobId").GetGuid();
    }

    private static async Task<JsonElement> JobAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/jobs")).EnumerateArray().Single(j => j.GetProperty("id").GetGuid() == id);

    private static Task<HttpResponseMessage> AcknowledgeAsync(HttpClient client, Guid jobId, string? actingAs)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/jobs/{jobId}/acknowledge");
        if (actingAs is not null)
        {
            request.Headers.Add("X-Acting-As", actingAs);
        }

        return client.SendAsync(request);
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

    private WebApplicationFactory<Program> CreateFactory(IClock clock, Action<Microsoft.AspNetCore.Hosting.IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Meridian", $"Data Source={_dbPath}");
            builder.UseSetting("Triage:Provider", "stub");
            configure?.Invoke(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(clock);
                // Ticks are driven by the test, not the timer.
                RemoveEscalationWorker(services);
            });
        });

    internal static void RemoveEscalationWorker(IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(EscalationWorker)).ToList())
        {
            services.Remove(descriptor);
        }
    }
}
