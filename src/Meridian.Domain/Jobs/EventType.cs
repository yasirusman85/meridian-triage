namespace Meridian.Domain.Jobs;

/// <summary>Job event types, past tense. Later stories add more.</summary>
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
}
