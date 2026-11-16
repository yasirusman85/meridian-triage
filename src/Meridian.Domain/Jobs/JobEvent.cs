namespace Meridian.Domain.Jobs;

/// <summary>An append-only history row for a job (AD-5). Never updated or deleted.</summary>
public sealed class JobEvent
{
    public const string SystemActor = "system";

    /// <summary>Length of the stored actor column.</summary>
    public const int MaxActorLength = 64;

    private JobEvent()
    {
        Actor = string.Empty;
        PayloadJson = "{}";
    }

    internal JobEvent(Guid jobId, EventType type, DateTime occurredAtUtc, string actor, string payloadJson)
    {
        Guards.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        Id = Guid.NewGuid();
        JobId = jobId;
        Type = type;
        OccurredAtUtc = occurredAtUtc;
        Actor = actor;
        PayloadJson = payloadJson;
    }

    public Guid Id { get; private set; }

    public Guid JobId { get; private set; }

    public EventType Type { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public string Actor { get; private set; }

    public string PayloadJson { get; private set; }
}
