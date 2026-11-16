using System.Text.Json;
using Meridian.Application.Messages;
using Meridian.Application.Outbox;
using Meridian.Application.Triage;
using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;
using Meridian.Infrastructure.Persistence;
using Meridian.Infrastructure.Triage;
using static Meridian.Tests.Application.SubmitMessageTests;

namespace Meridian.Tests.Application;

/// <summary>The intake pipeline suggests each new job's usual contractor after its category is set (AD-3, R12).</summary>
public class SubmitMessageContractorTests
{
    private const string JoyText =
        "Hi there the tap in the bathroom has been dripping for about two weeks now, it's not urgent but it's quite annoying at night. Also while I'm emailing, the light in the hallway cupboard doesn't work, I think it's just the bulb but I'm not allowed to change it myself am I. Flat 4, 22 Pemberton Road. Thanks, Joy";

    private const string WalshText =
        "URGENT URGENT no hot water and no heating at all since this morning, I have a baby in the house, this is unacceptable, I have called three times. 14b Alder Court. Please someone call me TONIGHT.";

    private const string WalshWifeText =
        "My husband has emailed twice about the heating at 14b Alder Court, no one has responded. We have a 7 month old baby. What is going on.";

    private const string NowakText =
        "Dzień dobry, w mieszkaniu jest pleśń na ścianie w sypialni, coraz gorzej, dziecko ma astmę. Mieszkanie 9, Beechwood House. Dziękuję.";

    private static readonly DateTime FridayEvening = new(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_heating_job_at_Alder_Court_gets_its_building_contractor_after_the_model_outcome()
    {
        var (submit, jobs, _) = Create(StubModel(), FridayEvening);

        await submit.ExecuteAsync(new SubmitMessageCommand("m.walsh@example.com", WalshText), default);

        var job = Assert.Single(jobs.Added);
        Assert.Equal(Category.Heating, job.Category);
        Assert.Equal(FakeContractorRepository.Named("Warmhold Heating").Id, job.SuggestedContractorId);
        Assert.Equal(ContractorRule.Building, job.SuggestedContractorRule);
        var types = job.Events.Select(e => e.Type).ToList();
        var suggested = types.IndexOf(EventType.ContractorSuggested);
        Assert.True(suggested > types.IndexOf(EventType.ModelPrepared), string.Join(", ", types));
        Assert.Equal(EventType.AcknowledgementSent, types[^1]);
        using var payload = JsonDocument.Parse(job.Events[suggested].PayloadJson);
        Assert.Equal("building", payload.RootElement.GetProperty("rule").GetString());
    }

    [Fact]
    public async Task A_follow_up_in_the_same_category_writes_no_second_suggestion()
    {
        var clock = new FakeClock(FridayEvening);
        var (submit, jobs, _) = Create(StubModel(), clock);
        await submit.ExecuteAsync(new SubmitMessageCommand("m.walsh@example.com", WalshText), default);

        clock.UtcNow = FridayEvening.AddMinutes(75);
        await submit.ExecuteAsync(new SubmitMessageCommand("s.walsh@example.com", WalshWifeText), default);

        var job = Assert.Single(jobs.Added);
        Assert.Equal(1, job.FollowUpCount);
        Assert.Single(job.Events, e => e.Type == EventType.ContractorSuggested);
    }

    [Fact]
    public async Task Two_issues_get_their_own_trades_contractors()
    {
        var (submit, jobs, _) = Create(StubModel(), new DateTime(2026, 1, 13, 10, 42, 0, DateTimeKind.Utc));

        await submit.ExecuteAsync(new SubmitMessageCommand("joy@example.com", JoyText), default);

        Assert.Equal(2, jobs.Added.Count);
        Assert.Equal(FakeContractorRepository.Named("Northway Plumbing").Id, jobs.Added.Single(j => j.Category == Category.Plumbing).SuggestedContractorId);
        Assert.Equal(FakeContractorRepository.Named("Brightline Electrical").Id, jobs.Added.Single(j => j.Category == Category.Electrical).SuggestedContractorId);
        Assert.All(jobs.Added, j => Assert.Equal(ContractorRule.Building, j.SuggestedContractorRule));
    }

    [Fact]
    public async Task An_Unsure_job_has_no_suggestion_and_no_event()
    {
        var (submit, jobs, _) = Create(StubModel(), new DateTime(2026, 1, 14, 8, 3, 0, DateTimeKind.Utc));

        await submit.ExecuteAsync(new SubmitMessageCommand("a.nowak@example.com", NowakText), default);

        var job = Assert.Single(jobs.Added);
        Assert.Equal(Category.Unsure, job.Category);
        Assert.Null(job.SuggestedContractorId);
        Assert.DoesNotContain(job.Events, e => e.Type == EventType.ContractorSuggested);
    }

    [Fact]
    public async Task A_model_failure_makes_an_Unsure_job_with_no_suggestion()
    {
        var (submit, jobs, _) = Create(new FakeTriageModel().Fails("timeout"), FridayEvening);

        await submit.ExecuteAsync(new SubmitMessageCommand("tenant@example.com", "Boiler broken, 14b Alder Court"), default);

        var job = Assert.Single(jobs.Added);
        Assert.Null(job.SuggestedContractorId);
        Assert.DoesNotContain(job.Events, e => e.Type == EventType.ContractorSuggested);
    }

    [Fact]
    public async Task A_category_the_building_has_no_assignment_for_gets_the_default()
    {
        const string raw = """
            {"language":"en","issues":[{"summary_en":"Mice in the kitchen.","category":"Pest","urgency":"Routine","property_text":"14b Alder Court","explanation":"Mice.","vulnerability_mentioned":false,"confidence":"high"}]}
            """;
        var (submit, jobs, _) = Create(new FakeTriageModel().Returns(raw), FridayEvening);

        await submit.ExecuteAsync(new SubmitMessageCommand("tenant@example.com", "Mice in the kitchen at 14b Alder Court"), default);

        var job = Assert.Single(jobs.Added);
        Assert.Equal(Category.Pest, job.Category);
        Assert.Equal(FakeContractorRepository.Named("Clearway Pest Control").Id, job.SuggestedContractorId);
        Assert.Equal(ContractorRule.Default, job.SuggestedContractorRule);
    }

    private static StubTriageModel StubModel() => StubTriageModel.FromDirectory(StubTriageModel.DefaultDirectory);

    private static (SubmitMessage Submit, FakeJobRepository Jobs, FakeClock Clock) Create(ITriageModel model, DateTime now) =>
        Create(model, new FakeClock(now));

    private static (SubmitMessage Submit, FakeJobRepository Jobs, FakeClock Clock) Create(ITriageModel model, FakeClock clock)
    {
        var jobs = new FakeJobRepository();
        var submit = new SubmitMessage(
            jobs,
            new FakePropertyRepository(),
            new FakeContractorRepository(SeedData.CreateContractorAssignments()),
            new FakeUnitOfWork(),
            clock,
            new PrepareJob(model),
            new AcknowledgeTenant(new FakeOutbox()));
        return (submit, jobs, clock);
    }
}
