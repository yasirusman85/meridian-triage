using Meridian.Application.Abstractions;
using Meridian.Domain.Contractors;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence;

internal sealed class ContractorRepository : IContractorRepository
{
    private readonly MeridianDbContext _db;

    public ContractorRepository(MeridianDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Contractor>> ListAsync(CancellationToken cancellationToken) =>
        await _db.Contractors.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ContractorAssignment>> ListAssignmentsAsync(CancellationToken cancellationToken) =>
        await _db.ContractorAssignments.AsNoTracking().ToListAsync(cancellationToken);
}
