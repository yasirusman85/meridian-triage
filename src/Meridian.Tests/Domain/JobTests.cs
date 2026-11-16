using System.Text.Json;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;

namespace Meridian.Tests.Domain;

public class JobTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 8, 3, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateFromMessage_makes_a_prepared_unsure_routine_job()
    {
        var message = Message.Receive("a.nowak@example.com", "Dzień dobry", Now);

        var job = Job.CreateFromMessage(message, "MER-000001", Now);

        Assert.Equal(JobStatus.Prepared, job.Status);
        Assert.Equal(Category.Unsure, job.Category);
        Assert.Equal(Urgency.Routine, job.Urgency);
        Assert.Equal("MER-000001", job.Reference);
        Assert.Equal(Now, job.ReceivedAtUtc);
        Assert.Same(message, Assert.Single(job.Messages));
        Assert.Null(job.PropertyId);
        Assert.Equal(0, job.FollowUpCount);
    }

    [Fact]
    public void CreateFromMessage_appends_MessageReceived_then_JobPrepared_by_system_in_utc()
    {
        var message = Message.Receive("tenant@example.com", "Tap dripping", Now);

        var job = Job.CreateFromMessage(message, "MER-000002", Now);

        Assert.Collection(
            job.Events,
            e =>
            {
                Assert.Equal(EventType.MessageReceived, e.Type);
                Assert.Equal(JobEvent.SystemActor, e.Actor);
                Assert.Equal(DateTimeKind.Utc, e.OccurredAtUtc.Kind);
                Assert.Equal(job.Id, e.JobId);
                using var payload = JsonDocument.Parse(e.PayloadJson);
                Assert.Equal(message.Id, payload.RootElement.GetProperty("messageId").GetGuid());
            },
            e =>
            {
                Assert.Equal(EventType.JobPrepared, e.Type);
                Assert.Equal(JobEvent.SystemActor, e.Actor);
                Assert.Equal(DateTimeKind.Utc, e.OccurredAtUtc.Kind);
            });
    }

    [Fact]
    public void CreateFromMessage_rejects_a_non_utc_time()
    {
        var message = Message.Receive("tenant@example.com", "Tap dripping", Now);

        Assert.Throws<ArgumentException>(() =>
            Job.CreateFromMessage(message, "MER-000003", DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    [Fact]
    public void One_message_can_start_several_jobs()
    {
        var message = Message.Receive("joy@example.com", "Tap and bulb", Now);

        var first = Job.CreateFromMessage(message, "MER-000004", Now);
        var second = Job.CreateFromMessage(message, "MER-000005", Now);

        Assert.Same(message, Assert.Single(first.Messages));
        Assert.Same(message, Assert.Single(second.Messages));
    }

    [Fact]
    public void Message_keeps_text_exactly_as_given()
    {
        const string text = "  Pleśń na ścianie\r\n\tw sypialni  ";

        var message = Message.Receive("a.nowak@example.com", text, Now);

        Assert.Equal(text, message.Text);
    }

    [Theory]
    [InlineData(1, "MER-000001")]
    [InlineData(123, "MER-000123")]
    [InlineData(999999, "MER-999999")]
    public void JobReference_is_MER_plus_six_digits(long number, string expected)
    {
        Assert.Equal(expected, JobReference.FromNumber(number));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_000)]
    public void JobReference_rejects_numbers_outside_six_digits(long number)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobReference.FromNumber(number));
    }
}
