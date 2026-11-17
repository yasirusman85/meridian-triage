using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
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
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Api;

/// <summary>Contractor attendance through the real app: the link, mark attended, the overdue alert and resend (R20, R21).</summary>
public sealed class AttendanceApiTests : IDisposable
{
    private const string WalshText =
        "URGENT URGENT no hot water and no heating at all since this morning, I have a baby in the house, this is unacceptable, I have called three times. 14b Alder Court. Please someone call me TONIGHT.";

    private static readonly DateTime Received = new(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc);
    private static readonly Regex LinkToken = new(@"/c/([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"meridian-attendance-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Approved_then_marked_attended_by_the_contractor_shows_the_three_events_in_order()
    {
        var clock = new FakeClock(Received);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "m.walsh@example.com", WalshText);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/approve", "rachel")).StatusCode);
        var token = await LatestTokenAsync(client, jobId);

        var view = await client.GetAsync($"/api/contractor-jobs/{token}");

        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        var body = await view.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "address", "category", "contractorName", "reference", "summary" }, body.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("MER-000001", body.GetProperty("reference").GetString());
        Assert.Equal("14b Alder Court", body.GetProperty("address").GetString());
        Assert.Equal("Heating", body.GetProperty("category").GetString());
        Assert.Equal("Warmhold Heating", body.GetProperty("contractorName").GetString());

        var attended = await client.PostAsync($"/api/contractor-jobs/{token}/attended", null);

        Assert.Equal(HttpStatusCode.OK, attended.StatusCode);
        Assert.Equal("Attended", (await attended.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        var detail = await DetailAsync(client, jobId);
        Assert.Equal("Attended", detail.GetProperty("status").GetString());
        var types = detail.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.Equal(new[] { "JobApproved", "SentToContractor", "JobAttended" }, types.TakeLast(3));
        Assert.StartsWith("contractor:", detail.GetProperty("events").EnumerateArray().Last().GetProperty("actor").GetString());
    }

    [Fact]
    public async Task A_used_token_and_a_wrong_token_are_404_and_change_nothing()
    {
        var clock = new FakeClock(Received);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "m.walsh@example.com", WalshText);
        await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/approve", "rachel");
        var token = await LatestTokenAsync(client, jobId);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/contractor-jobs/{token}/attended", null)).StatusCode);
        var events = await EventCountAsync(factory, jobId);

        var used = await client.PostAsync($"/api/contractor-jobs/{token}/attended", null);
        var usedView = await client.GetAsync($"/api/contractor-jobs/{token}");
        var wrong = await client.PostAsync("/api/contractor-jobs/random-string/attended", null);
        var wrongView = await client.GetAsync("/api/contractor-jobs/random-string");

        Assert.Equal(HttpStatusCode.NotFound, used.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, usedView.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongView.StatusCode);
        Assert.Equal("application/problem+json", used.Content.Headers.ContentType?.MediaType);
        Assert.Equal(events, await EventCountAsync(factory, jobId));
    }

    [Fact]
    public async Task An_emergency_25_hours_old_raises_one_overdue_alert_at_the_top_of_the_queue_however_many_ticks_run()
    {
        var clock = new FakeClock(Received);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "m.walsh@example.com", WalshText);
        await PostAsync(client, "tenant@example.com", "Tap dripping");
        await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/approve", "rachel");
        var worker = Worker(factory);

        clock.UtcNow = Received.AddHours(25);
        await worker.RunOnceAsync(default);
        await worker.RunOnceAsync(default);

        var queue = (await client.GetFromJsonAsync<JsonElement>("/api/jobs")).EnumerateArray().ToList();
        Assert.Equal(jobId, queue[0].GetProperty("id").GetGuid());
        Assert.True(queue[0].GetProperty("attendanceOverdue").GetBoolean());
        Assert.False(queue[1].GetProperty("attendanceOverdue").GetBoolean());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        Assert.Equal(1, await db.JobEvents.CountAsync(e => e.JobId == jobId && e.Type == EventType.AttendanceOverdue));
    }

    [Fact]
    public async Task A_routine_job_sent_to_a_contractor_is_never_flagged_however_late()
    {
        var clock = new FakeClock(Received);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "tenant@example.com", "Tap dripping");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Patch, $"/api/jobs/{jobId}", "rachel", new { category = "Plumbing" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/approve", "rachel")).StatusCode);

        clock.UtcNow = Received.AddHours(30);
        await Worker(factory).RunOnceAsync(default);

        var listed = (await client.GetFromJsonAsync<JsonElement>("/api/jobs")).EnumerateArray().Single(j => j.GetProperty("id").GetGuid() == jobId);
        Assert.Equal("SentToContractor", listed.GetProperty("status").GetString());
        Assert.False(listed.GetProperty("attendanceOverdue").GetBoolean());
    }

    [Fact]
    public async Task A_person_resends_an_overdue_job_to_a_fallback_contractor_and_the_old_link_stops_working()
    {
        var clock = new FakeClock(Received);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "m.walsh@example.com", WalshText);
        await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/approve", "rachel");
        var oldToken = await LatestTokenAsync(client, jobId);
        clock.UtcNow = Received.AddHours(25);
        await Worker(factory).RunOnceAsync(default);
        var fallback = await ContractorIdAsync(client, "Cottonfield Boiler Services");

        var resent = await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/resend", "dawn", new { contractorId = fallback });

        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        var detail = await resent.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SentToContractor", detail.GetProperty("status").GetString());
        Assert.Equal("Cottonfield Boiler Services", detail.GetProperty("contractor").GetProperty("name").GetString());
        var queued = (await client.GetFromJsonAsync<JsonElement>("/api/jobs")).EnumerateArray().Single(j => j.GetProperty("id").GetGuid() == jobId);
        Assert.False(queued.GetProperty("attendanceOverdue").GetBoolean());

        var newToken = await LatestTokenAsync(client, jobId);
        Assert.NotEqual(oldToken, newToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/contractor-jobs/{oldToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/contractor-jobs/{newToken}")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("mallory")]
    public async Task Resend_without_a_known_person_is_400_and_changes_nothing(string? actingAs)
    {
        var clock = new FakeClock(Received);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "m.walsh@example.com", WalshText);
        await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/approve", "rachel");
        var events = await EventCountAsync(factory, jobId);
        var fallback = await ContractorIdAsync(client, "Cottonfield Boiler Services");

        var response = await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/resend", actingAs, new { contractorId = fallback });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(events, await EventCountAsync(factory, jobId));
    }

    private static async Task<string> LatestTokenAsync(HttpClient client, Guid jobId)
    {
        var outbound = await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}/outbound");
        var latest = outbound.EnumerateArray().Last(o => o.GetProperty("templateId").GetString() == "contractor-job");
        return LinkToken.Match(latest.GetProperty("body").GetString()!).Groups[1].Value;
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

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string? actor, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (actor is not null)
        {
            request.Headers.Add("X-Acting-As", actor);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> DetailAsync(HttpClient client, Guid jobId) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");

    private static async Task<Guid> ContractorIdAsync(HttpClient client, string name)
    {
        var contractors = await client.GetFromJsonAsync<JsonElement>("/api/contractors");
        return contractors.EnumerateArray().Single(c => c.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();
    }

    private static async Task<int> EventCountAsync(WebApplicationFactory<Program> factory, Guid jobId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MeridianDbContext>().JobEvents.CountAsync(e => e.JobId == jobId);
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

    private WebApplicationFactory<Program> CreateFactory(IClock clock) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Meridian", $"Data Source={_dbPath}");
            builder.UseSetting("Triage:Provider", "stub");
            builder.UseSetting("Demo:SeedClientMessages", "false");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(clock);
                // Ticks are driven by the test, not the timer.
                EscalationApiTests.RemoveEscalationWorker(services);
            });
        });
}
