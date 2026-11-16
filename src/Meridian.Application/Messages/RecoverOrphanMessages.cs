using System.Text.Json;
using Meridian.Application.Abstractions;
using Meridian.Application.Outbox;
using Meridian.Application.Triage;
using Meridian.Domain.Jobs;
using Meridian.Domain.Rules;

namespace Meridian.Application.Messages;

/// <summary>
/// Orphan safety (RULE-11): <see cref="SubmitMessage"/> stores a message alone before the model is called and makes
/// its jobs in a second save, so a crash or restart between the two leaves a stored message with no job and no
/// acknowledgement. This use case finds such messages and gives each one Unsure job with the emergency rules applied,
/// a <c>ModelPreparationFailed</c> outcome (reason <see cref="TriageFailureReasons.Interrupted"/>; the model is not
/// asked again) and the tenant's acknowledgement, all in one unit of work per message. Runs at startup; a background
/// worker can call it too, with a minimum age so messages still inside their intake are left alone.
/// </summary>
public sealed class RecoverOrphanMessages
{
    /// <summary>
    /// How old an orphan must be before a running system recovers it: well past the model budget (20 s), so a message
    /// whose intake is still in progress is never given a second set of jobs.
    /// </summary>
    public static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromMinutes(2);

    private readonly IJobRepository _jobs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly AcknowledgeTenant _acknowledge;

    public RecoverOrphanMessages(IJobRepository jobs, IUnitOfWork unitOfWork, IClock clock, AcknowledgeTenant acknowledge)
    {
        _jobs = jobs;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _acknowledge = acknowledge;
    }

    /// <summary>Recovers every orphan received at least <paramref name="minimumAge"/> ago.</summary>
    /// <param name="minimumAge">
    /// <see cref="TimeSpan.Zero"/> at startup, when no intake can be in progress; <see cref="DefaultMinimumAge"/> otherwise.
    /// </param>
    /// <returns>The jobs made, one per recovered message.</returns>
    public async Task<IReadOnlyList<SubmittedJob>> ExecuteAsync(TimeSpan minimumAge, CancellationToken cancellationToken)
    {
        if (minimumAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAge), minimumAge, "Must not be negative.");
        }

        var orphans = await _jobs.ListMessagesWithoutJobsAsync(_clock.UtcNow - minimumAge, cancellationToken);
        var recovered = new List<SubmittedJob>();
        foreach (var message in orphans)
        {
            var made = await _unitOfWork.ExecuteAsync(
                async ct =>
                {
                    // Re-checked inside the unit of work: another caller may have recovered it meanwhile.
                    if (await _jobs.MessageHasJobsAsync(message.Id, ct))
                    {
                        return null;
                    }

                    var now = _clock.UtcNow;
                    var hits = EmergencyRules.Evaluate(message.Text, message.ReceivedAtUtc);
                    var number = await _jobs.NextJobNumberAsync(ct);
                    var job = Job.CreateFromMessage(message, JobReference.FromNumber(number), now);
                    job.ApplyRules(hits, now);
                    job.RecordModelFailure(InterruptedPayload(), now);
                    _jobs.Add(job);
                    _acknowledge.Send(message, hits, new[] { job }, now);
                    return new SubmittedJob(job.Id, job.Reference, false);
                },
                cancellationToken);
            if (made is not null)
            {
                recovered.Add(made);
            }
        }

        return recovered;
    }

    private static string InterruptedPayload() => JsonSerializer.Serialize(new
    {
        modelId = (string?)null,
        promptVersion = (string?)null,
        attemptCount = 0,
        latencyMs = 0,
        reason = TriageFailureReasons.Interrupted,
        applied = (object?)null,
        attempts = Array.Empty<object>(),
        recovered = true,
    });
}
