using Meridian.Domain.Jobs;

namespace Meridian.Domain.Contractors;

/// <summary>
/// The usual contractor for one category of work: at one building, or company-wide when <see cref="BuildingId"/> is
/// null (the default used when the building has no assignment for the category). AD-3: fixed data, read by plain code.
/// </summary>
public sealed class ContractorAssignment
{
    private ContractorAssignment()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>The building; null for the company-wide default for <see cref="Category"/>.</summary>
    public Guid? BuildingId { get; private set; }

    public Category Category { get; private set; }

    public Guid ContractorId { get; private set; }

    public bool IsDefault => BuildingId is null;

    /// <param name="buildingId">The building, or null for the company-wide default.</param>
    public static ContractorAssignment Create(Guid id, Guid? buildingId, Category category, Guid contractorId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An id is required.", nameof(id));
        }

        if (buildingId == Guid.Empty)
        {
            throw new ArgumentException("Building id must not be empty; pass null for the company-wide default.", nameof(buildingId));
        }

        RequireTrade(category, nameof(category));
        if (contractorId == Guid.Empty)
        {
            throw new ArgumentException("A contractor id is required.", nameof(contractorId));
        }

        return new ContractorAssignment { Id = id, BuildingId = buildingId, Category = category, ContractorId = contractorId };
    }

    internal static void RequireTrade(Category category, string paramName)
    {
        if (!Enum.IsDefined(category) || category == Category.Unsure)
        {
            throw new ArgumentOutOfRangeException(paramName, category, "A trade category (not Unsure) is required.");
        }
    }
}
