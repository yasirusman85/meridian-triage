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
}
