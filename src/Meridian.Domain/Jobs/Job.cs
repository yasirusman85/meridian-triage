using System.Text.Json;
using Meridian.Domain.Messages;

namespace Meridian.Domain.Jobs;

/// <summary>
/// One unit of maintenance work. State changes only through methods on this class,
/// and every change appends a <see cref="JobEvent"/> (AD-5).
/// </summary>
public sealed class Job
{
    private readonly List<Message> _messages = new();
    private readonly List<JobEvent> _events = new();

    private Job()
    {
        Reference = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Reference { get; private set; }

    public JobStatus Status { get; private set; }

    public Urgency Urgency { get; private set; }

    public Category Category { get; private set; }

    /// <summary>When the job's first message was received (UTC).</summary>
    public DateTime ReceivedAtUtc { get; private set; }

    /// <summary>When the job was opened (UTC).</summary>
    public DateTime CreatedAtUtc { get; private set; }

    public IReadOnlyList<Message> Messages => _messages;

    public IReadOnlyList<JobEvent> Events => _events;

    /// <summary>
    /// Creates the placeholder job for a freshly received message: status Prepared, category Unsure,
    /// urgency Routine. Appends <see cref="EventType.MessageReceived"/> then <see cref="EventType.JobPrepared"/>.
    /// </summary>
    /// <param name="message">The received message; it becomes the job's first message.</param>
    /// <param name="reference">The job reference, e.g. <c>MER-000001</c>.</param>
    /// <param name="nowUtc">The current time from the application's clock port (AD-9).</param>
    public static Job CreateFromMessage(Message message, string reference, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith(JobReference.Prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Reference must start with " + JobReference.Prefix + ".", nameof(reference));
        }

        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        if (message.JobId is not null)
        {
            throw new InvalidOperationException("Message already belongs to a job.");
        }

        var job = new Job
        {
            Id = Guid.NewGuid(),
            Reference = reference,
            Status = JobStatus.Prepared,
            Urgency = Urgency.Routine,
            Category = Category.Unsure,
            ReceivedAtUtc = message.ReceivedAtUtc,
            CreatedAtUtc = nowUtc,
        };

        message.AttachTo(job.Id);
        job._messages.Add(message);

        job.Append(
            EventType.MessageReceived,
            message.ReceivedAtUtc,
            JobEvent.SystemActor,
            new { messageId = message.Id });

        job.Append(
            EventType.JobPrepared,
            nowUtc,
            JobEvent.SystemActor,
            new
            {
                reference = job.Reference,
                status = job.Status.ToString(),
                urgency = job.Urgency.ToString(),
                category = job.Category.ToString(),
            });

        return job;
    }

    private void Append(EventType type, DateTime occurredAtUtc, string actor, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        _events.Add(new JobEvent(Id, type, occurredAtUtc, actor, json));
    }
}
