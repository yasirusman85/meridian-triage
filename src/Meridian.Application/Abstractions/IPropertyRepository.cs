using Meridian.Domain.Properties;

namespace Meridian.Application.Abstractions;

public interface IPropertyRepository
{
    /// <summary>Every seeded property with its building loaded, read-only.</summary>
    Task<IReadOnlyList<Property>> ListAsync(CancellationToken cancellationToken);
}
