using Meridian.Application.Review;
using Meridian.Application.Staff;
using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;

namespace Meridian.Tests.Application;

public class ReviewJobTests
{
    private static readonly DateTime T0 = new(2026, 1, 14, 8, 3, 0, DateTimeKind.Utc);

    private readonly SubmitMessageTests.FakeJobRepository _jobs = new();
    private readonly FakeClock _clock = new(T0.AddMinutes(10));
    private readonly FakeOutbox _outbox = new();

    [Fact]
    public async Task Setting_the_category_re_runs_the_contractor_lookup_for_the_building()
    {
        var job = AddJob("Flat 9, Beechwood House");

        var result = await Review().EditAsync(job.Id, "rachel", new JobEdit(Category: Category.Plumbing), default);

        Assert.Equal(ReviewOutcome.Done, result.Outcome);
        Assert.Equal(Category.Plumbing, job.Category);
        Assert.Equal(Contractor("Northway Plumbing"), job.SuggestedContractorId);
        Assert.Equal(ContractorRule.Building, job.SuggestedContractorRule);
        Assert.Equal(new[] { EventType.JobEdited, EventType.ContractorSuggested }, job.Events.TakeLast(2).Select(e => e.Type));
        Assert.Equal("rachel", job.Events[^2].Actor);
    }

    [Fact]
    public async Task Setting_the_category_back_to_Unsure_clears_the_contractor()
    {
        var job = AddJob("Flat 9, Beechwood House");
        await Review().EditAsync(job.Id, "rachel", new JobEdit(Category: Category.Plumbing), default);

        var result = await Review().EditAsync(job.Id, "dawn", new JobEdit(Category: Category.Unsure), default);

        Assert.Equal(ReviewOutcome.Done, result.Outcome);
        Assert.Null(job.SuggestedContractorId);
        Assert.Equal(new[] { EventType.JobEdited, EventType.JobEdited }, job.Events.TakeLast(2).Select(e => e.Type));
        Assert.All(job.Events.TakeLast(2), e => Assert.Equal("dawn", e.Actor));
    }

    [Fact]
    public async Task A_contractor_picked_with_the_category_wins_over_the_lookup()
    {
        var job = AddJob("Flat 9, Beechwood House");
        var chosen = Contractor("Irwell Pipe and Drain");

        await Review().EditAsync(job.Id, "rachel", new JobEdit(Category: Category.Plumbing, ContractorId: chosen), default);

        Assert.Equal(chosen, job.SuggestedContractorId);
        Assert.Equal(ContractorRule.Person, job.SuggestedContractorRule);
        Assert.DoesNotContain(job.Events, e => e.Type == EventType.ContractorSuggested);
    }

    [Fact]
    public async Task Setting_the_property_of_an_unresolved_job_re_suggests_for_its_building()
    {
        var job = AddJob(null);
        job.EditCategory(Category.Heating, "rachel", T0);
        job.SuggestContractor(new ContractorSuggestion(Contractor("Warmhold Heating"), ContractorRule.Default), T0);
        var flat = SubmitMessageTests.FakePropertyRepository.Named("14b Alder Court");

        var result = await Review().EditAsync(job.Id, "rachel", new JobEdit(PropertyId: flat.Id), default);

        Assert.Equal(ReviewOutcome.Done, result.Outcome);
        Assert.Equal(flat.Id, job.PropertyId);
        Assert.Equal(ContractorRule.Building, job.SuggestedContractorRule);
    }

    [Fact]
    public async Task Lowering_urgency_without_a_reason_is_invalid_and_changes_nothing()
    {
        var job = AddJob("Flat 9, Beechwood House");
        job.RaiseUrgency(Urgency.Emergency, "rule:gas", T0);
        var count = job.Events.Count;

        var result = await Review().EditAsync(job.Id, "rachel", new JobEdit(Category: Category.Heating, Urgency: Urgency.Urgent), default);

        Assert.Equal(ReviewOutcome.Invalid, result.Outcome);
        Assert.Contains(ReviewJob.Fields.UrgencyReason, result.Errors.Keys);
        Assert.Equal(count, job.Events.Count);
        Assert.Equal(Category.Unsure, job.Category);
        Assert.Equal(Urgency.Emergency, job.Urgency);
    }

    [Fact]
    public async Task Unknown_contractors_and_properties_are_invalid()
    {
        var job = AddJob("Flat 9, Beechwood House");

        Assert.Equal(ReviewOutcome.Invalid, (await Review().EditAsync(job.Id, "rachel", new JobEdit(ContractorId: Guid.NewGuid()), default)).Outcome);
        Assert.Equal(ReviewOutcome.Invalid, (await Review().EditAsync(job.Id, "rachel", new JobEdit(PropertyId: Guid.NewGuid()), default)).Outcome);
        Assert.Equal(ReviewOutcome.Invalid, (await Review().EditAsync(job.Id, "rachel", new JobEdit(), default)).Outcome);
        Assert.Equal(ReviewOutcome.Invalid, (await Review().EditAsync(job.Id, "rachel", new JobEdit(Category: (Category)99), default)).Outcome);
        Assert.Equal(3, job.Events.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mallory")]
    [InlineData("system")]
    public async Task A_missing_or_unknown_actor_is_invalid_for_every_action(string? actor)
    {
        var job = AddJob("Flat 9, Beechwood House");

        var results = new[]
        {
            await Review().ApproveAsync(job.Id, actor, default),
            await Review().RejectAsync(job.Id, actor, "duplicate", default),
            await Review().RejectAsync(job.Id, actor, null, default),
            await Review().EditAsync(job.Id, actor, new JobEdit(Category: Category.General), default),
        };

        Assert.All(results, r =>
        {
            Assert.Equal(ReviewOutcome.Invalid, r.Outcome);
            Assert.Contains(ReviewJob.Fields.Actor, r.Errors.Keys);
        });
        Assert.Equal(JobStatus.Prepared, job.Status);
        Assert.Equal(3, job.Events.Count);
    }

    [Fact]
    public async Task The_actor_is_matched_ignoring_case_and_recorded_as_the_staff_id()
    {
        var job = AddJob("Flat 9, Beechwood House");

        await Review().RejectAsync(job.Id, " Rachel ", "duplicate", default);

        Assert.Equal("rachel", job.Events[^1].Actor);
    }

    [Fact]
    public async Task Approving_an_Unsure_job_is_a_conflict_saying_what_is_missing()
    {
        var job = AddJob("Flat 9, Beechwood House");

        var result = await Review().ApproveAsync(job.Id, "rachel", default);

        Assert.Equal(ReviewOutcome.Conflict, result.Outcome);
        Assert.Equal(new[] { "category", "contractor" }, result.Missing);
        Assert.Equal("Set a category and choose a contractor before approving.", result.Detail);
        Assert.Equal(JobStatus.Prepared, job.Status);
    }

    [Fact]
    public async Task Acting_on_an_unknown_job_is_not_found_and_a_reviewed_job_is_a_conflict()
    {
        Assert.Equal(ReviewOutcome.NotFound, (await Review().ApproveAsync(Guid.NewGuid(), "rachel", default)).Outcome);

        var job = AddJob("Flat 9, Beechwood House");
        await Review().RejectAsync(job.Id, "rachel", "duplicate", default);

        Assert.Equal(ReviewOutcome.Conflict, (await Review().RejectAsync(job.Id, "rachel", "again", default)).Outcome);
        Assert.Equal(ReviewOutcome.Conflict, (await Review().EditAsync(job.Id, "rachel", new JobEdit(Category: Category.General), default)).Outcome);
    }

    [Fact]
    public async Task The_detail_shows_messages_rules_contractor_and_a_readable_history()
    {
        var job = AddJob("Flat 9, Beechwood House");
        await Review().EditAsync(job.Id, "rachel", new JobEdit(Category: Category.General, ContractorId: Contractor("Handyside Maintenance")), default);
        await Review().ApproveAsync(job.Id, "rachel", default);

        var detail = await new GetJob(_jobs, new SubmitMessageTests.FakePropertyRepository(), new SubmitMessageTests.FakeContractorRepository())
            .ExecuteAsync(job.Id, default);

        Assert.NotNull(detail);
        Assert.Equal(JobStatus.SentToContractor, detail.Status);
        Assert.False(detail.CanReview);
        Assert.Equal("Beechwood House", detail.BuildingName);
        Assert.Equal("Handyside Maintenance", detail.Contractor?.Name);
        Assert.Equal(ContractorRule.Person, detail.Contractor?.Rule);
        Assert.Equal("mould in the bedroom", Assert.Single(detail.Messages).Text);
        Assert.Contains(detail.Events, e => e.Summary == "Edited category: Unsure → General" && e.Actor == "rachel");
        Assert.Contains(detail.Events, e => e.Summary == "Edited contractor: none → Handyside Maintenance" && e.Actor == "rachel");
        Assert.Equal(new[] { EventType.JobApproved, EventType.SentToContractor }, detail.Events.TakeLast(2).Select(e => e.Type));
        Assert.Equal("rachel", detail.Events[^1].Actor);
    }

    private ReviewJob Review() => new(
        _jobs,
        new SubmitMessageTests.FakePropertyRepository(),
        new SubmitMessageTests.FakeContractorRepository(),
        new SubmitMessageTests.FakeUnitOfWork(),
        _clock,
        new StaffDirectory(),
        _outbox);

    private Job AddJob(string? propertyName)
    {
        var propertyId = propertyName is null ? (Guid?)null : SubmitMessageTests.FakePropertyRepository.Named(propertyName).Id;
        var job = Job.CreateFromMessage(Message.Receive("a.nowak@example.com", "mould in the bedroom", T0), $"MER-{_jobs.Added.Count + 1:000000}", T0, propertyId);
        job.ApplyRules(Array.Empty<Meridian.Domain.Rules.RuleHit>(), T0);
        _jobs.Add(job);
        return job;
    }

    private static Guid Contractor(string name) => SubmitMessageTests.FakeContractorRepository.Named(name).Id;
}
