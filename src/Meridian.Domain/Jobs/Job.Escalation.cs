namespace Meridian.Domain.Jobs;

/// <summary>
/// Staff escalation of an Emergency job (AD-6, R18, R19): <see cref="EscalationState"/> is a separate field from the
/// lifecycle status. Each step appends its event (AD-5). Which step is due, and when, is decided by the application's
/// escalation service from the clock (AD-9); these methods only guard the transitions.
/// </summary>
public sealed partial class Job
{
    public EscalationState EscalationState { get; private set; }

    /// <summary>When the primary contact was notified (UTC); null until then.</summary>
    public DateTime? EscalationPrimaryNotifiedAtUtc { get; private set; }

    /// <summary>When the backup was notified (UTC); null until then.</summary>
    public DateTime? EscalationBackupNotifiedAtUtc { get; private set; }

    /// <summary>When a staff member acknowledged the emergency (UTC); null until then.</summary>
    public DateTime? EscalationAcknowledgedAtUtc { get; private set; }

    /// <summary>Who acknowledged the emergency (e.g. <c>rachel</c>); null until then.</summary>
    public string? EscalationAcknowledgedBy { get; private set; }

    /// <summary>When escalation was marked failed (UTC); null unless it failed.</summary>
    public DateTime? EscalationFailedAtUtc { get; private set; }

    /// <summary>True while the job is an open Emergency whose escalation has not been acknowledged or failed.</summary>
    public bool IsEscalating =>
        Urgency == Urgency.Emergency
        && IsOpen
        && EscalationState is EscalationState.None or EscalationState.NotifiedRachel or EscalationState.NotifiedBackup;

    /// <summary>True when a staff member can acknowledge the emergency now (see <see cref="AcknowledgeEscalation"/>).</summary>
    public bool CanAcknowledgeEscalation =>
        Urgency == Urgency.Emergency && EscalationState is not EscalationState.Acknowledged;

    /// <summary>
    /// Records the next notification of an escalating emergency: from <see cref="EscalationState.None"/> the primary
    /// contact (state <see cref="EscalationState.NotifiedRachel"/>), from <see cref="EscalationState.NotifiedRachel"/>
    /// the backup (state <see cref="EscalationState.NotifiedBackup"/>). Appends <see cref="EventType.EscalationNotified"/>
    /// with the recipient, the step and the outbound message id.
    /// </summary>
    /// <param name="recipient">The staff member notified, e.g. <c>rachel</c>.</param>
    /// <param name="outboundId">The outbound message written for the notification (AD-8).</param>
    public void NotifyEscalation(string recipient, Guid outboundId, DateTime nowUtc)
    {
        RequireStaff(recipient, nameof(recipient));
        if (outboundId == Guid.Empty)
        {
            throw new ArgumentException("An outbound message id is required.", nameof(outboundId));
        }

        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        if (!IsEscalating)
        {
            throw new InvalidOperationException($"Job {Reference} is not an escalating emergency.");
        }

        string step;
        switch (EscalationState)
        {
            case EscalationState.None:
                EscalationState = EscalationState.NotifiedRachel;
                EscalationPrimaryNotifiedAtUtc = nowUtc;
                step = "primary";
                break;
            case EscalationState.NotifiedRachel:
                EscalationState = EscalationState.NotifiedBackup;
                EscalationBackupNotifiedAtUtc = nowUtc;
                step = "backup";
                break;
            default:
                throw new InvalidOperationException($"Job {Reference} has already notified the backup.");
        }

        Append(
            EventType.EscalationNotified,
            nowUtc,
            JobEvent.SystemActor,
            new { recipient, step, outboundId, state = EscalationState.ToString() });
    }

    /// <summary>
    /// A staff member acknowledges the emergency: state <see cref="EscalationState.Acknowledged"/> and
    /// <see cref="EventType.EscalationAcknowledged"/> with the actor. Allowed on an Emergency job that is not already
    /// acknowledged: from either notified state, and also before the first notification or after escalation failed.
    /// </summary>
    /// <param name="actor">The acting staff member, e.g. <c>rachel</c> or <c>dawn</c>.</param>
    public void AcknowledgeEscalation(string actor, DateTime nowUtc)
    {
        RequireStaff(actor, nameof(actor));
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        if (Urgency != Urgency.Emergency)
        {
            throw new InvalidOperationException($"Job {Reference} is not an emergency.");
        }

        if (EscalationState == EscalationState.Acknowledged)
        {
            throw new InvalidOperationException($"Job {Reference} is already acknowledged.");
        }

        var from = EscalationState;
        EscalationState = EscalationState.Acknowledged;
        EscalationAcknowledgedAtUtc = nowUtc;
        EscalationAcknowledgedBy = actor;
        Append(
            EventType.EscalationAcknowledged,
            nowUtc,
            actor,
            new { from = from.ToString(), state = EscalationState.ToString() });
    }

    /// <summary>
    /// Marks the escalation failed after the backup did not acknowledge: only from
    /// <see cref="EscalationState.NotifiedBackup"/>. Appends <see cref="EventType.EscalationFailed"/>.
    /// </summary>
    public void FailEscalation(DateTime nowUtc)
    {
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        if (!IsEscalating || EscalationState != EscalationState.NotifiedBackup)
        {
            throw new InvalidOperationException($"Job {Reference} has not notified its backup.");
        }

        EscalationState = EscalationState.Failed;
        EscalationFailedAtUtc = nowUtc;
        Append(
            EventType.EscalationFailed,
            nowUtc,
            JobEvent.SystemActor,
            new { state = EscalationState.ToString() });
    }

    private static void RequireStaff(string person, string paramName)
    {
        if (string.IsNullOrWhiteSpace(person) || string.Equals(person.Trim(), JobEvent.SystemActor, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A staff member is required.", paramName);
        }

        if (person.Length > JobEvent.MaxActorLength)
        {
            throw new ArgumentException($"Must be at most {JobEvent.MaxActorLength} characters.", paramName);
        }
    }
}
