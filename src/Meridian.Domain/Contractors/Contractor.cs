using Meridian.Domain.Jobs;

namespace Meridian.Domain.Contractors;

/// <summary>A contractor Meridian sends work to, e.g. a plumber, with the phone number a person calls (R11).</summary>
public sealed class Contractor
{
    public const int MaxNameLength = 128;
    public const int MaxPhoneLength = 32;

    private Contractor()
    {
        Name = string.Empty;
        Phone = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>The category of work the contractor does; never <see cref="Category.Unsure"/>.</summary>
    public Category Trade { get; private set; }

    public string Phone { get; private set; }

    public static Contractor Create(Guid id, string name, Category trade, string phone)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            throw new ArgumentException($"A name of at most {MaxNameLength} characters is required.", nameof(name));
        }

        ContractorAssignment.RequireTrade(trade, nameof(trade));
        if (string.IsNullOrWhiteSpace(phone) || phone.Trim().Length > MaxPhoneLength)
        {
            throw new ArgumentException($"A phone number of at most {MaxPhoneLength} characters is required.", nameof(phone));
        }

        return new Contractor { Id = id, Name = name.Trim(), Trade = trade, Phone = phone.Trim() };
    }
}
