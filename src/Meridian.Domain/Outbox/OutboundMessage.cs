namespace Meridian.Domain.Outbox;

/// <summary>Delivery state of an <see cref="OutboundMessage"/>. The first version only simulates sending (AD-8).</summary>
public enum OutboundStatus
{
    /// <summary>Stored and shown on the job; nothing left the system.</summary>
    Simulated,
}

/// <summary>Channels an outbound message can go out on.</summary>
public static class OutboundChannels
{
    public const string Email = "email";
}

/// <summary>
/// One message written to the outbox (AD-8): an acknowledgement, notification or contractor message, with the exact
/// body that was (or would be) sent. The body always comes from a fixed template (AD-7). Never updated once written.
/// </summary>
public sealed class OutboundMessage
{
    public const int MaxChannelLength = 16;
    public const int MaxTemplateIdLength = 64;

    private OutboundMessage()
    {
        Channel = string.Empty;
        Recipient = string.Empty;
        TemplateId = string.Empty;
        Body = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>The job the message is filed under; for an acknowledgement, the first job its message touched.</summary>
    public Guid JobId { get; private set; }

    /// <summary>The incoming message this answers (acknowledgements); null for messages not caused by one.</summary>
    public Guid? MessageId { get; private set; }

    /// <summary>For example <see cref="OutboundChannels.Email"/>.</summary>
    public string Channel { get; private set; }

    public string Recipient { get; private set; }

    /// <summary>The id of the fixed template the body was rendered from, e.g. <c>acknowledgement</c>.</summary>
    public string TemplateId { get; private set; }

    public string Body { get; private set; }

    public OutboundStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static OutboundMessage Create(
        Guid jobId,
        string channel,
        string recipient,
        string templateId,
        string body,
        OutboundStatus status,
        DateTime createdAtUtc,
        Guid? messageId = null)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("A job is required.", nameof(jobId));
        }

        RequireText(channel, nameof(channel), MaxChannelLength);
        RequireText(recipient, nameof(recipient), null);
        RequireText(templateId, nameof(templateId), MaxTemplateIdLength);
        RequireText(body, nameof(body), null);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown outbound status.");
        }

        Guards.RequireUtc(createdAtUtc, nameof(createdAtUtc));
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("A message id, when given, must not be empty.", nameof(messageId));
        }

        return new OutboundMessage
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            MessageId = messageId,
            Channel = channel,
            Recipient = recipient,
            TemplateId = templateId,
            Body = body,
            Status = status,
            CreatedAtUtc = createdAtUtc,
        };
    }

    private static void RequireText(string value, string paramName, int? maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", paramName);
        }

        if (maxLength is { } max && value.Length > max)
        {
            throw new ArgumentException($"Must be at most {max} characters.", paramName);
        }
    }
}
