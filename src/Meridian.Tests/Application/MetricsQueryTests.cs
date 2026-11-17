using Meridian.Application.Abstractions;
using Meridian.Application.Metrics;
using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;

namespace Meridian.Tests.Application;

public class MetricsQueryTests
{
    private static readonly DateTime T0 = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Empty_database_shows_no_data_for_every_measure()
    {
        var report = await Query(new SubmitMessageTests.FakeJobRepository(), T0).ExecuteAsync(default);

        foreach (var window in new[] { report.Last30Days, report.AllTime })
        {
            Assert.Equal(0, window.JobCount);
            Assert.Equal(0, window.FirstHumanLook.Jobs);
            Assert.Null(window.FirstHumanLook.MedianMinutes);
            Assert.Null(window.FirstHumanLook.Percentile90Minutes);
            Assert.Null(window.TimeToApproval.MedianMinutes);
            Assert.Null(window.TimeToAttendance.MedianMinutes);
            Assert.Equal(0, window.Emergency24Hours.Eligible);
            Assert.Equal(0, window.NonEmergency5WorkingDays.Eligible);
        }
    }

    [Fact]
    public async Task Counts_by_category_and_urgency_match_the_jobs()
    {
        var repo = new SubmitMessageTests.FakeJobRepository();
        repo.Add(Prepared("MER-000001", T0.AddHours(-1), Category.Heating, Urgency.Emergency));
        repo.Add(Prepared("MER-000002", T0.AddHours(-2), Category.Heating, Urgency.Routine));
        repo.Add(Prepared("MER-000003", T0.AddHours(-3), Category.Plumbing, Urgency.Routine));
        repo.Add(Prepared("MER-000004", T0.AddHours(-4), Category.Unsure, Urgency.Urgent));
        repo.Add(Prepared("MER-000005", T0.AddHours(-5), Category.Pest, Urgency.Routine));

        var window = (await Query(repo, T0).ExecuteAsync(default)).AllTime;

        Assert.Equal(5, window.JobCount);
        Assert.Equal(2, Count(window.ByCategory, "Heating"));
        Assert.Equal(1, Count(window.ByCategory, "Plumbing"));
        Assert.Equal(1, Count(window.ByCategory, "Unsure"));
        Assert.Equal(0, Count(window.ByCategory, "Electrical"));
        Assert.Equal(1, Count(window.ByUrgency, "Emergency"));
        Assert.Equal(1, Count(window.ByUrgency, "Urgent"));
        Assert.Equal(3, Count(window.ByUrgency, "Routine"));
        Assert.Equal(5, window.OpenByStatus.Sum(c => c.Count));
    }

    [Fact]
    public async Task Time_to_first_human_look_runs_from_prepared_to_the_first_person_action_not_the_system()
    {
        var repo = new SubmitMessageTests.FakeJobRepository();
        var job = Job.CreateFromMessage(Message.Receive("tenant@example.com", "no heat", T0.AddMinutes(-20)), "MER-000001", T0);
        job.ApplyModelPreparation(Category.Unsure, Urgency.Routine, "Heating.", "No heat.", "en", "{}", T0.AddMinutes(5));
        job.EditCategory(Category.Heating, "rachel", T0.AddMinutes(30));
        repo.Add(job);

        var window = (await Query(repo, T0.AddHours(1)).ExecuteAsync(default)).AllTime;

        Assert.Equal(1, window.FirstHumanLook.Jobs);
        Assert.Equal(30, window.FirstHumanLook.MedianMinutes);
        Assert.Equal(30, window.FirstHumanLook.Percentile90Minutes);
    }

    [Fact]
    public async Task A_job_first_seen_40_days_ago_is_in_all_time_but_not_the_last_30_days()
    {
        var repo = new SubmitMessageTests.FakeJobRepository();
        repo.Add(Prepared("MER-000001", T0.AddDays(-40), Category.Plumbing, Urgency.Routine));

        var report = await Query(repo, T0).ExecuteAsync(default);

        Assert.Equal(0, report.Last30Days.JobCount);
        Assert.Equal(1, report.AllTime.JobCount);
    }

    [Fact]
    public async Task An_emergency_three_hours_old_and_not_attended_is_excluded_from_the_24_hour_promise()
    {
        var repo = new SubmitMessageTests.FakeJobRepository();
        repo.Add(Prepared("MER-000001", T0.AddHours(-3), Category.Heating, Urgency.Emergency));

        var window = (await Query(repo, T0).ExecuteAsync(default)).AllTime;

        Assert.Equal(0, window.Emergency24Hours.Eligible);
        Assert.Equal(0, window.Emergency24Hours.Met);
    }

    [Fact]
    public async Task An_emergency_thirty_hours_old_and_not_attended_counts_as_a_broken_24_hour_promise()
    {
        var repo = new SubmitMessageTests.FakeJobRepository();
        repo.Add(Prepared("MER-000001", T0.AddHours(-30), Category.Heating, Urgency.Emergency));

        var window = (await Query(repo, T0).ExecuteAsync(default)).AllTime;

        Assert.Equal(1, window.Emergency24Hours.Eligible);
        Assert.Equal(0, window.Emergency24Hours.Met);
    }

    [Fact]
    public async Task An_emergency_attended_10_hours_after_the_message_meets_the_24_hour_promise_and_shows_time_to_attendance()
    {
        var repo = new SubmitMessageTests.FakeJobRepository();
        var received = T0.AddHours(-20);
        var job = Job.CreateFromMessage(Message.Receive("tenant@example.com", "no heat", received), "MER-000001", received);
        job.ApplyModelPreparation(Category.Heating, Urgency.Emergency, "No heat.", "No heat.", "en", "{}", received.AddMinutes(1));
        job.SuggestContractor(new ContractorSuggestion(Guid.NewGuid(), ContractorRule.Default), received.AddMinutes(1));
        job.Approve("rachel", received.AddMinutes(30));
        job.SendToContractor(Guid.NewGuid(), "token-1", Guid.NewGuid(), "rachel", received.AddMinutes(31));
        job.MarkAttended(received.AddHours(10));
        repo.Add(job);

        var window = (await Query(repo, T0).ExecuteAsync(default)).AllTime;

        Assert.Equal(1, window.Emergency24Hours.Eligible);
        Assert.Equal(1, window.Emergency24Hours.Met);
        Assert.Equal(1, window.TimeToAttendance.Jobs);
        Assert.Equal(600, window.TimeToAttendance.MedianMinutes);
        Assert.Equal(0, window.NonEmergency5WorkingDays.Eligible);
    }

    [Fact]
    public async Task A_non_emergency_past_its_five_working_day_promise_counts_as_broken()
    {
        var repo = new SubmitMessageTests.FakeJobRepository();
        var friday = new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);
        repo.Add(Prepared("MER-000001", friday, Category.Plumbing, Urgency.Routine));

        var notYet = (await Query(repo, friday.AddDays(2)).ExecuteAsync(default)).AllTime;
        var past = (await Query(repo, friday.AddDays(8)).ExecuteAsync(default)).AllTime;

        Assert.Equal(0, notYet.NonEmergency5WorkingDays.Eligible);
        Assert.Equal(1, past.NonEmergency5WorkingDays.Eligible);
        Assert.Equal(0, past.NonEmergency5WorkingDays.Met);
    }

    [Fact]
    public async Task Time_to_approval_runs_from_the_message_to_the_approval()
    {
        var repo = new SubmitMessageTests.FakeJobRepository();
        var job = Job.CreateFromMessage(Message.Receive("tenant@example.com", "tap", T0), "MER-000001", T0);
        job.ApplyModelPreparation(Category.Plumbing, Urgency.Routine, "Tap.", "Tap.", "en", "{}", T0);
        job.SuggestContractor(new ContractorSuggestion(Guid.NewGuid(), ContractorRule.Default), T0);
        job.Approve("rachel", T0.AddMinutes(90));
        repo.Add(job);

        var window = (await Query(repo, T0.AddHours(2)).ExecuteAsync(default)).AllTime;

        Assert.Equal(1, window.TimeToApproval.Jobs);
        Assert.Equal(90, window.TimeToApproval.MedianMinutes);
    }

    [Fact]
    public async Task Open_jobs_by_status_are_derived_from_events_and_leave_out_rejected_jobs()
    {
        var repo = new SubmitMessageTests.FakeJobRepository();
        repo.Add(Prepared("MER-000001", T0, Category.Plumbing, Urgency.Routine));
        var approved = Prepared("MER-000002", T0, Category.Plumbing, Urgency.Routine);
        approved.SuggestContractor(new ContractorSuggestion(Guid.NewGuid(), ContractorRule.Default), T0);
        approved.Approve("rachel", T0.AddMinutes(5));
        repo.Add(approved);
        var rejected = Prepared("MER-000003", T0, Category.Plumbing, Urgency.Routine);
        rejected.Reject("rachel", "duplicate", T0.AddMinutes(5));
        repo.Add(rejected);

        var window = (await Query(repo, T0.AddHours(1)).ExecuteAsync(default)).AllTime;

        Assert.Equal(1, Count(window.OpenByStatus, "Prepared"));
        Assert.Equal(1, Count(window.OpenByStatus, "Approved"));
        Assert.Equal(0, Count(window.OpenByStatus, "SentToContractor"));
    }

    [Fact]
    public void Working_days_skip_weekends_and_keep_the_time_of_day()
    {
        var friday = new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc), MetricsQuery.AddWorkingDays(friday, 5));
        Assert.Equal(new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc), MetricsQuery.AddWorkingDays(friday, 3));
    }

    [Fact]
    public void Median_and_90th_percentile_use_the_mean_of_the_middle_pair_and_nearest_rank()
    {
        var even = MetricsQuery.Measure(new double[] { 40, 10, 30, 20 });
        var single = MetricsQuery.Measure(new double[] { 5 });

        Assert.Equal(25, even.MedianMinutes);
        Assert.Equal(40, even.Percentile90Minutes);
        Assert.Equal(5, single.MedianMinutes);
        Assert.Equal(5, single.Percentile90Minutes);
    }

    private static MetricsQuery Query(IJobRepository repo, DateTime now) =>
        new(repo, new FakeClock(now));

    private static int Count(IReadOnlyList<MetricCount> counts, string name) =>
        counts.Single(c => c.Name == name).Count;

    private static Job Prepared(string reference, DateTime receivedAtUtc, Category category, Urgency urgency)
    {
        var job = Job.CreateFromMessage(
            Message.Receive("tenant@example.com", "text " + reference, receivedAtUtc),
            reference,
            receivedAtUtc);
        job.ApplyModelPreparation(category, urgency, "Explained.", "Summary.", "en", "{}", receivedAtUtc);
        return job;
    }
}
