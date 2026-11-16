namespace Meridian.Application.Abstractions;

/// <summary>Runs work in one database transaction and saves everything it tracked (AD-5).</summary>
public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}
