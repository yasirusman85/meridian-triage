using System.Diagnostics;
using System.Text.Json;
using Meridian.Application.Abstractions;
using Meridian.Domain.Jobs;

namespace Meridian.Application.Triage;

/// <summary>Settings for <see cref="PrepareJob"/>. Defaults are the product's (AD-3: 20 s).</summary>
public sealed class PrepareJobOptions
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Budget for the whole preparation step, both attempts included.</summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;
}

/// <summary>What happened when a job was prepared.</summary>
public sealed record PrepareJobOutcome(bool Prepared, int Attempts, string? FailureReason);

/// <summary>
/// The model preparation step of the pipeline (AD-3): asks <see cref="ITriageModel"/> about a stored job's
/// message, validates the answer against <c>triage.v1</c> with one retry for invalid output, and applies the
/// first issue to the job, or records a failure and leaves it Unsure (RULE-11, R10). Never throws for a model
/// problem; the job always ends with exactly one <c>ModelPrepared</c> or <c>ModelPreparationFailed</c> event.
/// </summary>
public sealed class PrepareJob
{
    public const int MaxAttempts = 2;

    private readonly ITriageModel _model;
    private readonly IJobRepository _jobs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly TimeSpan _timeout;

    public PrepareJob(ITriageModel model, IJobRepository jobs, IUnitOfWork unitOfWork, IClock clock, PrepareJobOptions? options = null)
    {
        _model = model;
        _jobs = jobs;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _timeout = (options ?? new PrepareJobOptions()).Timeout;
    }

    public async Task<PrepareJobOutcome> ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _jobs.GetAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException($"Job {jobId} not found.");
        var message = job.Messages.OrderBy(m => m.ReceivedAtUtc).FirstOrDefault()
            ?? throw new InvalidOperationException($"Job {jobId} has no message.");

        // AD-10: text and received time only.
        var request = new TriageRequest(message.Text, message.ReceivedAtUtc);
        var run = await AskModelAsync(request, cancellationToken);

        // The outcome is saved even if the caller has gone away, so every job gets exactly one outcome event.
        await _unitOfWork.ExecuteAsync(
            _ =>
            {
                Apply(job, run);
                return Task.FromResult(0);
            },
            CancellationToken.None);

        return new PrepareJobOutcome(run.Response is not null, run.Attempts.Count, run.FailureReason);
    }

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

    private void Apply(Job job, ModelRun run)
    {
        var now = _clock.UtcNow;
        if (run.Response is null)
        {
            job.RecordModelFailure(Payload(run, applied: null), now);
            return;
        }

        // Splitting into several jobs is a later story; the first issue prepares this job.
        var issue = run.Response.Issues[0];
        var category = issue.Confidence == TriageConfidence.Low ? Category.Unsure : issue.Category;
        var applied = new
        {
            category = category.ToString(),
            proposedCategory = issue.Category.ToString(),
            proposedUrgency = issue.Urgency.ToString(),
            confidence = issue.Confidence.ToString().ToLowerInvariant(),
            issueCount = run.Response.Issues.Count,
        };

        job.ApplyModelPreparation(
            category,
            issue.Urgency,
            issue.Explanation,
            issue.SummaryEn,
            run.Response.Language,
            Payload(run, applied),
            now);
    }

    private string Payload(ModelRun run, object? applied)
    {
        var last = run.Attempts.LastOrDefault();
        return JsonSerializer.Serialize(new
        {
            modelId = last?.ModelId ?? _model.ModelId,
            promptVersion = last?.PromptVersion ?? _model.PromptVersion,
            attemptCount = run.Attempts.Count,
            latencyMs = run.TotalLatencyMs,
            reason = run.FailureReason,
            applied,
            attempts = run.Attempts.Select(a => new
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
            }),
        });
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
