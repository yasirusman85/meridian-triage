namespace Meridian.Domain.Jobs;

/// <summary>Job event types, past tense.</summary>
public enum EventType
{
    MessageReceived,
    JobPrepared,

    /// <summary>The model's proposal was validated and applied to the job.</summary>
    ModelPrepared,

    /// <summary>The model gave no usable proposal; the job stays Unsure (RULE-11, R10).</summary>
    ModelPreparationFailed,

    /// <summary>The job's urgency went up (AD-4).</summary>
    UrgencyRaised,

    /// <summary>The emergency rules ran on a message (R4); the payload lists the rules that fired, possibly none.</summary>
    RulesApplied,

    /// <summary>A person set the job's urgency, up or down, with a reason (AD-4). The only way urgency goes down.</summary>
    UrgencySetByPerson,

    /// <summary>A later message was attached to this open job as a follow-up (R8, AD-13); it may raise urgency.</summary>
    FollowUpAttached,

    /// <summary>A fixed acknowledgement (or the gas safety message) was written to the outbox for the tenant (R16).</summary>
    AcknowledgementSent,

    /// <summary>Fixed code suggested the usual contractor for the job's category and building (R12, AD-3).</summary>
    ContractorSuggested,

    /// <summary>A person approved the prepared job (AD-6: Prepared → Approved); the payload has the category, urgency and contractor.</summary>
    JobApproved,

    /// <summary>A person rejected the prepared job with a reason (AD-6: Prepared → Rejected).</summary>
    JobRejected,

    /// <summary>A person changed one field of a prepared job; the payload has the field, the value before and after.</summary>
    JobEdited,

    /// <summary>A staff member was notified of the emergency (R18, R19); the payload names the recipient and the outbound id.</summary>
    EscalationNotified,

    /// <summary>A staff member acknowledged the emergency; the event actor is who acknowledged it.</summary>
    EscalationAcknowledged,

    /// <summary>Neither the primary nor the backup acknowledged the emergency in time.</summary>
    EscalationFailed,

    /// <summary>The job was sent to its contractor with a single-job link (AD-6, AD-11); the payload has the contractor and the outbound id.</summary>
    SentToContractor,

    /// <summary>The contractor marked the job attended through its link; the actor is <c>contractor:&lt;id&gt;</c>.</summary>
    JobAttended,

    /// <summary>An Emergency job was not attended within 24 hours of its first message (RULE-8); flagged once per send.</summary>
    AttendanceOverdue,
}
