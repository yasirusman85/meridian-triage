using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Domain.Jobs;
using Meridian.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Api;

/// <summary>The review endpoints against the real app and database, one row of the story's I/O matrix each.</summary>
public sealed class ReviewApiTests : IDisposable
{
    private const string NowakText =
        "Dzień dobry, w mieszkaniu jest pleśń na ścianie w sypialni, coraz gorzej, dziecko ma astmę. Mieszkanie 9, Beechwood House. Dziękuję.";

    private const string WalshText =
        "URGENT URGENT no hot water and no heating at all since this morning, I have a baby in the house, this is unacceptable, I have called three times. 14b Alder Court. Please someone call me TONIGHT.";

    private const string PatelText =
        "The boiler is making a loud banging noise when it comes on. It's still working and we've got hot water. Didn't do this before. 8 Carlton Street.";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"meridian-review-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Rachel_sets_the_Nowak_job_to_General_with_a_contractor_and_approves_it()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "a.nowak@example.com", NowakText);
        var contractorId = await ContractorIdAsync(client, "Handyside Maintenance");

        var edited = await SendAsync(client, HttpMethod.Patch, $"/api/jobs/{jobId}", "rachel", new { category = "General", contractorId });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var approved = await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/approve", "rachel");

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var detail = await approved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SentToContractor", detail.GetProperty("status").GetString());
        Assert.Equal("General", detail.GetProperty("category").GetString());
        Assert.Equal("Handyside Maintenance", detail.GetProperty("contractor").GetProperty("name").GetString());
        Assert.Equal("Person", detail.GetProperty("contractor").GetProperty("rule").GetString());
        Assert.False(detail.GetProperty("canReview").GetBoolean());

        var events = detail.GetProperty("events").EnumerateArray().ToList();
        var review = events.Where(e => e.GetProperty("actor").GetString() == "rachel").ToList();
        Assert.Equal(new[] { "JobEdited", "JobEdited", "JobApproved", "SentToContractor" }, review.Select(e => e.GetProperty("type").GetString()));
        Assert.Contains(review, e => e.GetProperty("summary").GetString() == "Edited category: Unsure → General");
        Assert.Contains(review, e => e.GetProperty("summary").GetString() == "Edited contractor: none → Handyside Maintenance");

        // Stored: the same events, with field, before and after.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var stored = await db.JobEvents.Where(e => e.JobId == jobId && e.Actor == "rachel").ToListAsync();
        var category = stored.Single(e => e.Type == EventType.JobEdited && e.PayloadJson.Contains("\"category\""));
        using var payload = JsonDocument.Parse(category.PayloadJson);
        Assert.Equal("Unsure", payload.RootElement.GetProperty("before").GetString());
        Assert.Equal("General", payload.RootElement.GetProperty("after").GetString());
        Assert.Single(stored, e => e.Type == EventType.JobApproved);
        Assert.Single(stored, e => e.Type == EventType.SentToContractor);
        Assert.Equal(JobStatus.SentToContractor, (await db.Jobs.SingleAsync(j => j.Id == jobId)).Status);
    }

    [Fact]
    public async Task Approve_a_prepared_Heating_job_with_a_contractor()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "m.walsh@example.com", WalshText);

        var response = await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/approve", "dawn");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SentToContractor", detail.GetProperty("status").GetString());
        var events = detail.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(new[] { "JobApproved", "SentToContractor" }, events.TakeLast(2).Select(e => e.GetProperty("type").GetString()));
        Assert.Equal("dawn", events[^2].GetProperty("actor").GetString());
        Assert.Equal("dawn", events[^1].GetProperty("actor").GetString());
    }

    [Fact]
    public async Task Approve_an_Unsure_job_is_409_set_a_category_and_nothing_changes()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "a.nowak@example.com", NowakText);
        var before = await EventCountAsync(factory, jobId);

        var response = await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/approve", "rachel");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("set a category", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("category", problem.GetProperty("missing").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(before, await EventCountAsync(factory, jobId));
        Assert.Equal("Prepared", (await DetailAsync(client, jobId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Edit_category_from_Unsure_to_Plumbing_re_suggests_the_contractor()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "a.nowak@example.com", NowakText);

        var response = await SendAsync(client, HttpMethod.Patch, $"/api/jobs/{jobId}", "rachel", new { category = "Plumbing" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Plumbing", detail.GetProperty("category").GetString());
        Assert.Equal("Northway Plumbing", detail.GetProperty("contractor").GetProperty("name").GetString());
        Assert.Equal("Building", detail.GetProperty("contractor").GetProperty("rule").GetString());
        var events = detail.GetProperty("events").EnumerateArray().ToList();
        Assert.Contains(events, e => e.GetProperty("type").GetString() == "JobEdited" && e.GetProperty("actor").GetString() == "rachel");
        Assert.Equal("ContractorSuggested", events[^1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Lower_urgency_without_a_reason_is_400_and_nothing_changes()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "m.walsh@example.com", WalshText);
        var before = await EventCountAsync(factory, jobId);

        var response = await SendAsync(client, HttpMethod.Patch, $"/api/jobs/{jobId}", "rachel", new { urgency = "Urgent" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("urgencyReason", out _));
        Assert.Equal(before, await EventCountAsync(factory, jobId));
        Assert.Equal("Emergency", (await DetailAsync(client, jobId)).GetProperty("urgency").GetString());
    }

    [Fact]
    public async Task Lower_urgency_with_a_reason_writes_UrgencySetByPerson()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "m.walsh@example.com", WalshText);

        var response = await SendAsync(
            client, HttpMethod.Patch, $"/api/jobs/{jobId}", "rachel", new { urgency = "Urgent", urgencyReason = "heating back on" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Urgent", detail.GetProperty("urgency").GetString());
        var last = detail.GetProperty("events").EnumerateArray().Last();
        Assert.Equal("UrgencySetByPerson", last.GetProperty("type").GetString());
        Assert.Equal("rachel", last.GetProperty("actor").GetString());
        Assert.Contains("heating back on", last.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Reject_with_reason_duplicate_writes_JobRejected()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "r.patel@example.com", PatelText);

        var response = await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/reject", "dawn", new { reason = "duplicate" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Rejected", detail.GetProperty("status").GetString());
        var last = detail.GetProperty("events").EnumerateArray().Last();
        Assert.Equal("JobRejected", last.GetProperty("type").GetString());
        Assert.Equal("dawn", last.GetProperty("actor").GetString());
        Assert.Equal("Rejected: duplicate", last.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Reject_without_a_reason_is_400()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "r.patel@example.com", PatelText);

        var response = await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/reject", "dawn", new { reason = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Prepared", (await DetailAsync(client, jobId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Approve_twice_and_edit_after_approve_are_409_and_nothing_changes()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "m.walsh@example.com", WalshText);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/approve", "rachel")).StatusCode);
        var before = await EventCountAsync(factory, jobId);

        var again = await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/approve", "rachel");
        var edit = await SendAsync(client, HttpMethod.Patch, $"/api/jobs/{jobId}", "rachel", new { category = "Plumbing" });
        var reject = await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/reject", "rachel", new { reason = "duplicate" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
        Assert.Equal("application/problem+json", again.Content.Headers.ContentType?.MediaType);
        Assert.Equal(before, await EventCountAsync(factory, jobId));
        var detail = await DetailAsync(client, jobId);
        Assert.Equal("SentToContractor", detail.GetProperty("status").GetString());
        Assert.Equal("Heating", detail.GetProperty("category").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("mallory")]
    public async Task No_or_unknown_actor_is_400_and_nothing_changes(string? actor)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "m.walsh@example.com", WalshText);
        var before = await EventCountAsync(factory, jobId);

        var responses = new[]
        {
            await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/approve", actor),
            await SendAsync(client, HttpMethod.Post, $"/api/jobs/{jobId}/reject", actor, new { reason = "duplicate" }),
            await SendAsync(client, HttpMethod.Patch, $"/api/jobs/{jobId}", actor, new { category = "Plumbing" }),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(before, await EventCountAsync(factory, jobId));
        Assert.Equal("Prepared", (await DetailAsync(client, jobId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_detail_shows_explanation_summary_messages_rules_and_contractor()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "m.walsh@example.com", WalshText);

        var detail = await DetailAsync(client, jobId);

        Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty("explanation").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty("summaryEn").GetString()));
        Assert.Equal(WalshText, Assert.Single(detail.GetProperty("messages").EnumerateArray()).GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.Array, detail.GetProperty("rules").ValueKind);
        Assert.Equal("Warmhold Heating", detail.GetProperty("contractor").GetProperty("name").GetString());
        Assert.Equal("Alder Court", detail.GetProperty("buildingName").GetString());
        Assert.True(detail.GetProperty("canReview").GetBoolean());
        Assert.Empty(detail.GetProperty("missingForApproval").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/jobs/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, $"/api/jobs/{Guid.NewGuid()}/approve", "rachel")).StatusCode);
    }

    [Fact]
    public async Task The_staff_list_is_Rachel_and_Dawn_and_the_form_lists_contractors_and_properties()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var staff = await client.GetFromJsonAsync<JsonElement>("/api/staff");
        var contractors = await client.GetFromJsonAsync<JsonElement>("/api/contractors");
        var properties = await client.GetFromJsonAsync<JsonElement>("/api/properties");

        Assert.Equal(new[] { "rachel", "dawn" }, staff.EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        Assert.Equal(new[] { "Rachel", "Dawn" }, staff.EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(SeedData.Contractors.Count, contractors.GetArrayLength());
        Assert.Contains(properties.EnumerateArray(), p => p.GetProperty("name").GetString() == "14b Alder Court");
    }

    [Fact]
    public async Task An_invalid_category_value_is_400()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var jobId = await PostAsync(client, "a.nowak@example.com", NowakText);

        var response = await SendAsync(client, HttpMethod.Patch, $"/api/jobs/{jobId}", "rachel", new { category = "Roofing" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    private static async Task<Guid> PostAsync(HttpClient client, string sender, string text)
    {
        var response = await client.PostAsJsonAsync("/api/messages", new { sender, text });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Assert.Single(created.GetProperty("jobs").EnumerateArray()).GetProperty("jobId").GetGuid();
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
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        return await db.JobEvents.CountAsync(e => e.JobId == jobId);
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
