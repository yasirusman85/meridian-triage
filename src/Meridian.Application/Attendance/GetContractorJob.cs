using Meridian.Application.Abstractions;
using Meridian.Domain.Jobs;

namespace Meridian.Application.Attendance;

/// <summary>What a contractor sees through its link: only that job's reference, address, category, summary and contractor name.</summary>
public sealed record ContractorJobView(string Reference, string? Address, string Category, string? Summary, string ContractorName);

/// <summary>
/// The contractor's view of one job (AD-11). A wrong, used or expired token gives null, which the API answers with 404
/// and no detail. Read-only.
/// </summary>
public sealed class GetContractorJob
{
    private readonly IJobRepository _jobs;
    private readonly IPropertyRepository _properties;
    private readonly IContractorRepository _contractors;

    public GetContractorJob(IJobRepository jobs, IPropertyRepository properties, IContractorRepository contractors)
    {
        _jobs = jobs;
        _properties = properties;
        _contractors = contractors;
    }

    public async Task<ContractorJobView?> ExecuteAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var job = await _jobs.GetByContractorTokenHashAsync(Job.HashContractorToken(token), cancellationToken);
        if (job is null || job.Status != JobStatus.SentToContractor || !job.HasContractorToken(token) || job.SentToContractorId is not { } contractorId)
        {
            return null;
        }

        var property = job.PropertyId is { } propertyId
            ? (await _properties.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == propertyId)
            : null;
        var contractor = (await _contractors.ListAsync(cancellationToken)).FirstOrDefault(c => c.Id == contractorId);
        return new ContractorJobView(
            job.Reference,
            property?.Name,
            job.Category.ToString(),
            job.SummaryEn,
            contractor?.Name ?? string.Empty);
    }
}
