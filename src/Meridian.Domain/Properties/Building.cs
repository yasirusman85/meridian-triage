namespace Meridian.Domain.Properties;

/// <summary>
/// A building Meridian manages, e.g. <c>22 Pemberton Road</c> or <c>Alder Court</c>. A name that starts with a
/// house number (<c>22 Pemberton Road</c>) is matched only together with that number (AD-13).
/// </summary>
public sealed class Building
{
    private readonly List<Property> _properties = new();

    private Building()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>The building's address as Meridian writes it, e.g. <c>22 Pemberton Road</c>.</summary>
    public string Name { get; private set; }

    public IReadOnlyList<Property> Properties => _properties;

    public static Building Create(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A building name is required.", nameof(name));
        }

        return new Building { Id = id, Name = name.Trim() };
    }

    /// <summary>Adds the whole building as one property (a house), with no unit.</summary>
    public Property AddWholeBuilding(Guid id) => Add(Property.Create(id, Id, null, Name));

    /// <summary>Adds a flat, e.g. unit <c>4</c> shown as <c>Flat 4, 22 Pemberton Road</c>.</summary>
    /// <param name="name">How the flat is shown; defaults to <c>Flat {unit}, {building}</c>.</param>
    public Property AddFlat(Guid id, string unit, string? name = null)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new ArgumentException("A unit is required.", nameof(unit));
        }

        return Add(Property.Create(id, Id, unit.Trim(), name ?? $"Flat {unit.Trim()}, {Name}"));
    }

    private Property Add(Property property)
    {
        if (_properties.Any(p => p.Unit is null) || (property.Unit is null && _properties.Count > 0))
        {
            throw new InvalidOperationException("A whole-building property cannot share a building with flats.");
        }

        if (property.Unit is not null
            && _properties.Any(p => string.Equals(p.Unit, property.Unit, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Unit {property.Unit} already exists in {Name}.");
        }

        property.SetBuilding(this);
        _properties.Add(property);
        return property;
    }
}
