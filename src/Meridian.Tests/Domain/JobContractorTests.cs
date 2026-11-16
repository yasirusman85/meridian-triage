using System.Text.Json;
using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;

namespace Meridian.Tests.Domain;

public class JobContractorTests
{
    private static readonly DateTime T0 = new(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc);

    [Fact]
    public void Suggesting_a_contractor_sets_it_and_appends_ContractorSuggested_with_the_id_and_rule()
    {
        var job = HeatingJob();
        var contractorId = Guid.NewGuid();

        var changed = job.SuggestContractor(new ContractorSuggestion(contractorId, ContractorRule.Building), T0);

        Assert.True(changed);
        Assert.Equal(contractorId, job.SuggestedContractorId);
        Assert.Equal(ContractorRule.Building, job.SuggestedContractorRule);
        var suggested = job.Events[^1];
        Assert.Equal(EventType.ContractorSuggested, suggested.Type);
        Assert.Equal(JobEvent.SystemActor, suggested.Actor);
        using var payload = JsonDocument.Parse(suggested.PayloadJson);
        Assert.Equal(contractorId, payload.RootElement.GetProperty("contractorId").GetGuid());
        Assert.Equal("building", payload.RootElement.GetProperty("rule").GetString());
    }

    [Fact]
    public void The_same_suggestion_again_writes_no_event_and_a_different_one_does()
    {
        var job = HeatingJob();
        var first = new ContractorSuggestion(Guid.NewGuid(), ContractorRule.Default);
        job.SuggestContractor(first, T0);
        var count = job.Events.Count;

        Assert.False(job.SuggestContractor(first, T0.AddMinutes(1)));
        Assert.Equal(count, job.Events.Count);

        var second = new ContractorSuggestion(Guid.NewGuid(), ContractorRule.Building);
        Assert.True(job.SuggestContractor(second, T0.AddMinutes(2)));
        Assert.Equal(second.ContractorId, job.SuggestedContractorId);
        Assert.Equal(count + 1, job.Events.Count);
    }

    [Fact]
    public void A_new_job_has_no_suggested_contractor()
    {
        var job = Job.CreateFromMessage(Message.Receive("t@example.com", "text", T0), "MER-000001", T0);

        Assert.Null(job.SuggestedContractorId);
        Assert.Null(job.SuggestedContractorRule);
    }

    [Fact]
    public void An_Unsure_job_cannot_get_a_suggestion()
    {
        var job = Job.CreateFromMessage(Message.Receive("t@example.com", "text", T0), "MER-000001", T0);

        Assert.Throws<InvalidOperationException>(
            () => job.SuggestContractor(new ContractorSuggestion(Guid.NewGuid(), ContractorRule.Default), T0));
        Assert.DoesNotContain(job.Events, e => e.Type == EventType.ContractorSuggested);
    }

    [Fact]
    public void An_empty_contractor_id_or_a_local_time_is_rejected()
    {
        var job = HeatingJob();

        Assert.Throws<ArgumentException>(() => job.SuggestContractor(new ContractorSuggestion(Guid.Empty, ContractorRule.Default), T0));
        Assert.Throws<ArgumentException>(() => job.SuggestContractor(
            new ContractorSuggestion(Guid.NewGuid(), ContractorRule.Default), DateTime.SpecifyKind(T0, DateTimeKind.Local)));
        Assert.Null(job.SuggestedContractorId);
    }

    private static Job HeatingJob()
    {
        var job = Job.CreateFromMessage(Message.Receive("t@example.com", "no heating", T0), "MER-000001", T0);
        job.ApplyModelPreparation(Category.Heating, Urgency.Urgent, "No heating.", "No heating.", "en", "{}", T0);
        return job;
    }
}
