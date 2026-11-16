using System.Diagnostics;
using System.Text.Json;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;

namespace Meridian.Application.Triage;

/// <summary>Settings for <see cref="PrepareJob"/>. Defaults are the product's (AD-3: 20 s).</summary>
public sealed class PrepareJobOptions
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Budget for the whole preparation step, both attempts included.</summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;
}

/// <summary>
/// The model step's result for one message (AD-2): the validated answer, or why there is none, with every attempt
/// for the event log. <see cref="PrepareJob"/> records it on each job the message makes.
/// </summary>
public sealed class ModelPreparation
{
    internal ModelPreparation(
        TriageResponse? response,
        string? failureReason,
        IReadOnlyList<object> attempts,
        string modelId,
        string promptVersion,
        long totalLatencyMs)
    {
        Response = response;
        FailureReason = failureReason;
        AttemptRecords = attempts;
        ModelId = modelId;
        PromptVersion = promptVersion;
        TotalLatencyMs = totalLatencyMs;
    }

    /// <summary>The validated answer; null when the model failed (RULE-11, R10).</summary>
    public TriageResponse? Response { get; }

    public bool Prepared => Response is not null;

    public string? FailureReason { get; }

    public int Attempts => AttemptRecords.Count;

    public string ModelId { get; }

    public string PromptVersion { get; }

    public long TotalLatencyMs { get; }

    internal IReadOnlyList<object> AttemptRecords { get; }

    /// <summary>The model outcome event payload: model metadata, every attempt, and what was applied to the job.</summary>
    public string Payload(object? applied) => JsonSerializer.Serialize(new
    {
        modelId = ModelId,
        promptVersion = PromptVersion,
        attemptCount = Attempts,
        latencyMs = TotalLatencyMs,
        reason = FailureReason,
        applied,
        attempts = AttemptRecords,
    });
}

/// <summary>
/// The model preparation step of the pipeline (AD-3): asks <see cref="ITriageModel"/> about a stored message and
/// validates the answer against <c>triage.v1</c> with one retry for invalid output. Never throws for a model problem.
/// Deterministic code then turns the answer into jobs (<see cref="Meridian.Application.Messages.SubmitMessage"/>); the <c>Record*</c>
/// methods write the outcome on a job, so every job ends with exactly one <c>ModelPrepared</c> or
/// <c>ModelPreparationFailed</c> event.
/// </summary>
public sealed class PrepareJob
{
    public const int MaxAttempts = 2;

    private readonly ITriageModel _model;
    private readonly TimeSpan _timeout;

    public PrepareJob(ITriageModel model, PrepareJobOptions? options = null)
    {
        _model = model;
        _timeout = (options ?? new PrepareJobOptions()).Timeout;
    }

    /// <summary>Asks the model about <paramref name="message"/>: its text and received time only (AD-10).</summary>
    public async Task<ModelPreparation> AskAsync(Message message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var run = await AskModelAsync(new TriageRequest(message.Text, message.ReceivedAtUtc), cancellationToken);
        var last = run.Attempts.LastOrDefault();
        return new ModelPreparation(
            run.Response,
            run.FailureReason,
            run.Attempts.Select(a => (object)new
            {
                attempt = a.Attempt,
                modelId = a.ModelId ?? _model.ModelId,
                promptVersion = a.PromptVersion ?? _model.PromptVersion,
                latencyMs = a.LatencyMs,
                inputTokens = a.InputTokens,
                outputTokens = a.OutputTokens,
                rawOutput = a.RawOutput,
                failureReason = a.FailureReason,
                exceptionType = a.ExceptionType,
                validationErrors = a.ValidationErrors,
            }).ToList(),
            last?.ModelId ?? _model.ModelId,
            last?.PromptVersion ?? _model.PromptVersion,
            run.TotalLatencyMs);
    }

    /// <summary>
    /// The category a job gets for <paramref name="issue"/>: Unsure when the model is not confident or the
    /// property is not resolved (AD-13), otherwise the proposed category.
    /// </summary>
    public static Category CategoryFor(TriageIssue issue, PropertyMatch property) =>
        issue.Confidence == TriageConfidence.Low || !property.IsResolved ? Category.Unsure : issue.Category;

    /// <summary>Applies one issue of a valid answer to a new job made for it.</summary>
    public static void RecordIssue(Job job, ModelPreparation preparation, int issueIndex, PropertyMatch property, DateTime nowUtc)
    {
        var response = preparation.Response ?? throw new InvalidOperationException("The model gave no answer.");
        var issue = response.Issues[issueIndex];
        var category = CategoryFor(issue, property);
        var applied = new
        {
            category = category.ToString(),
            proposedCategory = issue.Category.ToString(),
            proposedUrgency = issue.Urgency.ToString(),
            confidence = issue.Confidence.ToString().ToLowerInvariant(),
            issueCount = response.Issues.Count,
            issueIndex,
            propertyText = issue.PropertyText,
            propertyResolution = property.Resolution.ToString(),
            propertyId = property.Property?.Id,
        };

        job.ApplyModelPreparation(
            category,
            issue.Urgency,
            issue.Explanation,
            issue.SummaryEn,
            response.Language,
            preparation.Payload(applied),
            nowUtc);
    }

    /// <summary>Records a valid answer with no issues: the job stays Unsure and is flagged for a person.</summary>
    public static void RecordNotMaintenance(Job job, ModelPreparation preparation, DateTime nowUtc)
    {
        var response = preparation.Response ?? throw new InvalidOperationException("The model gave no answer.");
        var applied = new { category = Category.Unsure.ToString(), issueCount = 0, notMaintenance = true };
        job.ApplyNotMaintenance(response.Language, preparation.Payload(applied), nowUtc);
    }

    /// <summary>Records a model failure: the job stays Unsure with its urgency (RULE-11).</summary>
    public static void RecordFailure(Job job, ModelPreparation preparation, DateTime nowUtc) =>
        job.RecordModelFailure(preparation.Payload(applied: null), nowUtc);

    private async Task<ModelRun> AskModelAsync(TriageRequest request, CancellationToken cancellationToken)
    {
        var attempts = new List<AttemptRecord>();
        var total = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var watch = Stopwatch.StartNew();
            TriageModelResult result;
            try
            {
                // WaitAsync enforces the budget even if an adapter ignores the token.
                result = await _model.PrepareAsync(request, linked.Token).WaitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                attempts.Add(AttemptRecord.Failed(attempt, watch.ElapsedMilliseconds, TriageFailureReasons.Cancelled));
                return ModelRun.Failed(attempts, TriageFailureReasons.Cancelled, total.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                attempts.Add(AttemptRecord.Failed(attempt, watch.ElapsedMilliseconds, TriageFailureReasons.Timeout));
                return ModelRun.Failed(attempts, TriageFailureReasons.Timeout, total.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                // Only the exception type is kept: messages may echo tenant text.
                attempts.Add(AttemptRecord.Failed(attempt, watch.ElapsedMilliseconds, TriageFailureReasons.Unavailable, ex.GetType().Name));
                return ModelRun.Failed(attempts, TriageFailureReasons.Unavailable, total.ElapsedMilliseconds);
            }

            if (!result.Succeeded)
            {
                var reason = string.IsNullOrWhiteSpace(result.FailureReason) ? TriageFailureReasons.Unavailable : result.FailureReason;
                attempts.Add(AttemptRecord.From(attempt, result, reason, Array.Empty<string>()));
                return ModelRun.Failed(attempts, reason, total.ElapsedMilliseconds);
            }

            var validation = TriageValidator.Validate(result.RawOutput);
            attempts.Add(AttemptRecord.From(attempt, result, null, validation.Errors));
            if (validation.IsValid)
            {
                return ModelRun.Succeeded(attempts, validation.Response!, total.ElapsedMilliseconds);
            }
        }

        return ModelRun.Failed(attempts, TriageFailureReasons.InvalidOutput, total.ElapsedMilliseconds);
    }

    private sealed record ModelRun(IReadOnlyList<AttemptRecord> Attempts, TriageResponse? Response, string? FailureReason, long TotalLatencyMs)
    {
        public static ModelRun Succeeded(IReadOnlyList<AttemptRecord> attempts, TriageResponse response, long latencyMs) =>
            new(attempts, response, null, latencyMs);

        public static ModelRun Failed(IReadOnlyList<AttemptRecord> attempts, string reason, long latencyMs) =>
            new(attempts, null, reason, latencyMs);
    }

    private sealed record AttemptRecord(
        int Attempt,
        string? ModelId,
        string? PromptVersion,
        long LatencyMs,
        int? InputTokens,
        int? OutputTokens,
        string? RawOutput,
        string? FailureReason,
        string? ExceptionType,
        IReadOnlyList<string> ValidationErrors)
    {
        public static AttemptRecord Failed(int attempt, long latencyMs, string reason, string? exceptionType = null) =>
            new(attempt, null, null, latencyMs, null, null, null, reason, exceptionType, Array.Empty<string>());

        public static AttemptRecord From(int attempt, TriageModelResult result, string? reason, IReadOnlyList<string> errors) =>
            new(attempt, result.ModelId, result.PromptVersion, result.LatencyMs, result.InputTokens, result.OutputTokens,
                result.RawOutput, reason, null, errors);
    }
}
