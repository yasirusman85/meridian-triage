using Meridian.Domain.Contractors;

namespace Meridian.Application.Abstractions;

public interface IContractorRepository
{
    /// <summary>Every seeded contractor, read-only.</summary>
    Task<IReadOnlyList<Contractor>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Every usual-contractor assignment (per building and company-wide defaults), read-only.</summary>
    Task<IReadOnlyList<ContractorAssignment>> ListAssignmentsAsync(CancellationToken cancellationToken);
}
