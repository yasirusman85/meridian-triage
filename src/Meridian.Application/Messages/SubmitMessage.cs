using Meridian.Application.Abstractions;
using Meridian.Application.Outbox;
using Meridian.Application.Triage;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;
using Meridian.Domain.Rules;

namespace Meridian.Application.Messages;

public sealed record SubmitMessageCommand(string? Sender, string? Text);

/// <summary>One job a message made or was attached to, in the order of the model's issues.</summary>
/// <param name="IsFollowUp">True when the message was attached to an existing job rather than starting it.</param>
public sealed record SubmittedJob(Guid JobId, string Reference, bool IsFollowUp);

public sealed record SubmitMessageResult(
    Guid? MessageId,
    IReadOnlyList<SubmittedJob> Jobs,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Errors.Count == 0;

    public static SubmitMessageResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(null, Array.Empty<SubmittedJob>(), errors);

    public static SubmitMessageResult Created(Guid messageId, IReadOnlyList<SubmittedJob> jobs) =>
        new(messageId, jobs, new Dictionary<string, string[]>());
}

/// <summary>
/// The intake pipeline (AD-3): stores a pasted message unchanged, evaluates the emergency rules, asks the model
/// (<see cref="PrepareJob"/>), then turns the model's issues into jobs with plain code (AD-13):
/// <list type="number">
/// <item><see cref="PropertyResolver"/> matches each issue's <c>property_text</c>; unresolved or ambiguous → Unsure, property unset.</item>
/// <item><see cref="FollowUpMatcher"/> attaches an issue to an existing open job as a follow-up, which can raise its urgency.</item>
/// <item>Every other issue becomes its own job; one message can make several jobs.</item>
/// </list>
/// The message is committed before the model is called, so a model failure never loses it; when the model fails
/// the message makes one Unsure job, and a valid answer with no issues makes one Unsure job flagged for a person.
/// Every job the message touches gets the rule result (<c>RulesApplied</c>), so a rule emergency applies whatever
/// the model does (RULE-11).
/// In the same second unit of work the sender gets one fixed acknowledgement quoting every job reference the message
/// made or matched (<see cref="AcknowledgeTenant"/>, R16). A crash between the two saves leaves a message with no
/// job; <see cref="RecoverOrphanMessages"/> turns it into one Unsure job and acknowledges it (RULE-11).
/// </summary>
public sealed class SubmitMessage
{
    private readonly IJobRepository _jobs;
    private readonly IPropertyRepository _properties;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly PrepareJob _prepareJob;
    private readonly AcknowledgeTenant _acknowledge;

    public SubmitMessage(
        IJobRepository jobs,
        IPropertyRepository properties,
        IUnitOfWork unitOfWork,
        IClock clock,
        PrepareJob prepareJob,
        AcknowledgeTenant acknowledge)
    {
        _jobs = jobs;
        _properties = properties;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _prepareJob = prepareJob;
        _acknowledge = acknowledge;
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

        // 1. Store the message, unchanged and once (RULE-9).
        var message = await _unitOfWork.ExecuteAsync(
            _ =>
            {
                var received = Message.Receive(command.Sender!, command.Text!, _clock.UtcNow);
                _jobs.AddMessage(received);
                return Task.FromResult(received);
            },
            cancellationToken);

        // 2. Emergency rules before the model (AD-3); they can only raise urgency (AD-4).
        var hits = EmergencyRules.Evaluate(message.Text, message.ReceivedAtUtc);

        // 3. Model preparation; never throws for a model problem.
        var preparation = await _prepareJob.AskAsync(message, cancellationToken);

        // 4. Jobs, follow-ups and the tenant's acknowledgement (R16). Saved even if the caller has gone away, so the stored message always gets its jobs.
        var jobs = await _unitOfWork.ExecuteAsync(
            ct => MakeJobsAsync(message, hits, preparation, ct),
            CancellationToken.None);

        return SubmitMessageResult.Created(message.Id, jobs);
    }

    private async Task<IReadOnlyList<SubmittedJob>> MakeJobsAsync(
        Message message,
        IReadOnlyList<RuleHit> hits,
        ModelPreparation preparation,
        CancellationToken cancellationToken)
    {
        var response = preparation.Response;
        if (response is null)
        {
            var failed = await NewJobAsync(message, hits, null, cancellationToken);
            PrepareJob.RecordFailure(failed, preparation, _clock.UtcNow);
            _acknowledge.Send(message, hits, new[] { failed }, _clock.UtcNow);
            return new[] { new SubmittedJob(failed.Id, failed.Reference, false) };
        }

        if (response.Issues.Count == 0)
        {
            var flagged = await NewJobAsync(message, hits, null, cancellationToken);
            PrepareJob.RecordNotMaintenance(flagged, preparation, _clock.UtcNow);
            _acknowledge.Send(message, hits, new[] { flagged }, _clock.UtcNow);
            return new[] { new SubmittedJob(flagged.Id, flagged.Reference, false) };
        }

        var resolver = new PropertyResolver(await _properties.ListAsync(cancellationToken));
        var matches = response.Issues.Select(i => resolver.Resolve(i.PropertyText)).ToList();
        var propertyIds = matches.Where(m => m.IsResolved).Select(m => m.Property!.Id).Distinct().ToList();
        var candidates = propertyIds.Count == 0
            ? Array.Empty<Job>()
            : await _jobs.ListOpenAtPropertiesAsync(
                propertyIds,
                message.ReceivedAtUtc - FollowUpMatcher.Window,
                cancellationToken);

        var result = new List<SubmittedJob>();
        var created = new List<Job>();
        var followUps = new Dictionary<Job, Urgency>();
        for (var index = 0; index < response.Issues.Count; index++)
        {
            var issue = response.Issues[index];
            var property = matches[index];
            var category = PrepareJob.CategoryFor(issue, property);

            // Only jobs that existed before this message are candidates: the model's split stands within a message.
            var existing = FollowUpMatcher.Match(candidates, property.Property?.Id, category, message.ReceivedAtUtc);
            if (existing is not null)
            {
                followUps[existing] = followUps.TryGetValue(existing, out var raised) && raised > issue.Urgency
                    ? raised
                    : issue.Urgency;
                result.Add(new SubmittedJob(existing.Id, existing.Reference, true));
                continue;
            }

            var job = await NewJobAsync(message, hits, property.Property?.Id, cancellationToken);
            PrepareJob.RecordIssue(job, preparation, index, property, _clock.UtcNow);
            created.Add(job);
            result.Add(new SubmittedJob(job.Id, job.Reference, false));
        }

        foreach (var (job, urgency) in followUps)
        {
            job.AttachFollowUp(message, urgency, _clock.UtcNow);
            job.ApplyRules(hits, _clock.UtcNow);
        }

        // Two issues attached to the same job are one follow-up.
        var touched = result.DistinctBy(j => j.JobId).ToList();
        var jobsById = followUps.Keys.Concat(created).ToDictionary(j => j.Id);
        _acknowledge.Send(message, hits, touched.Select(j => jobsById[j.JobId]).ToList(), _clock.UtcNow);
        return touched;
    }

    private async Task<Job> NewJobAsync(Message message, IReadOnlyList<RuleHit> hits, Guid? propertyId, CancellationToken cancellationToken)
    {
        var number = await _jobs.NextJobNumberAsync(cancellationToken);
        var job = Job.CreateFromMessage(message, JobReference.FromNumber(number), _clock.UtcNow, propertyId);
        job.ApplyRules(hits, _clock.UtcNow);
        _jobs.Add(job);
        return job;
    }
}
