using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;

namespace Meridian.Application.Contractors;

/// <summary>
/// Suggests the usual contractor for a job (R12, AD-3): plain code over the seeded assignments, no model call.
/// The building's assignment for the category wins; otherwise the company-wide default for the category. An Unsure
/// category gives no suggestion ("needs a person"); so does a category with neither assignment.
/// </summary>
public sealed class ContractorLookup
{
    private readonly Dictionary<(Guid BuildingId, Category Category), Guid> _byBuilding = new();
    private readonly Dictionary<Category, Guid> _defaults = new();

    public ContractorLookup(IEnumerable<ContractorAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        foreach (var assignment in assignments)
        {
            var added = assignment.BuildingId is { } buildingId
                ? _byBuilding.TryAdd((buildingId, assignment.Category), assignment.ContractorId)
                : _defaults.TryAdd(assignment.Category, assignment.ContractorId);
            if (!added)
            {
                throw new ArgumentException(
                    $"More than one usual contractor for {assignment.Category} at {(object?)assignment.BuildingId ?? "company level"}.",
                    nameof(assignments));
            }
        }
    }

    /// <param name="buildingId">The job's building, or null when its property is unresolved.</param>
    /// <returns>The suggestion, or null when a person must choose.</returns>
    public ContractorSuggestion? Suggest(Guid? buildingId, Category category)
    {
        if (category == Category.Unsure)
        {
            return null;
        }

        if (buildingId is { } id && _byBuilding.TryGetValue((id, category), out var usual))
        {
            return new ContractorSuggestion(usual, ContractorRule.Building);
        }

        return _defaults.TryGetValue(category, out var fallback)
            ? new ContractorSuggestion(fallback, ContractorRule.Default)
            : null;
    }
}
