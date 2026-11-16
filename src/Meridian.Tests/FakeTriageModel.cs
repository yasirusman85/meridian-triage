using Meridian.Application.Triage;

namespace Meridian.Tests;

/// <summary>A scripted <see cref="ITriageModel"/>: each call runs the next step; with no steps left it is unavailable.</summary>
internal sealed class FakeTriageModel : ITriageModel
{
    private readonly Queue<Func<TriageRequest, CancellationToken, Task<TriageModelResult>>> _steps = new();

    public string ModelId => "stub";

    public string PromptVersion => TriageContract.Version;

    public List<TriageRequest> Requests { get; } = new();

    public Action? OnCall { get; set; }

    public FakeTriageModel Returns(string rawOutput)
    {
        _steps.Enqueue((_, _) => Task.FromResult(TriageModelResult.Output(rawOutput, ModelId, PromptVersion, 5, 100, 50)));
        return this;
    }

    public FakeTriageModel Fails(string reason)
    {
        _steps.Enqueue((_, _) => Task.FromResult(TriageModelResult.Failure(reason, ModelId, PromptVersion, 1)));
        return this;
    }

    public FakeTriageModel Throws(Exception exception)
    {
        _steps.Enqueue((_, _) => Task.FromException<TriageModelResult>(exception));
        return this;
    }

    /// <summary>Waits until cancelled, like a model that never answers.</summary>
    public FakeTriageModel Hangs()
    {
        _steps.Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        return this;
    }

    /// <summary>Never answers and ignores the token.</summary>
    public FakeTriageModel HangsIgnoringCancellation()
    {
        _steps.Enqueue((_, _) => new TaskCompletionSource<TriageModelResult>().Task);
        return this;
    }

    public Task<TriageModelResult> PrepareAsync(TriageRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        OnCall?.Invoke();
        return _steps.Count > 0
            ? _steps.Dequeue()(request, cancellationToken)
            : Task.FromResult(TriageModelResult.Failure(TriageFailureReasons.Unavailable, ModelId, PromptVersion, 0));
    }
}
