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

    /// <summary>Why the job was prepared this way, from the model's proposal (R7). Null until prepared.</summary>
    public string? Explanation { get; private set; }

    /// <summary>English summary of the issue, from the model's proposal. Null until prepared.</summary>
    public string? SummaryEn { get; private set; }

    /// <summary>Language of the tenant's message as reported by the model (e.g. <c>pl</c>). Null until prepared.</summary>
    public string? Language { get; private set; }

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

    /// <summary>True once a <see cref="EventType.ModelPrepared"/> or <see cref="EventType.ModelPreparationFailed"/> event exists.</summary>
    public bool HasModelOutcome =>
        _events.Any(e => e.Type is EventType.ModelPrepared or EventType.ModelPreparationFailed);

    /// <summary>
    /// Applies a validated model proposal: sets category, explanation, English summary and language, appends
    /// <see cref="EventType.ModelPrepared"/> with <paramref name="payloadJson"/>, then raises urgency to the
    /// proposal (never lowers it, AD-4).
    /// </summary>
    /// <param name="payloadJson">Event payload with the model metadata (model id, prompt version, latency, raw output, attempts).</param>
    public void ApplyModelPreparation(
        Category category,
        Urgency urgency,
        string explanation,
        string summaryEn,
        string language,
        string payloadJson,
        DateTime nowUtc)
    {
        RequireNoModelOutcome();
        if (string.IsNullOrWhiteSpace(explanation))
        {
            throw new ArgumentException("Explanation is required.", nameof(explanation));
        }

        if (string.IsNullOrWhiteSpace(summaryEn))
        {
            throw new ArgumentException("English summary is required.", nameof(summaryEn));
        }

        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("Language is required.", nameof(language));
        }

        RequireJsonObject(payloadJson, nameof(payloadJson));
        Guards.RequireUtc(nowUtc, nameof(nowUtc));

        Category = category;
        Explanation = explanation;
        SummaryEn = summaryEn;
        Language = language;
        AppendRaw(EventType.ModelPrepared, nowUtc, JobEvent.SystemActor, payloadJson);
        RaiseUrgency(urgency, "model", nowUtc);
    }

    /// <summary>
    /// Records that the model gave no usable proposal. The job keeps category Unsure and its urgency (RULE-11).
    /// </summary>
    /// <param name="payloadJson">Event payload with the failure reason, model metadata and validation errors.</param>
    public void RecordModelFailure(string payloadJson, DateTime nowUtc)
    {
        RequireNoModelOutcome();
        RequireJsonObject(payloadJson, nameof(payloadJson));
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        AppendRaw(EventType.ModelPreparationFailed, nowUtc, JobEvent.SystemActor, payloadJson);
    }

    /// <summary>
    /// The only automatic urgency change (AD-4): raises to <paramref name="level"/> and appends
    /// <see cref="EventType.UrgencyRaised"/>; a value lower than or equal to the current urgency is ignored.
    /// </summary>
    /// <returns>True when the urgency changed.</returns>
    public bool RaiseUrgency(Urgency level, string reason, DateTime nowUtc)
    {
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown urgency.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required.", nameof(reason));
        }

        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        if (level <= Urgency)
        {
            return false;
        }

        var from = Urgency;
        Urgency = level;
        Append(
            EventType.UrgencyRaised,
            nowUtc,
            JobEvent.SystemActor,
            new { from = from.ToString(), to = level.ToString(), reason });
        return true;
    }

    private void RequireNoModelOutcome()
    {
        if (HasModelOutcome)
        {
            throw new InvalidOperationException("The model outcome for this job is already recorded.");
        }
    }

    private static void RequireJsonObject(string payloadJson, string paramName)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson ?? string.Empty);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Payload must be a JSON object.", paramName);
            }
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Payload must be valid JSON.", paramName, ex);
        }
    }

    private void Append(EventType type, DateTime occurredAtUtc, string actor, object payload) =>
        AppendRaw(type, occurredAtUtc, actor, JsonSerializer.Serialize(payload));

    private void AppendRaw(EventType type, DateTime occurredAtUtc, string actor, string payloadJson) =>
        _events.Add(new JobEvent(Id, type, occurredAtUtc, actor, payloadJson));
}
