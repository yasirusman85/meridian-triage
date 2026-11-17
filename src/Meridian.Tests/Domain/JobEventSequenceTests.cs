using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;

namespace Meridian.Tests.Domain;

/// <summary>Story 1.7: history order is fixed by a per-job sequence, not by timestamp.</summary>
public class JobEventSequenceTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 8, 3, 0, DateTimeKind.Utc);

    [Fact]
    public void Events_appended_at_the_same_time_get_increasing_sequence_numbers()
    {
        var message = Message.Receive("tenant@example.com", "No heating", Now);

        var job = Job.CreateFromMessage(message, "MER-000001", Now);

        Assert.Equal(new[] { 1, 2 }, job.Events.Select(e => e.Sequence));
        Assert.All(job.Events, e => Assert.Equal(Now, e.OccurredAtUtc));
        Assert.Equal(EventType.MessageReceived, job.Events[0].Type);
        Assert.Equal(EventType.JobPrepared, job.Events[1].Type);
    }

    [Fact]
    public void Sequence_numbers_are_unique_and_continue_after_later_events()
    {
        var message = Message.Receive("tenant@example.com", "No heating", Now);
        var job = Job.CreateFromMessage(message, "MER-000002", Now);

        job.RaiseUrgency(Urgency.Emergency, "rule:no-heating-winter", Now);

        var sequences = job.Events.Select(e => e.Sequence).ToList();
        Assert.Equal(sequences.Distinct().Count(), sequences.Count);
        Assert.Equal(Enumerable.Range(1, sequences.Count), sequences);
    }
}
