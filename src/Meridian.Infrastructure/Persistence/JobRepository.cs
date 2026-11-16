using Meridian.Application.Abstractions;
using Meridian.Domain.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence;

internal sealed class JobRepository : IJobRepository
{
    private readonly MeridianDbContext _db;

    public JobRepository(MeridianDbContext db)
    {
        _db = db;
    }

    public async Task<long> NextJobNumberAsync(CancellationToken cancellationToken)
    {
        // One atomic statement: the UPDATE takes SQLite's write lock, so two writers never get the same number.
        var values = await _db.Database
            .SqlQueryRaw<long>(
                "UPDATE \"JobNumberSequence\" SET \"LastValue\" = \"LastValue\" + 1 WHERE \"Id\" = {0} RETURNING \"LastValue\" AS \"Value\"",
                JobNumberSequence.SingletonId)
            .ToListAsync(cancellationToken);

        return values.Count == 1
            ? values[0]
            : throw new InvalidOperationException("Job number sequence row is missing.");
    }

    public void Add(Job job) => _db.Jobs.Add(job);

    public async Task<IReadOnlyList<Job>> ListAsync(CancellationToken cancellationToken) =>
        await _db.Jobs
            .AsNoTracking()
            .Include(j => j.Messages)
            .ToListAsync(cancellationToken);
}
