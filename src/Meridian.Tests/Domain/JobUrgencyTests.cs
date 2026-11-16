using System.Text.Json;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;
using Meridian.Domain.Rules;

namespace Meridian.Tests.Domain;

/// <summary><see cref="Job.ApplyRules"/> and <see cref="Job.SetUrgencyByPerson"/> (AD-4).</summary>
public class JobUrgencyTests
{
    private static readonly DateTime Now = new(2026, 1, 16, 19, 15, 0, DateTimeKind.Utc);
    private const string Payload = "{\"modelId\":\"stub\"}";

    [Fact]
    public void A_rule_hit_appends_RulesApplied_then_raises_to_Emergency_with_the_rule_as_reason()
    {
        var job = NewJob();

        job.ApplyRules(new[] { new RuleHit(EmergencyRuleIds.Gas, "gas") }, Now);

        Assert.Equal(Urgency.Emergency, job.Urgency);
        Assert.Equal(new[] { "gas" }, job.RuleIds);
        Assert.Equal(
            new[] { EventType.MessageReceived, EventType.JobPrepared, EventType.RulesApplied, EventType.UrgencyRaised },
            job.Events.Select(e => e.Type));

        using var applied = JsonDocument.Parse(job.Events[2].PayloadJson);
        var rule = Assert.Single(applied.RootElement.GetProperty("rules").EnumerateArray());
        Assert.Equal("gas", rule.GetProperty("id").GetString());
        Assert.Equal("gas", rule.GetProperty("matchedPhrase").GetString());
        Assert.Equal(JobEvent.SystemActor, job.Events[2].Actor);

        using var raised = JsonDocument.Parse(job.Events[3].PayloadJson);
        Assert.Equal("Routine", raised.RootElement.GetProperty("from").GetString());
        Assert.Equal("Emergency", raised.RootElement.GetProperty("to").GetString());
        Assert.Equal("rule:gas", raised.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void No_hit_appends_RulesApplied_with_an_empty_list_and_changes_nothing_else()
    {
        var job = NewJob();

        job.ApplyRules(Array.Empty<RuleHit>(), Now);

        Assert.Equal(Urgency.Routine, job.Urgency);
        Assert.Empty(job.RuleIds);
        Assert.Equal(EventType.RulesApplied, job.Events[^1].Type);
        using var applied = JsonDocument.Parse(job.Events[^1].PayloadJson);
        Assert.Empty(applied.RootElement.GetProperty("rules").EnumerateArray());
    }

    [Fact]
    public void Several_hits_are_all_recorded_and_raise_once()
    {
        var job = NewJob();

        job.ApplyRules(
            new[] { new RuleHit(EmergencyRuleIds.Gas, "gas"), new RuleHit(EmergencyRuleIds.NoHeatingWinter, "no heating") },
            Now);
        job.ApplyRules(new[] { new RuleHit(EmergencyRuleIds.Gas, "gaz") }, Now);

        Assert.Equal(new[] { "gas", "no-heating-winter" }, job.RuleIds);
        Assert.Single(job.Events, e => e.Type == EventType.UrgencyRaised);
        Assert.Equal(2, job.Events.Count(e => e.Type == EventType.RulesApplied));
    }

    [Fact]
    public void An_unknown_rule_id_is_rejected()
    {
        var job = NewJob();

        Assert.Throws<ArgumentException>(() => job.ApplyRules(new[] { new RuleHit("roofing", "x") }, Now));
        Assert.Throws<ArgumentException>(() => job.ApplyRules(new[] { new RuleHit("gas,water", "x") }, Now));
        Assert.DoesNotContain(job.Events, e => e.Type == EventType.RulesApplied);
    }

    [Fact]
    public void A_model_proposal_of_Routine_after_a_rule_hit_leaves_the_job_at_Emergency()
    {
        var job = NewJob();
        job.ApplyRules(new[] { new RuleHit(EmergencyRuleIds.WaterThroughCeiling, "water through the ceiling") }, Now);

        job.ApplyModelPreparation(Category.Plumbing, Urgency.Routine, "Leak.", "Leak.", "en", Payload, Now.AddSeconds(2));

        Assert.Equal(Urgency.Emergency, job.Urgency);
        Assert.Single(job.Events, e => e.Type == EventType.UrgencyRaised);
    }

    [Fact]
    public void A_person_can_lower_urgency_and_the_event_records_who_and_why()
    {
        var job = NewJob();
        job.ApplyRules(new[] { new RuleHit(EmergencyRuleIds.Gas, "gas meter") }, Now);

        job.SetUrgencyByPerson(Urgency.Urgent, "Rachel", "Meter cupboard door only, no leak.", Now.AddMinutes(5));

        Assert.Equal(Urgency.Urgent, job.Urgency);
        var set = job.Events[^1];
        Assert.Equal(EventType.UrgencySetByPerson, set.Type);
        Assert.Equal("Rachel", set.Actor);
        Assert.Equal(Now.AddMinutes(5), set.OccurredAtUtc);
        using var payload = JsonDocument.Parse(set.PayloadJson);
        Assert.Equal("Emergency", payload.RootElement.GetProperty("from").GetString());
        Assert.Equal("Urgent", payload.RootElement.GetProperty("to").GetString());
        Assert.Equal("Meter cupboard door only, no leak.", payload.RootElement.GetProperty("reason").GetString());
        // The fired rules stay on the job: they are history, not the current urgency.
        Assert.Equal(new[] { "gas" }, job.RuleIds);
    }

    [Fact]
    public void A_person_can_also_raise_urgency()
    {
        var job = NewJob();

        job.SetUrgencyByPerson(Urgency.Emergency, "Rachel", "Tenant is elderly and alone.", Now);

        Assert.Equal(Urgency.Emergency, job.Urgency);
        Assert.Equal(EventType.UrgencySetByPerson, job.Events[^1].Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_reason_is_required_to_set_urgency(string? reason)
    {
        var job = NewJob();
        job.RaiseUrgency(Urgency.Emergency, "test", Now);
        var count = job.Events.Count;

        Assert.Throws<ArgumentException>(() => job.SetUrgencyByPerson(Urgency.Urgent, "Rachel", reason!, Now));

        Assert.Equal(Urgency.Emergency, job.Urgency);
        Assert.Equal(count, job.Events.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("system")]
    [InlineData(" System ")]
    public void A_person_is_required_to_set_urgency(string? actor)
    {
        var job = NewJob();

        Assert.Throws<ArgumentException>(() => job.SetUrgencyByPerson(Urgency.Urgent, actor!, "reason", Now));
        Assert.Equal(Urgency.Routine, job.Urgency);
    }

    [Fact]
    public void SetUrgencyByPerson_rejects_an_overlong_actor_an_unknown_level_and_local_time()
    {
        var job = NewJob();

        Assert.Throws<ArgumentException>(() => job.SetUrgencyByPerson(Urgency.Urgent, new string('r', 65), "reason", Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => job.SetUrgencyByPerson((Urgency)7, "Rachel", "reason", Now));
        Assert.Throws<ArgumentException>(() =>
            job.SetUrgencyByPerson(Urgency.Urgent, "Rachel", "reason", DateTime.SpecifyKind(Now, DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(() => job.ApplyRules(Array.Empty<RuleHit>(), DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    private static Job NewJob() =>
        Job.CreateFromMessage(Message.Receive("tenant@example.com", "text", Now), "MER-000001", Now);
}
