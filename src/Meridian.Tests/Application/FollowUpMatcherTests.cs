using Meridian.Application.Triage;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;
using Meridian.Tests.Domain;

namespace Meridian.Tests.Application;

/// <summary><see cref="FollowUpMatcher"/> (AD-13): same property, same category or either Unsure, open, within 7 days.</summary>
public class FollowUpMatcherTests
{
    private static readonly DateTime Now = new(2026, 1, 16, 20, 30, 0, DateTimeKind.Utc);
    private static readonly Guid Flat = Guid.NewGuid();
    private static readonly Guid OtherFlat = Guid.NewGuid();

    [Fact]
    public void Same_property_and_category_matches()
    {
        var job = Open(Category.Heating, Now.AddHours(-1));

        Assert.Same(job, FollowUpMatcher.Match(new[] { job }, Flat, Category.Heating, Now));
    }

    [Theory]
    [InlineData(Category.Heating, Category.Unsure)]
    [InlineData(Category.Unsure, Category.Plumbing)]
    [InlineData(Category.Unsure, Category.Unsure)]
    public void Either_side_Unsure_matches(Category jobCategory, Category issueCategory)
    {
        var job = Open(jobCategory, Now.AddHours(-1));

        Assert.Same(job, FollowUpMatcher.Match(new[] { job }, Flat, issueCategory, Now));
    }

    [Fact]
    public void Different_category_at_the_same_property_does_not_match()
    {
        var job = Open(Category.Heating, Now.AddHours(-1));

        Assert.Null(FollowUpMatcher.Match(new[] { job }, Flat, Category.Plumbing, Now));
    }

    [Fact]
    public void A_different_property_does_not_match()
    {
        var job = Open(Category.Heating, Now.AddHours(-1));

        Assert.Null(FollowUpMatcher.Match(new[] { job }, OtherFlat, Category.Heating, Now));
    }

    [Fact]
    public void An_unresolved_property_never_matches()
    {
        var job = Job.CreateFromMessage(Message.Receive("a@example.com", "x", Now.AddHours(-1)), "MER-000001", Now.AddHours(-1));

        Assert.Null(FollowUpMatcher.Match(new[] { job }, null, Category.Unsure, Now));
    }

    [Fact]
    public void A_job_opened_8_days_ago_does_not_match()
    {
        var job = Open(Category.Heating, Now.AddDays(-8));

        Assert.Null(FollowUpMatcher.Match(new[] { job }, Flat, Category.Heating, Now));
    }

    [Fact]
    public void A_job_opened_exactly_7_days_ago_still_matches()
    {
        var job = Open(Category.Heating, Now.AddDays(-7));

        Assert.Same(job, FollowUpMatcher.Match(new[] { job }, Flat, Category.Heating, Now));
    }

    [Theory]
    [InlineData(JobStatus.Rejected)]
    [InlineData(JobStatus.Attended)]
    public void A_closed_job_does_not_match(JobStatus status)
    {
        var job = Open(Category.Heating, Now.AddHours(-1));
        TestJobs.SetStatus(job, status);

        Assert.Null(FollowUpMatcher.Match(new[] { job }, Flat, Category.Heating, Now));
    }

    [Theory]
    [InlineData(JobStatus.Approved)]
    [InlineData(JobStatus.SentToContractor)]
    public void An_approved_or_sent_job_is_still_open(JobStatus status)
    {
        var job = Open(Category.Heating, Now.AddHours(-1));
        TestJobs.SetStatus(job, status);

        Assert.Same(job, FollowUpMatcher.Match(new[] { job }, Flat, Category.Heating, Now));
    }

    [Fact]
    public void A_same_category_job_wins_over_an_Unsure_one_then_the_newest()
    {
        var unsure = Open(Category.Unsure, Now.AddMinutes(-10), "MER-000001");
        var olderHeating = Open(Category.Heating, Now.AddHours(-5), "MER-000002");
        var newerHeating = Open(Category.Heating, Now.AddHours(-2), "MER-000003");

        Assert.Same(newerHeating, FollowUpMatcher.Match(new[] { unsure, olderHeating, newerHeating }, Flat, Category.Heating, Now));
    }

    private static Job Open(Category category, DateTime openedAtUtc, string reference = "MER-000001")
    {
        var job = Job.CreateFromMessage(Message.Receive("a@example.com", "x", openedAtUtc), reference, openedAtUtc, Flat);
        if (category != Category.Unsure)
        {
            job.ApplyModelPreparation(category, Urgency.Routine, "why", "what", "en", "{}", openedAtUtc);
        }

        return job;
    }
}
