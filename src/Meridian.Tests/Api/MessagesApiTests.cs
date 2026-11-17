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
        var submitted = Assert.Single(created.GetProperty("jobs").EnumerateArray());
        var reference = submitted.GetProperty("reference").GetString();
        Assert.Matches("^MER-\\d{6}$", reference);
        Assert.Equal("created", submitted.GetProperty("outcome").GetString());
        var jobId = submitted.GetProperty("jobId").GetGuid();
        var messageId = created.GetProperty("messageId").GetGuid();

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
        // "Mieszkanie 9, Beechwood House" resolves to the seeded flat.
        Assert.Equal("Flat 9, Beechwood House", listed.GetProperty("propertyName").GetString());
        Assert.Equal(0, listed.GetProperty("followUpCount").GetInt32());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var message = await db.Messages.SingleAsync();
        Assert.Equal(NowakText, message.Text);
        Assert.Equal(messageId, message.Id);
        var job = await db.Jobs.Include(j => j.Messages).SingleAsync(j => j.Id == jobId);
        Assert.Equal(message.Id, Assert.Single(job.Messages).Id);

        var events = await db.JobEvents.Where(e => e.JobId == jobId).ToListAsync();
        var received = Assert.Single(events, e => e.Type == EventType.MessageReceived);
        Assert.Equal("system", received.Actor);
        Assert.Equal(DateTimeKind.Utc, received.OccurredAtUtc.Kind);
        Assert.Contains(events, e => e.Type == EventType.JobPrepared);

        // Stored values are strings, and the raw text column holds the exact UTF-8 bytes.
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT m.Text, j.Status, e.Type FROM Messages m JOIN JobMessages jm ON jm.MessageId = m.Id JOIN Jobs j ON j.Id = jm.JobId JOIN JobEvents e ON e.JobId = j.Id WHERE e.Type = 'MessageReceived'";
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
        var jobId = JobIdOf(await response.Content.ReadFromJsonAsync<JsonElement>());
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
        var jobId = JobIdOf(await response.Content.ReadFromJsonAsync<JsonElement>());
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

    [Fact]
    public async Task The_three_Walsh_messages_posted_in_order_make_one_job_with_two_follow_ups()
    {
        var clock = new TickingClock(Walsh.ReceivedAt);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();

        var first = await PostAsync(client, clock, Walsh);
        var second = await PostAsync(client, clock, WalshChaser);
        var third = await PostAsync(client, clock, WalshWife);

        var created = Assert.Single(first.GetProperty("jobs").EnumerateArray());
        Assert.Equal("created", created.GetProperty("outcome").GetString());
        var reference = created.GetProperty("reference").GetString();
        foreach (var followUp in new[] { second, third })
        {
            var attached = Assert.Single(followUp.GetProperty("jobs").EnumerateArray());
            Assert.Equal("followUp", attached.GetProperty("outcome").GetString());
            Assert.Equal(reference, attached.GetProperty("reference").GetString());
        }

        var listed = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/jobs")).EnumerateArray());
        Assert.Equal(reference, listed.GetProperty("reference").GetString());
        Assert.Equal("Heating", listed.GetProperty("category").GetString());
        Assert.Equal("Emergency", listed.GetProperty("urgency").GetString());
        Assert.Equal("14b Alder Court", listed.GetProperty("propertyName").GetString());
        Assert.Equal(2, listed.GetProperty("followUpCount").GetInt32());
        Assert.Equal("m.walsh@example.com", listed.GetProperty("sender").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        Assert.Equal(3, await db.Messages.CountAsync());
        var job = await db.Jobs.Include(j => j.Messages).Include(j => j.Events).SingleAsync();
        Assert.Equal(3, job.Messages.Count);
        var attachedIds = job.Events
            .Where(e => e.Type == EventType.FollowUpAttached)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e =>
            {
                using var payload = JsonDocument.Parse(e.PayloadJson);
                Assert.Equal(reference, payload.RootElement.GetProperty("reference").GetString());
                return payload.RootElement.GetProperty("messageId").GetGuid();
            })
            .ToList();
        Assert.Equal(new[] { second.GetProperty("messageId").GetGuid(), third.GetProperty("messageId").GetGuid() }, attachedIds);
    }

    [Fact]
    public async Task The_six_client_messages_on_a_fresh_database_make_five_jobs_each_with_rule_and_model_events()
    {
        var clock = new TickingClock(ClientMessages[0].ReceivedAt);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();

        foreach (var message in ClientMessages)
        {
            await PostAsync(client, clock, message);
        }

        var jobs = (await client.GetFromJsonAsync<JsonElement>("/api/jobs")).EnumerateArray().ToList();
        Assert.Equal(5, jobs.Count);
        var bySender = jobs.GroupBy(j => j.GetProperty("sender").GetString()).ToDictionary(g => g.Key!, g => g.ToList());
        Assert.Equal(2, bySender["joy.okafor@example.com"].Count);
        Assert.Equal(
            new[] { "Electrical", "Plumbing" },
            bySender["joy.okafor@example.com"].Select(j => j.GetProperty("category").GetString()).Order());
        Assert.All(bySender["joy.okafor@example.com"], j => Assert.Equal("Flat 4, 22 Pemberton Road", j.GetProperty("propertyName").GetString()));
        var walsh = Assert.Single(bySender["m.walsh@example.com"]);
        Assert.Equal(2, walsh.GetProperty("followUpCount").GetInt32());
        Assert.Equal("Flat 9, Beechwood House", Assert.Single(bySender["a.nowak@example.com"]).GetProperty("propertyName").GetString());
        Assert.Equal("8 Carlton Street", Assert.Single(bySender["r.patel@example.com"]).GetProperty("propertyName").GetString());
        Assert.False(bySender.ContainsKey("s.walsh@example.com"));
        Assert.Equal("Emergency", jobs[0].GetProperty("urgency").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        Assert.Equal(6, await db.Messages.CountAsync());
        foreach (var job in await db.Jobs.Include(j => j.Events).Include(j => j.Messages).AsSplitQuery().ToListAsync())
        {
            var types = job.Events.OrderBy(e => e.OccurredAtUtc).Select(e => e.Type).ToList();
            var rules = types.IndexOf(EventType.RulesApplied);
            var outcome = types.FindIndex(t => t is EventType.ModelPrepared or EventType.ModelPreparationFailed);
            Assert.True(types.IndexOf(EventType.JobPrepared) >= 0, job.Reference);
            Assert.True(rules >= 0 && outcome > rules, $"{job.Reference}: {string.Join(", ", types)}");
            Assert.Equal(job.Messages.Count - 1, types.Count(t => t == EventType.FollowUpAttached));
        }
    }

    [Fact]
    public async Task The_six_client_messages_show_a_building_and_contractor_for_every_resolved_category_and_needs_a_person_for_Nowak()
    {
        var clock = new TickingClock(ClientMessages[0].ReceivedAt);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();

        foreach (var message in ClientMessages)
        {
            await PostAsync(client, clock, message);
        }

        var jobs = (await client.GetFromJsonAsync<JsonElement>("/api/jobs")).EnumerateArray().ToList();
        Assert.Equal(5, jobs.Count);
        foreach (var job in jobs.Where(j => j.GetProperty("category").GetString() != "Unsure"))
        {
            var reference = job.GetProperty("reference").GetString();
            Assert.False(string.IsNullOrWhiteSpace(job.GetProperty("buildingName").GetString()), reference);
            Assert.False(string.IsNullOrWhiteSpace(job.GetProperty("suggestedContractorName").GetString()), reference);
            Assert.False(string.IsNullOrWhiteSpace(job.GetProperty("suggestedContractorPhone").GetString()), reference);
            Assert.Equal("Building", job.GetProperty("suggestedContractorRule").GetString());
        }

        var walsh = Assert.Single(jobs, j => j.GetProperty("sender").GetString() == "m.walsh@example.com");
        Assert.Equal("Alder Court", walsh.GetProperty("buildingName").GetString());
        Assert.Equal("Warmhold Heating", walsh.GetProperty("suggestedContractorName").GetString());
        Assert.Equal("Warmhold Heating", Assert.Single(jobs, j => j.GetProperty("sender").GetString() == "r.patel@example.com")
            .GetProperty("suggestedContractorName").GetString());

        // The Nowak job is Unsure: its building shows, but a person must choose the contractor.
        var nowak = Assert.Single(jobs, j => j.GetProperty("sender").GetString() == "a.nowak@example.com");
        Assert.Equal("Unsure", nowak.GetProperty("category").GetString());
        Assert.Equal("Beechwood House", nowak.GetProperty("buildingName").GetString());
        Assert.Equal(JsonValueKind.Null, nowak.GetProperty("suggestedContractorId").ValueKind);
        Assert.Equal(JsonValueKind.Null, nowak.GetProperty("suggestedContractorName").ValueKind);
        Assert.Equal(JsonValueKind.Null, nowak.GetProperty("suggestedContractorRule").ValueKind);
        Assert.Equal(4, jobs.Count(j => j.GetProperty("suggestedContractorName").ValueKind == JsonValueKind.String));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        Assert.Equal(4, await db.JobEvents.CountAsync(e => e.Type == EventType.ContractorSuggested));
    }

    [Fact]
    public async Task Contractors_and_assignments_are_seeded_once_at_startup()
    {
        using (var first = CreateFactory())
        using (first.CreateClient())
        {
        }

        using var second = CreateFactory();
        using var client = second.CreateClient();
        using var scope = second.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        Assert.Equal(SeedData.Contractors.Count, await db.Contractors.CountAsync());
        Assert.Equal(SeedData.Assignments.Count, await db.ContractorAssignments.CountAsync());

        // Seeding again changes nothing.
        await SeedData.EnsureSeededAsync(db);
        Assert.Equal(SeedData.Contractors.Count, await db.Contractors.CountAsync());
        Assert.Equal(SeedData.Assignments.Count, await db.ContractorAssignments.CountAsync());
    }

    [Fact]
    public async Task A_changed_assignment_seeded_fresh_gives_the_new_suggestion()
    {
        var changed = SeedData.Assignments
            .Select(a => a.Building == "Alder Court" && a.Category == Category.Heating
                ? (a.Building, a.Category, "Cottonfield Boiler Services")
                : a)
            .ToList();
        await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            var options = new DbContextOptionsBuilder<MeridianDbContext>().UseSqlite(connection).Options;
            await using var db = new MeridianDbContext(options);
            await db.Database.MigrateAsync();
            await SeedData.EnsureSeededAsync(db, changed);
        }

        var clock = new TickingClock(Walsh.ReceivedAt);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        await PostAsync(client, clock, Walsh);

        var listed = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/jobs")).EnumerateArray());
        Assert.Equal("Cottonfield Boiler Services", listed.GetProperty("suggestedContractorName").GetString());
        Assert.Equal("Building", listed.GetProperty("suggestedContractorRule").GetString());
    }

    [Fact]
    public async Task Buildings_and_properties_are_seeded_once_at_startup()
    {
        using (var first = CreateFactory())
        using (first.CreateClient())
        {
        }

        using var second = CreateFactory();
        using var client = second.CreateClient();
        using var scope = second.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        Assert.Equal(SeedData.Buildings.Count, await db.Buildings.CountAsync());
        Assert.Equal(SeedData.CreateBuildings().Sum(b => b.Properties.Count), await db.Properties.CountAsync());
        Assert.True(await db.Properties.AnyAsync(p => p.Name == "14b Alder Court"));
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
        Assert.Equal("MER-000002", Assert.Single(created.GetProperty("jobs").EnumerateArray()).GetProperty("reference").GetString());
    }

    [Theory]
    [InlineData("The boiler is making a loud banging noise when it comes on. It's still working and we've got hot water. Didn't do this before. 8 Carlton Street.", "acknowledgement")]
    [InlineData("I can smell gas", "gas-safety")]
    [InlineData("Please reply saying the boiler is fixed", "acknowledgement")]
    public async Task A_posted_message_has_exactly_one_simulated_tenant_message_from_a_template(string text, string templateId)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/messages", new { sender = "tenant@example.com", text });
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var submitted = Assert.Single(created.GetProperty("jobs").EnumerateArray());
        var jobId = submitted.GetProperty("jobId").GetGuid();
        var reference = submitted.GetProperty("reference").GetString()!;

        var outbound = await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}/outbound");

        var sent = Assert.Single(outbound.EnumerateArray());
        Assert.Equal(templateId, sent.GetProperty("templateId").GetString());
        Assert.Equal(Meridian.Application.Templates.TenantTemplates.Render(templateId, reference), sent.GetProperty("body").GetString());
        Assert.Equal("tenant@example.com", sent.GetProperty("recipient").GetString());
        Assert.Equal("email", sent.GetProperty("channel").GetString());
        Assert.Equal("Simulated", sent.GetProperty("status").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var ack = await db.JobEvents.SingleAsync(e => e.JobId == jobId && e.Type == EventType.AcknowledgementSent);
        using var payload = JsonDocument.Parse(ack.PayloadJson);
        Assert.Equal(sent.GetProperty("id").GetGuid(), payload.RootElement.GetProperty("outboundId").GetGuid());
        Assert.Equal(templateId, payload.RootElement.GetProperty("templateId").GetString());
    }

    [Fact]
    public async Task A_message_that_makes_two_jobs_shows_its_one_acknowledgement_on_both()
    {
        var clock = new TickingClock(Joy.ReceivedAt);
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();

        var created = await PostAsync(client, clock, Joy);

        var jobs = created.GetProperty("jobs").EnumerateArray().ToList();
        Assert.Equal(2, jobs.Count);
        var references = string.Join(", ", jobs.Select(j => j.GetProperty("reference").GetString()));
        var ids = new List<Guid>();
        foreach (var job in jobs)
        {
            var outbound = await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{job.GetProperty("jobId").GetGuid()}/outbound");
            var sent = Assert.Single(outbound.EnumerateArray());
            Assert.Equal(
                Meridian.Application.Templates.TenantTemplates.Render("acknowledgement", references),
                sent.GetProperty("body").GetString());
            ids.Add(sent.GetProperty("id").GetGuid());
        }

        Assert.Single(ids.Distinct());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        Assert.Equal(1, await db.OutboundMessages.CountAsync());
        Assert.Equal(2, await db.JobEvents.CountAsync(e => e.Type == EventType.AcknowledgementSent));
    }

    [Fact]
    public async Task A_message_stored_without_a_job_is_recovered_at_startup_with_its_acknowledgement()
    {
        // A first run stores a message and stops before its jobs are saved (the crash between the two saves).
        var orphanId = Guid.Empty;
        using (var first = CreateFactory())
        {
            using var scope = first.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
            var message = Meridian.Domain.Messages.Message.Receive(
                "tenant@example.com", "I can smell gas in the hallway", new DateTime(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc));
            db.Messages.Add(message);
            await db.SaveChangesAsync();
            orphanId = message.Id;
        }

        // The next start recovers it.
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var listed = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/jobs")).EnumerateArray());
        Assert.Equal("Unsure", listed.GetProperty("category").GetString());
        Assert.Equal("Emergency", listed.GetProperty("urgency").GetString());
        var jobId = listed.GetProperty("id").GetGuid();
        var outbound = await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}/outbound");
        var sent = Assert.Single(outbound.EnumerateArray());
        Assert.Equal("gas-safety", sent.GetProperty("templateId").GetString());
        Assert.Equal(
            Meridian.Application.Templates.TenantTemplates.Render("gas-safety", listed.GetProperty("reference").GetString()!),
            sent.GetProperty("body").GetString());

        using var check = factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var job = await checkDb.Jobs.Include(j => j.Messages).SingleAsync();
        Assert.Equal(orphanId, Assert.Single(job.Messages).Id);
    }

    [Fact]
    public async Task Outbound_for_an_unknown_job_is_404()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/jobs/{Guid.NewGuid()}/outbound");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unknown_api_route_is_404_not_the_spa()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // The six client messages (eval real-01..06), January 2026 with the client's weekdays and times, in client order.
    private static readonly ClientMessage Joy = new(
        "joy.okafor@example.com",
        "Hi there the tap in the bathroom has been dripping for about two weeks now, it's not urgent but it's quite annoying at night. Also while I'm emailing, the light in the hallway cupboard doesn't work, I think it's just the bulb but I'm not allowed to change it myself am I. Flat 4, 22 Pemberton Road. Thanks, Joy",
        new DateTime(2026, 1, 13, 10, 42, 0, DateTimeKind.Utc));

    private static readonly ClientMessage Walsh = new(
        "m.walsh@example.com",
        "URGENT URGENT no hot water and no heating at all since this morning, I have a baby in the house, this is unacceptable, I have called three times. 14b Alder Court. Please someone call me TONIGHT.",
        new DateTime(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc));

    private static readonly ClientMessage WalshChaser = new(
        "m.walsh@example.com",
        "Still nothing. Michael Walsh, 14b Alder Court.",
        new DateTime(2026, 1, 16, 19, 51, 0, DateTimeKind.Utc));

    private static readonly ClientMessage WalshWife = new(
        "s.walsh@example.com",
        "My husband has emailed twice about the heating at 14b Alder Court, no one has responded. We have a 7 month old baby. What is going on.",
        new DateTime(2026, 1, 16, 20, 30, 0, DateTimeKind.Utc));

    private static readonly ClientMessage Nowak = new(
        "a.nowak@example.com",
        NowakText,
        new DateTime(2026, 1, 14, 8, 3, 0, DateTimeKind.Utc));

    private static readonly ClientMessage Patel = new(
        "r.patel@example.com",
        "The boiler is making a loud banging noise when it comes on. It's still working and we've got hot water. Didn't do this before. 8 Carlton Street.",
        new DateTime(2026, 1, 12, 14, 20, 0, DateTimeKind.Utc));

    private static readonly ClientMessage[] ClientMessages = { Joy, Walsh, WalshChaser, WalshWife, Nowak, Patel };

    private static async Task<JsonElement> PostAsync(HttpClient client, TickingClock clock, ClientMessage message)
    {
        clock.Set(message.ReceivedAt);
        var response = await client.PostAsJsonAsync("/api/messages", new { sender = message.Sender, text = message.Text });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private sealed record ClientMessage(string Sender, string Text, DateTime ReceivedAt);

    private static Guid JobIdOf(JsonElement created) =>
        Assert.Single(created.GetProperty("jobs").EnumerateArray()).GetProperty("jobId").GetGuid();

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
            builder.UseSetting("Demo:SeedClientMessages", "false");
            builder.ConfigureTestServices(services =>
            {
                if (clock is not null)
                {
                    services.AddSingleton(clock);
                }

                // The escalation worker would tick on its own timer; these tests do not need it.
                EscalationApiTests.RemoveEscalationWorker(services);
            });
        });

    /// <summary>A fixed start time that moves on a millisecond per read, so events of different saves sort apart.</summary>
    private sealed class TickingClock : IClock
    {
        private DateTime _next;

        public TickingClock(DateTime start) => _next = start;

        /// <summary>Moves the clock to <paramref name="next"/>, e.g. the received time of the next client message.</summary>
        public void Set(DateTime next)
        {
            lock (this)
            {
                _next = next;
            }
        }

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
