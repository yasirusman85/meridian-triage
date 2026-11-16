using Meridian.Application.Abstractions;
using Meridian.Application.Contractors;
using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;

namespace Meridian.Application.Review;

/// <summary>
/// A person's change to a Prepared job. Null fields are left as they are. <see cref="UrgencyReason"/> is required to
/// lower urgency (AD-4).
/// </summary>
public sealed record JobEdit(
    Category? Category = null,
    Urgency? Urgency = null,
    string? UrgencyReason = null,
    Guid? ContractorId = null,
    Guid? PropertyId = null)
{
    public bool IsEmpty => Category is null && Urgency is null && ContractorId is null && PropertyId is null;
}

public enum ReviewOutcome
{
    Done,

    /// <summary>The job does not exist.</summary>
    NotFound,

    /// <summary>The request is invalid (unknown actor, missing reason, unknown contractor...); see <see cref="ReviewResult.Errors"/>.</summary>
    Invalid,

    /// <summary>The job's state does not allow the action (AD-6); see <see cref="ReviewResult.Detail"/> and <see cref="ReviewResult.Missing"/>.</summary>
    Conflict,
}

/// <summary>The result of a review action. On anything but <see cref="ReviewOutcome.Done"/> nothing changed.</summary>
public sealed record ReviewResult(
    ReviewOutcome Outcome,
    IReadOnlyDictionary<string, string[]> Errors,
    string? Detail,
    IReadOnlyList<string> Missing)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    public static ReviewResult Done { get; } = new(ReviewOutcome.Done, NoErrors, null, Array.Empty<string>());

    public static ReviewResult NotFound { get; } = new(ReviewOutcome.NotFound, NoErrors, null, Array.Empty<string>());

    public static ReviewResult Invalid(string field, string error) =>
        new(ReviewOutcome.Invalid, new Dictionary<string, string[]> { [field] = new[] { error } }, null, Array.Empty<string>());

    public static ReviewResult Conflict(string detail, IReadOnlyList<string>? missing = null) =>
        new(ReviewOutcome.Conflict, NoErrors, detail, missing ?? Array.Empty<string>());
}

/// <summary>
/// The review use case (R13–R15, AD-6): a person approves, rejects or edits a Prepared job. Each action is one unit
/// of work with its events (AD-5), recorded with the acting person. Illegal transitions are conflicts; nothing is
/// sent to a contractor here, and nothing is approved automatically.
/// </summary>
public sealed class ReviewJob
{
    /// <summary>Error keys, matching the request fields.</summary>
    public static class Fields
    {
        public const string Actor = "actingAs";
        public const string Reason = "reason";
        public const string Category = "category";
        public const string Urgency = "urgency";
        public const string UrgencyReason = "urgencyReason";
        public const string ContractorId = "contractorId";
        public const string PropertyId = "propertyId";
        public const string Edit = "edit";
    }

    public const int MaxReasonLength = 500;

    private readonly IJobRepository _jobs;
    private readonly IPropertyRepository _properties;
    private readonly IContractorRepository _contractors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly StaffDirectory _staff;

    public ReviewJob(
        IJobRepository jobs,
        IPropertyRepository properties,
        IContractorRepository contractors,
        IUnitOfWork unitOfWork,
        IClock clock,
        StaffDirectory staff)
    {
        _jobs = jobs;
        _properties = properties;
        _contractors = contractors;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _staff = staff;
    }

    /// <summary>Approves a Prepared job that has a category (not Unsure) and a contractor.</summary>
    public Task<ReviewResult> ApproveAsync(Guid jobId, string? actor, CancellationToken cancellationToken) =>
        RunAsync(jobId, actor, (job, person, _) =>
        {
            var missing = job.MissingForApproval();
            if (missing.Count > 0)
            {
                return Task.FromResult(ReviewResult.Conflict(MissingText(missing), missing));
            }

            job.Approve(person, _clock.UtcNow);
            return Task.FromResult(ReviewResult.Done);
        }, cancellationToken);

    /// <summary>Rejects a Prepared job; a reason is required.</summary>
    public Task<ReviewResult> RejectAsync(Guid jobId, string? actor, string? reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return CheckedAsync(actor, ReviewResult.Invalid(Fields.Reason, "Give a reason for rejecting the job."));
        }

        if (reason.Trim().Length > MaxReasonLength)
        {
            return CheckedAsync(actor, ReviewResult.Invalid(Fields.Reason, $"The reason must be at most {MaxReasonLength} characters."));
        }

        return RunAsync(jobId, actor, (job, person, _) =>
        {
            job.Reject(person, reason, _clock.UtcNow);
            return Task.FromResult(ReviewResult.Done);
        }, cancellationToken);
    }

    /// <summary>
    /// Edits a Prepared job: property, category, urgency (through <c>SetUrgencyByPerson</c>) and contractor, each
    /// changed field one event. A category change, or a property change while the contractor is not a person's
    /// choice, re-runs <see cref="ContractorLookup"/> (none for Unsure), unless the edit also picks a contractor.
    /// </summary>
    public Task<ReviewResult> EditAsync(Guid jobId, string? actor, JobEdit edit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (Validate(edit) is { } invalid)
        {
            return CheckedAsync(actor, invalid);
        }

        return RunAsync(jobId, actor, async (job, person, ct) =>
        {
            if (edit.Urgency is { } level && level < job.Urgency && string.IsNullOrWhiteSpace(edit.UrgencyReason))
            {
                return ReviewResult.Invalid(Fields.UrgencyReason, "Give a reason for lowering the urgency.");
            }

            var properties = edit.PropertyId is null && edit.Category is null
                ? null
                : await _properties.ListAsync(ct);
            if (edit.PropertyId is { } propertyId && properties!.All(p => p.Id != propertyId))
            {
                return ReviewResult.Invalid(Fields.PropertyId, "Unknown property.");
            }

            if (edit.ContractorId is { } contractorId
                && (await _contractors.ListAsync(ct)).All(c => c.Id != contractorId))
            {
                return ReviewResult.Invalid(Fields.ContractorId, "Unknown contractor.");
            }

            // Everything is checked; from here on the job changes.
            var now = _clock.UtcNow;
            var propertyChanged = edit.PropertyId is { } newProperty && job.EditProperty(newProperty, person, now);
            var categoryChanged = edit.Category is { } category && job.EditCategory(category, person, now);
            if (edit.Urgency is { } urgency)
            {
                job.EditUrgency(urgency, person, edit.UrgencyReason, now);
            }

            if (edit.ContractorId is { } chosen)
            {
                job.ChooseContractor(chosen, person, now);
            }
            else if (categoryChanged || (propertyChanged && job.SuggestedContractorRule != ContractorRule.Person))
            {
                var buildingId = job.PropertyId is { } id ? properties!.FirstOrDefault(p => p.Id == id)?.BuildingId : (Guid?)null;
                var lookup = new ContractorLookup(await _contractors.ListAssignmentsAsync(ct));
                if (lookup.Suggest(buildingId, job.Category) is { } suggestion)
                {
                    job.SuggestContractor(suggestion, now);
                }
                else
                {
                    job.ClearContractor(person, categoryChanged ? Job.EditedFields.Category : Job.EditedFields.Property, now);
                }
            }

            return ReviewResult.Done;
        }, cancellationToken);
    }

    /// <summary>Human text for what approval still needs, e.g. "Set a category and choose a contractor before approving."</summary>
    public static string MissingText(IReadOnlyList<string> missing)
    {
        var parts = missing.Select(m => m switch
        {
            Job.ApprovalNeeds.Category => "set a category",
            Job.ApprovalNeeds.Contractor => "choose a contractor",
            _ => m,
        }).ToList();
        var text = string.Join(" and ", parts);
        return char.ToUpperInvariant(text[0]) + text[1..] + " before approving.";
    }

    private static ReviewResult? Validate(JobEdit edit)
    {
        if (edit.IsEmpty)
        {
            return ReviewResult.Invalid(Fields.Edit, "Nothing to change.");
        }

        if (edit.Category is { } category && !Enum.IsDefined(category))
        {
            return ReviewResult.Invalid(Fields.Category, "Unknown category.");
        }

        if (edit.Urgency is { } urgency && !Enum.IsDefined(urgency))
        {
            return ReviewResult.Invalid(Fields.Urgency, "Unknown urgency.");
        }

        if (edit.UrgencyReason is { } reason && reason.Trim().Length > MaxReasonLength)
        {
            return ReviewResult.Invalid(Fields.UrgencyReason, $"The reason must be at most {MaxReasonLength} characters.");
        }

        if (edit.ContractorId == Guid.Empty)
        {
            return ReviewResult.Invalid(Fields.ContractorId, "Unknown contractor.");
        }

        if (edit.PropertyId == Guid.Empty)
        {
            return ReviewResult.Invalid(Fields.PropertyId, "Unknown property.");
        }

        return null;
    }

    /// <summary>An unknown actor is reported before any other problem with the request.</summary>
    private Task<ReviewResult> CheckedAsync(string? actor, ReviewResult otherwise) =>
        Task.FromResult(_staff.Find(actor) is null ? UnknownActor() : otherwise);

    private static ReviewResult UnknownActor() =>
        ReviewResult.Invalid(Fields.Actor, "Choose who you are acting as.");

    private async Task<ReviewResult> RunAsync(
        Guid jobId,
        string? actor,
        Func<Job, string, CancellationToken, Task<ReviewResult>> action,
        CancellationToken cancellationToken)
    {
        if (_staff.Find(actor) is not { } person)
        {
            return UnknownActor();
        }

        try
        {
            return await _unitOfWork.ExecuteAsync(async ct =>
            {
                var job = await _jobs.GetAsync(jobId, ct);
                if (job is null)
                {
                    return ReviewResult.NotFound;
                }

                if (job.Status != JobStatus.Prepared)
                {
                    return ReviewResult.Conflict($"Job {job.Reference} is {job.Status}; only a Prepared job can be reviewed.");
                }

                return await action(job, person.Id, ct);
            }, cancellationToken);
        }
        catch (JobStateException ex)
        {
            // Thrown before the unit of work saved anything: nothing changed.
            return ReviewResult.Conflict(ex.Message, ex.Missing);
        }
    }
}
