using Meridian.Application.Abstractions;
using Meridian.Domain.Properties;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence;

internal sealed class PropertyRepository : IPropertyRepository
{
    private readonly MeridianDbContext _db;

    public PropertyRepository(MeridianDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Property>> ListAsync(CancellationToken cancellationToken) =>
        await _db.Properties
            .AsNoTracking()
            .Include(p => p.Building)
            .ToListAsync(cancellationToken);
}
