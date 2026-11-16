namespace Meridian.Domain.Messages;

/// <summary>A tenant message, stored exactly as received (RULE-9). The text is never altered.</summary>
public sealed class Message
{
    private Message()
    {
        Sender = string.Empty;
        Text = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid? JobId { get; private set; }

    public string Sender { get; private set; }

    public string Text { get; private set; }

    public DateTime ReceivedAtUtc { get; private set; }

    public static Message Receive(string sender, string text, DateTime receivedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(sender))
        {
            throw new ArgumentException("Sender is required.", nameof(sender));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Message text is required.", nameof(text));
        }

        Guards.RequireUtc(receivedAtUtc, nameof(receivedAtUtc));

        return new Message
        {
            Id = Guid.NewGuid(),
            Sender = sender,
            Text = text,
            ReceivedAtUtc = receivedAtUtc,
        };
    }

    internal void AttachTo(Guid jobId) => JobId = jobId;
}
