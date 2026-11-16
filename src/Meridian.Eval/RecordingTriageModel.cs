using Meridian.Application.Abstractions;
using Meridian.Application.Triage;

namespace Meridian.Eval;

/// <summary>
/// Wraps the configured <see cref="ITriageModel"/> and keeps every result it returns, so the eval can read the model's
/// answer, latency and tokens per case. It passes calls through unchanged.
/// </summary>
public sealed class RecordingTriageModel : ITriageModel
{
    private readonly ITriageModel _inner;
    private readonly List<TriageModelResult> _results = new();
    private readonly object _gate = new();

    public RecordingTriageModel(ITriageModel inner)
    {
        _inner = inner;
    }

    public string ModelId => _inner.ModelId;

    public string PromptVersion => _inner.PromptVersion;

    public async Task<TriageModelResult> PrepareAsync(TriageRequest request, CancellationToken cancellationToken)
    {
        var result = await _inner.PrepareAsync(request, cancellationToken);
        lock (_gate)
        {
            _results.Add(result);
        }

        return result;
    }

    /// <summary>Returns the results since the last call and forgets them.</summary>
    public IReadOnlyList<TriageModelResult> TakeResults()
    {
        lock (_gate)
        {
            var taken = _results.ToList();
            _results.Clear();
            return taken;
        }
    }
}

/// <summary>A clock the runner sets to each case's <c>received_at</c>, so the pipeline sees the case's time.</summary>
public sealed class EvalClock : IClock
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
}
