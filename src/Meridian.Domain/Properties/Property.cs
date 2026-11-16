namespace Meridian.Domain.Properties;

/// <summary>One lettable home: a flat in a building, or a whole building (a house) when <see cref="Unit"/> is null.</summary>
public sealed class Property
{
    private Property()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid BuildingId { get; private set; }

    /// <summary>The flat number as Meridian writes it (e.g. <c>4</c>, <c>14b</c>); null for a whole building.</summary>
    public string? Unit { get; private set; }

    /// <summary>How the property is shown, e.g. <c>Flat 4, 22 Pemberton Road</c> or <c>14b Alder Court</c>.</summary>
    public string Name { get; private set; }

    public Building? Building { get; private set; }

    internal static Property Create(Guid id, Guid buildingId, string? unit, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A property name is required.", nameof(name));
        }

        return new Property { Id = id, BuildingId = buildingId, Unit = unit, Name = name.Trim() };
    }

    internal void SetBuilding(Building building) => Building = building;
}
