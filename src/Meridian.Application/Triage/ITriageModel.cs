namespace Meridian.Application.Triage;

/// <summary>
/// The one port to a model (AD-1). Only <c>Meridian.Infrastructure</c> implements it; everything else calls it.
/// </summary>
public interface ITriageModel
{
    /// <summary>The model id recorded in events (e.g. <c>stub</c>), also when a call fails without a result.</summary>
    string ModelId { get; }

    /// <summary>The prompt and contract version (e.g. <c>triage.v1</c>).</summary>
    string PromptVersion { get; }

    /// <summary>
    /// Asks the model to prepare one message. Returns the raw output unvalidated; the caller validates it
    /// with <see cref="TriageValidator"/>. May throw or be cancelled; the caller treats that as unavailable.
    /// </summary>
    Task<TriageModelResult> PrepareAsync(TriageRequest request, CancellationToken cancellationToken);
}

/// <summary>What the model is given: the message text and received time only, nothing about the sender (AD-10).</summary>
public sealed record TriageRequest(string Text, DateTime ReceivedAtUtc);

/// <summary>One model call's outcome, for validation and the event log (AD-2).</summary>
/// <param name="Succeeded">True when the model returned output (which may still fail validation).</param>
/// <param name="RawOutput">The model's raw text, unvalidated. Null when it returned nothing.</param>
/// <param name="FailureReason">Why there is no output, e.g. <c>no-recorded-response</c> or <c>unavailable</c>.</param>
public sealed record TriageModelResult(
    bool Succeeded,
    string? RawOutput,
    string ModelId,
    string PromptVersion,
    long LatencyMs,
    int? InputTokens,
    int? OutputTokens,
    string? FailureReason)
{
    public static TriageModelResult Output(
        string rawOutput,
        string modelId,
        string promptVersion,
        long latencyMs,
        int? inputTokens = null,
        int? outputTokens = null) =>
        new(true, rawOutput, modelId, promptVersion, latencyMs, inputTokens, outputTokens, null);

    public static TriageModelResult Failure(string reason, string modelId, string promptVersion, long latencyMs) =>
        new(false, null, modelId, promptVersion, latencyMs, null, null, reason);
}

/// <summary>Failure reasons written to <c>ModelPreparationFailed</c> events.</summary>
public static class TriageFailureReasons
{
    public const string Timeout = "timeout";
    public const string Unavailable = "unavailable";
    public const string NoRecordedResponse = "no-recorded-response";
    public const string InvalidOutput = "invalid-output";
    public const string Cancelled = "cancelled";
}
