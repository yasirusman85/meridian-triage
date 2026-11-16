using Meridian.Application.Abstractions;

namespace Meridian.Infrastructure.Persistence;

internal sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly MeridianDbContext _db;

    public EfUnitOfWork(MeridianDbContext db)
    {
        _db = db;
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var result = await work(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
