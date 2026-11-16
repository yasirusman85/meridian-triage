using Meridian.Application.Abstractions;
using Meridian.Application.Triage;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;

namespace Meridian.Application.Messages;

public sealed record SubmitMessageCommand(string? Sender, string? Text);

public sealed record SubmitMessageResult(
    Guid? JobId,
    string? Reference,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Errors.Count == 0;

    public static SubmitMessageResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, null, errors);

    public static SubmitMessageResult Created(Guid jobId, string reference) =>
        new(jobId, reference, new Dictionary<string, string[]>());
}

/// <summary>
/// Stores a pasted message unchanged and creates its placeholder job, in one transaction, then runs model
/// preparation (<see cref="PrepareJob"/>). The message and job are saved before the model is called, so a model
/// failure never loses a message. Emergency rules and splitting arrive in later stories (AD-3).
/// </summary>
public sealed class SubmitMessage
{
    private readonly IJobRepository _jobs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly PrepareJob _prepareJob;

    public SubmitMessage(IJobRepository jobs, IUnitOfWork unitOfWork, IClock clock, PrepareJob prepareJob)
    {
        _jobs = jobs;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _prepareJob = prepareJob;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(SubmitMessageCommand command)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(command.Sender))
        {
            errors["sender"] = new[] { "Sender is required." };
        }

        if (string.IsNullOrWhiteSpace(command.Text))
        {
            errors["text"] = new[] { "Message text is required." };
        }

        return errors;
    }

    public async Task<SubmitMessageResult> ExecuteAsync(SubmitMessageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = Validate(command);
        if (errors.Count > 0)
        {
            return SubmitMessageResult.Invalid(errors);
        }

        var job = await _unitOfWork.ExecuteAsync(
            async ct =>
            {
                var now = _clock.UtcNow;
                var number = await _jobs.NextJobNumberAsync(ct);
                var message = Message.Receive(command.Sender!, command.Text!, now);
                var created = Job.CreateFromMessage(message, JobReference.FromNumber(number), now);
                _jobs.Add(created);
                return created;
            },
            cancellationToken);

        // After the commit: the message is safe whatever the model does (RULE-11).
        await _prepareJob.ExecuteAsync(job.Id, cancellationToken);

        return SubmitMessageResult.Created(job.Id, job.Reference);
    }
}
