namespace Meridian.Domain.Contractors;

/// <summary>Which assignment gave a job's suggested contractor.</summary>
public enum ContractorRule
{
    /// <summary>The building's usual contractor for the category.</summary>
    Building,

    /// <summary>The company-wide default for the category (the building has no assignment, or the property is unknown).</summary>
    Default,

    /// <summary>A person chose the contractor in review; not a suggestion of <c>ContractorLookup</c>.</summary>
    Person,
}

/// <summary>The contractor fixed code suggests for a job (AD-3), and the rule that chose it.</summary>
public sealed record ContractorSuggestion(Guid ContractorId, ContractorRule Rule);
