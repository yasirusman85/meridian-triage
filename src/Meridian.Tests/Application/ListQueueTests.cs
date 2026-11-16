using Meridian.Application.Queue;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;

namespace Meridian.Tests.Application;

public class ListQueueTests
{
    private static readonly DateTime T0 = new(2026, 11, 11, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Jobs_are_listed_oldest_first_when_urgency_is_equal()
    {
        var repo = new SubmitMessageTests.FakeJobRepository();
        repo.Add(Make("MER-000002", T0.AddMinutes(5)));
        repo.Add(Make("MER-000001", T0));
        repo.Add(Make("MER-000003", T0.AddMinutes(10)));

        var items = await new ListQueue(repo).ExecuteAsync(default);

        Assert.Equal(new[] { "MER-000001", "MER-000002", "MER-000003" }, items.Select(i => i.Reference));
    }

    [Fact]
    public void Preview_keeps_short_text_and_cuts_long_text_at_120_characters()
    {
        Assert.Equal("short", ListQueue.Preview("short"));
        var longText = new string('ś', 500);
        Assert.Equal(120, ListQueue.Preview(longText).Length);
    }

    [Fact]
    public void Preview_does_not_split_a_surrogate_pair()
    {
        var text = new string('a', 119) + "\U0001F600" + "tail";

        var preview = ListQueue.Preview(text);

        Assert.Equal(new string('a', 119), preview);
    }

    private static Job Make(string reference, DateTime receivedAtUtc) =>
        Job.CreateFromMessage(Message.Receive("tenant@example.com", "text " + reference, receivedAtUtc), reference, receivedAtUtc);
}
