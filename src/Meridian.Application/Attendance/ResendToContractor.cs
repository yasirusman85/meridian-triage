using Meridian.Application.Abstractions;
using Meridian.Application.Outbox;
using Meridian.Application.Review;
using Meridian.Application.Staff;
using Meridian.Domain.Jobs;

namespace Meridian.Application.Attendance;

/// <summary>
/// A person sends a job that is already SentToContractor to another contractor (the fallback, after the overdue alert):
/// a new token and a new message, the old link stops working, the overdue flag is cleared and <c>SentToContractor</c>
/// is recorded again. Nothing is chosen automatically (AD-11). One unit of work; every check runs before anything changes.
/// </summary>
public sealed class ResendToContractor
{
    private readonly IJobRepository _jobs;
    private readonly IPropertyRepository _properties;
    private readonly IContractorRepository _contractors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IOutbox _outbox;
    private readonly StaffDirectory _staff;

    public ResendToContractor(
        IJobRepository jobs,
        IPropertyRepository properties,
        IContractorRepository contractors,
        IUnitOfWork unitOfWork,
        IClock clock,
        IOutbox outbox,
        StaffDirectory staff)
    {
        _jobs = jobs;
        _properties = properties;
        _contractors = contractors;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _outbox = outbox;
        _staff = staff;
    }

    public Task<ReviewResult> ExecuteAsync(Guid jobId, string? actor, Guid? contractorId, CancellationToken cancellationToken)
    {
        if (_staff.Find(actor) is not { } person)
        {
            return Task.FromResult(ReviewResult.Invalid(ReviewJob.Fields.Actor, "Choose who you are acting as."));
        }

        if (contractorId is not { } chosen || chosen == Guid.Empty)
        {
            return Task.FromResult(ReviewResult.Invalid(ReviewJob.Fields.ContractorId, "Choose a contractor to send the job to."));
        }

        return _unitOfWork.ExecuteAsync<ReviewResult>(async ct =>
        {
            var contractor = (await _contractors.ListAsync(ct)).FirstOrDefault(c => c.Id == chosen);
            if (contractor is null)
            {
                return ReviewResult.Invalid(ReviewJob.Fields.ContractorId, "Unknown contractor.");
            }

            var job = await _jobs.GetAsync(jobId, ct);
            if (job is null)
            {
                return ReviewResult.NotFound;
            }

            if (job.Status != JobStatus.SentToContractor)
            {
                return ReviewResult.Conflict($"Job {job.Reference} is {job.Status}; only a job sent to a contractor can be sent again.");
            }

            var address = job.PropertyId is { } propertyId
                ? (await _properties.ListAsync(ct)).FirstOrDefault(p => p.Id == propertyId)?.Name
                : null;
            ContractorDispatch.Send(job, contractor, address, _outbox, person.Id, _clock.UtcNow, resend: true);
            return ReviewResult.Done;
        }, cancellationToken);
    }
}
