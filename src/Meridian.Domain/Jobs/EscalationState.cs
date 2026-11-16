namespace Meridian.Domain.Jobs;

/// <summary>
/// How far an Emergency job's staff escalation has gone (AD-6, R18, R19). A separate field from the lifecycle
/// <see cref="JobStatus"/>: <c>None → NotifiedRachel → NotifiedBackup → Failed</c>, and <c>Acknowledged</c> once a
/// staff member has taken it.
/// </summary>
public enum EscalationState
{
    /// <summary>Nobody has been notified (not an emergency, or the worker has not ticked yet).</summary>
    None,

    /// <summary>The primary contact (Rachel) was notified and has not acknowledged.</summary>
    NotifiedRachel,

    /// <summary>The primary did not acknowledge within the window; the backup (Dawn) was notified.</summary>
    NotifiedBackup,

    /// <summary>A staff member acknowledged the emergency; escalation stops.</summary>
    Acknowledged,

    /// <summary>Neither the primary nor the backup acknowledged in time. The job sorts to the top of the queue.</summary>
    Failed,
}
