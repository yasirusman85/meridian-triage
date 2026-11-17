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

    internal JobEvent(Guid jobId, EventType type, DateTime occurredAtUtc, string actor, string payloadJson, int sequence)
    {
        Guards.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Sequence starts at 1.");
        }

        Id = Guid.NewGuid();
        JobId = jobId;
        Type = type;
        OccurredAtUtc = occurredAtUtc;
        Actor = actor;
        PayloadJson = payloadJson;
        Sequence = sequence;
    }

    public Guid Id { get; private set; }

    public Guid JobId { get; private set; }

    public EventType Type { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public string Actor { get; private set; }

    public string PayloadJson { get; private set; }

    /// <summary>
    /// Position in the job's history: 1, 2, 3… in append order, unique per job, set when the event is appended and
    /// never changed. History is ordered by it, since events written in one save share a timestamp.
    /// </summary>
    public int Sequence { get; private set; }
}
