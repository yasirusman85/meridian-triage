using System.Text.Json;
using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;

namespace Meridian.Tests.Domain;

public class JobReviewTests
{
    private static readonly DateTime T0 = new(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc);

    [Fact]
    public void Approving_a_Prepared_job_with_a_category_and_contractor_makes_it_Approved_with_JobApproved_by_the_person()
    {
        var job = HeatingJobWithContractor(out var contractorId);

        job.Approve("rachel", T0.AddMinutes(5));

        Assert.Equal(JobStatus.Approved, job.Status);
        var approved = job.Events[^1];
        Assert.Equal(EventType.JobApproved, approved.Type);
        Assert.Equal("rachel", approved.Actor);
        using var payload = JsonDocument.Parse(approved.PayloadJson);
        Assert.Equal("Prepared", payload.RootElement.GetProperty("from").GetString());
        Assert.Equal("Approved", payload.RootElement.GetProperty("to").GetString());
        Assert.Equal("Heating", payload.RootElement.GetProperty("category").GetString());
        Assert.Equal(contractorId, payload.RootElement.GetProperty("contractorId").GetGuid());
    }

    [Fact]
    public void Approving_an_Unsure_job_throws_naming_what_is_missing_and_changes_nothing()
    {
        var job = NewJob();
        var count = job.Events.Count;

        var ex = Assert.Throws<JobStateException>(() => job.Approve("rachel", T0));

        Assert.Equal(new[] { Job.ApprovalNeeds.Category, Job.ApprovalNeeds.Contractor }, ex.Missing);
        Assert.Equal(JobStatus.Prepared, job.Status);
        Assert.Equal(count, job.Events.Count);
    }

    [Fact]
    public void Approving_without_a_contractor_throws_with_contractor_missing()
    {
        var job = NewJob();
        job.EditCategory(Category.Pest, "rachel", T0);

        var ex = Assert.Throws<JobStateException>(() => job.Approve("rachel", T0));

        Assert.Equal(new[] { Job.ApprovalNeeds.Contractor }, ex.Missing);
    }

    [Fact]
    public void Approving_twice_throws_and_writes_no_second_event()
    {
        var job = HeatingJobWithContractor(out _);
        job.Approve("rachel", T0);
        var count = job.Events.Count;

        Assert.Throws<JobStateException>(() => job.Approve("dawn", T0.AddMinutes(1)));
        Assert.Equal(count, job.Events.Count);
        Assert.Equal(JobStatus.Approved, job.Status);
    }

    [Fact]
    public void Rejecting_records_the_reason_and_the_person_and_the_job_takes_no_more_follow_ups()
    {
        var job = NewJob();

        job.Reject("dawn", "duplicate", T0);

        Assert.Equal(JobStatus.Rejected, job.Status);
        Assert.False(job.IsOpen);
        var rejected = job.Events[^1];
        Assert.Equal(EventType.JobRejected, rejected.Type);
        Assert.Equal("dawn", rejected.Actor);
        using var payload = JsonDocument.Parse(rejected.PayloadJson);
        Assert.Equal("duplicate", payload.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void Rejecting_needs_a_reason_and_a_Prepared_job()
    {
        var job = NewJob();
        Assert.Throws<ArgumentException>(() => job.Reject("rachel", " ", T0));
        Assert.Equal(JobStatus.Prepared, job.Status);

        var approved = HeatingJobWithContractor(out _);
        approved.Approve("rachel", T0);
        Assert.Throws<JobStateException>(() => approved.Reject("rachel", "duplicate", T0));
        Assert.Equal(JobStatus.Approved, approved.Status);
    }

    [Fact]
    public void Editing_the_category_writes_one_JobEdited_with_field_before_after_and_actor()
    {
        var job = NewJob();

        Assert.True(job.EditCategory(Category.Plumbing, "rachel", T0));

        Assert.Equal(Category.Plumbing, job.Category);
        var edited = job.Events[^1];
        Assert.Equal(EventType.JobEdited, edited.Type);
        Assert.Equal("rachel", edited.Actor);
        using var payload = JsonDocument.Parse(edited.PayloadJson);
        Assert.Equal("category", payload.RootElement.GetProperty("field").GetString());
        Assert.Equal("Unsure", payload.RootElement.GetProperty("before").GetString());
        Assert.Equal("Plumbing", payload.RootElement.GetProperty("after").GetString());
    }

    [Fact]
    public void Editing_to_the_same_value_writes_nothing()
    {
        var job = NewJob();
        var count = job.Events.Count;

        Assert.False(job.EditCategory(Category.Unsure, "rachel", T0));
        Assert.False(job.EditUrgency(Urgency.Routine, "rachel", null, T0));
        Assert.Equal(count, job.Events.Count);
    }

    [Fact]
    public void Choosing_a_contractor_sets_it_with_rule_Person_and_writes_JobEdited()
    {
        var job = NewJob();
        var contractorId = Guid.NewGuid();

        Assert.True(job.ChooseContractor(contractorId, "dawn", T0));

        Assert.Equal(contractorId, job.SuggestedContractorId);
        Assert.Equal(ContractorRule.Person, job.SuggestedContractorRule);
        using var payload = JsonDocument.Parse(job.Events[^1].PayloadJson);
        Assert.Equal("contractor", payload.RootElement.GetProperty("field").GetString());
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("before").ValueKind);
        Assert.Equal(contractorId, payload.RootElement.GetProperty("after").GetGuid());
    }

    [Fact]
    public void Editing_the_property_writes_JobEdited_with_the_ids()
    {
        var job = NewJob();
        var propertyId = Guid.NewGuid();

        Assert.True(job.EditProperty(propertyId, "rachel", T0));

        Assert.Equal(propertyId, job.PropertyId);
        using var payload = JsonDocument.Parse(job.Events[^1].PayloadJson);
        Assert.Equal("property", payload.RootElement.GetProperty("field").GetString());
        Assert.Equal(propertyId, payload.RootElement.GetProperty("after").GetGuid());
    }

    [Fact]
    public void Clearing_the_contractor_writes_JobEdited_with_the_cause()
    {
        var job = HeatingJobWithContractor(out var contractorId);

        Assert.True(job.ClearContractor("rachel", "category", T0));

        Assert.Null(job.SuggestedContractorId);
        Assert.Null(job.SuggestedContractorRule);
        using var payload = JsonDocument.Parse(job.Events[^1].PayloadJson);
        Assert.Equal(contractorId, payload.RootElement.GetProperty("before").GetGuid());
        Assert.Equal("category", payload.RootElement.GetProperty("cause").GetString());
        Assert.False(job.ClearContractor("rachel", "category", T0));
    }

    [Fact]
    public void Lowering_urgency_needs_a_reason_and_goes_through_UrgencySetByPerson()
    {
        var job = NewJob();
        job.RaiseUrgency(Urgency.Emergency, "rule:gas", T0);
        var count = job.Events.Count;

        Assert.Throws<ArgumentException>(() => job.EditUrgency(Urgency.Urgent, "rachel", null, T0));
        Assert.Equal(Urgency.Emergency, job.Urgency);
        Assert.Equal(count, job.Events.Count);

        Assert.True(job.EditUrgency(Urgency.Urgent, "rachel", "tenant confirmed no gas smell", T0));
        Assert.Equal(Urgency.Urgent, job.Urgency);
        var set = job.Events[^1];
        Assert.Equal(EventType.UrgencySetByPerson, set.Type);
        Assert.Equal("rachel", set.Actor);
        using var payload = JsonDocument.Parse(set.PayloadJson);
        Assert.Equal("tenant confirmed no gas smell", payload.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void Raising_urgency_in_review_needs_no_reason()
    {
        var job = NewJob();

        Assert.True(job.EditUrgency(Urgency.Urgent, "dawn", null, T0));

        using var payload = JsonDocument.Parse(job.Events[^1].PayloadJson);
        Assert.Equal(Job.RaisedInReviewReason, payload.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void No_edit_is_allowed_once_the_job_is_approved()
    {
        var job = HeatingJobWithContractor(out _);
        job.Approve("rachel", T0);
        var count = job.Events.Count;

        Assert.Throws<JobStateException>(() => job.EditCategory(Category.Plumbing, "rachel", T0));
        Assert.Throws<JobStateException>(() => job.EditUrgency(Urgency.Emergency, "rachel", null, T0));
        Assert.Throws<JobStateException>(() => job.ChooseContractor(Guid.NewGuid(), "rachel", T0));
        Assert.Throws<JobStateException>(() => job.EditProperty(Guid.NewGuid(), "rachel", T0));
        Assert.Throws<JobStateException>(() => job.ClearContractor("rachel", "category", T0));
        Assert.Equal(count, job.Events.Count);
        Assert.Equal(Category.Heating, job.Category);
    }

    [Theory]
    [InlineData("")]
    [InlineData("system")]
    [InlineData("SYSTEM")]
    public void Review_actions_need_a_person_not_the_system(string actor)
    {
        var job = HeatingJobWithContractor(out _);

        Assert.Throws<ArgumentException>(() => job.Approve(actor, T0));
        Assert.Throws<ArgumentException>(() => job.Reject(actor, "duplicate", T0));
        Assert.Throws<ArgumentException>(() => job.EditCategory(Category.Plumbing, actor, T0));
        Assert.Equal(JobStatus.Prepared, job.Status);
    }

    private static Job NewJob() =>
        Job.CreateFromMessage(Message.Receive("t@example.com", "text", T0), "MER-000001", T0);

    private static Job HeatingJobWithContractor(out Guid contractorId)
    {
        var job = NewJob();
        job.ApplyModelPreparation(Category.Heating, Urgency.Urgent, "No heating.", "No heating.", "en", "{}", T0);
        contractorId = Guid.NewGuid();
        job.SuggestContractor(new ContractorSuggestion(contractorId, ContractorRule.Building), T0);
        return job;
    }
}
