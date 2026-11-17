using System.Security.Cryptography;
using System.Text;
using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;
using Meridian.Domain.Properties;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence;

/// <summary>
/// The buildings and properties Meridian manages in the demo: those of the six forwarded client messages and every
/// address used in <c>eval/cases</c>. Names and addresses of the written variations are made up. Also the contractors
/// (all fictional, with phone numbers from Ofcom's drama range) and who is the usual contractor for each category, per
/// building and company-wide (R11, D12). Ids are derived from the names, so they are the same in every database.
/// </summary>
public static class SeedData
{
    /// <summary>
    /// Each building with its flats; an empty list means the building is one home (a house). Blocks have several
    /// flats so that a block named without a flat number stays ambiguous (AD-13).
    /// </summary>
    public static readonly IReadOnlyList<(string Building, IReadOnlyList<string> Flats)> Buildings = new (string, IReadOnlyList<string>)[]
    {
        // The six client messages (docs/input/client-input.md).
        ("22 Pemberton Road", Range(1, 6)),
        ("Alder Court", new[] { "14a", "14b", "15a", "15b", "16" }),
        ("Beechwood House", Range(1, 12)),
        ("8 Carlton Street", Array.Empty<string>()),

        // The written eval variations (eval/cases/var-*.json).
        ("18 Clarendon Road", Range(1, 8)),
        ("3 Ferndale Mews", Array.Empty<string>()),
        ("Kingfisher House", Range(1, 12)),
        ("41 Hollins Street", Range(1, 4)),
        ("17 Reed Lane", Array.Empty<string>()),
        ("Orchard Mill", Range(1, 14)),
        ("27 Ashby Grove", Array.Empty<string>()),
        ("9 Linden Court", Range(1, 4)),
        ("44 Mill Brow", Array.Empty<string>()),
        ("7 Kingsway Court", Range(1, 4)),
        ("60 Wilmslow Road", Range(1, 6)),
        ("Hawthorn House", Range(1, 10)),
        ("12 Primrose Bank", Array.Empty<string>()),
        ("5 Ladybarn Lane", Array.Empty<string>()),
        ("2 Egerton Street", Range(1, 4)),
        ("15 Cromwell Avenue", Range(1, 3)),
        ("9 Rusholme Place", Array.Empty<string>()),
        ("31 Albert Road", Range(1, 8)),
        ("21 Granby Row", Array.Empty<string>()),
        ("Elm Bank", Range(1, 12)),
        ("50 Upper Chorlton Road", Range(1, 6)),
        ("11 Station Approach", Range(1, 4)),
        ("5 Moss Lane", Array.Empty<string>()),
        ("Victoria Mill", Range(1, 8)),
        ("38 Brookfield Road", Array.Empty<string>()),
        ("4 Delamere Street", Array.Empty<string>()),
        ("Rowan Court", Range(1, 10)),
        ("30 Clarendon Road", Range(1, 6)),
        ("9 Wellington Grove", Range(1, 4)),
        ("6 Hope Street", Range(1, 3)),
        ("19 Thornbury Way", Array.Empty<string>()),
        ("Albany Court", Range(1, 12)),
        ("8 Wycliffe Street", Range(1, 4)),
        ("2 Laurel Close", Array.Empty<string>()),
        ("31 Dover Street", Range(1, 6)),
        ("27 Princess Street", Range(1, 8)),
        ("Pennine House", Range(1, 6)),
        ("16 Heaton Road", Array.Empty<string>()),
        ("33 Moorside Road", Array.Empty<string>()),
    };

    /// <summary>
    /// The contractors: one per category, plus a second heating and a second plumbing contractor, whom a person can
    /// choose in review (they are nobody's usual contractor).
    /// </summary>
    public static readonly IReadOnlyList<(string Name, Category Trade, string Phone)> Contractors = new[]
    {
        ("Northway Plumbing", Category.Plumbing, "07700 900101"),
        ("Brightline Electrical", Category.Electrical, "07700 900201"),
        ("Warmhold Heating", Category.Heating, "07700 900301"),
        ("Fixwell Appliance Repairs", Category.Appliances, "07700 900401"),
        ("Keystone Locksmiths", Category.LocksAndDoors, "07700 900501"),
        ("Clearway Pest Control", Category.Pest, "07700 900601"),
        ("Handyside Maintenance", Category.General, "07700 900701"),

        // Fallbacks.
        ("Irwell Pipe and Drain", Category.Plumbing, "07700 900102"),
        ("Cottonfield Boiler Services", Category.Heating, "07700 900302"),
    };

    /// <summary>The categories every seeded building has its own usual contractor for; the rest use the company default.</summary>
    public static readonly IReadOnlyList<Category> BuildingCategories =
        new[] { Category.Heating, Category.Plumbing, Category.Electrical, Category.General };

    /// <summary>
    /// The usual contractor per category: <c>Building</c> null is the company-wide default, used when the building has
    /// no assignment for the category or the property is unknown.
    /// </summary>
    public static readonly IReadOnlyList<(string? Building, Category Category, string Contractor)> Assignments =
        // The first contractor listed for a trade is its usual one.
        Contractors.DistinctBy(c => c.Trade).Select(c => ((string?)null, c.Trade, c.Name))
            .Concat(Buildings.SelectMany(b => BuildingCategories.Select(category =>
                ((string?)b.Building, category, Contractors.First(c => c.Trade == category).Name))))
            .ToList();

    /// <summary>The seeded contractors, as new entities.</summary>
    public static IReadOnlyList<Contractor> CreateContractors() =>
        Contractors.Select(c => Contractor.Create(IdFor("contractor", c.Name), c.Name, c.Trade, c.Phone)).ToList();

    /// <summary>The usual-contractor assignments as new entities; <paramref name="assignments"/> defaults to <see cref="Assignments"/>.</summary>
    public static IReadOnlyList<ContractorAssignment> CreateContractorAssignments(
        IEnumerable<(string? Building, Category Category, string Contractor)>? assignments = null) =>
        (assignments ?? Assignments)
            .Select(a => ContractorAssignment.Create(
                IdFor("assignment", $"{a.Building ?? "*"}|{a.Category}"),
                a.Building is null ? null : IdFor("building", a.Building),
                a.Category,
                IdFor("contractor", a.Contractor)))
            .ToList();

    /// <summary>The seeded buildings with their properties, as new entities.</summary>
    public static IReadOnlyList<Building> CreateBuildings()
    {
        var buildings = new List<Building>();
        foreach (var (name, flats) in Buildings)
        {
            var building = Building.Create(IdFor("building", name), name);
            if (flats.Count == 0)
            {
                building.AddWholeBuilding(IdFor("property", name));
            }
            else
            {
                foreach (var unit in flats)
                {
                    // A flat named by a number with a letter (14b) is written in front: "14b Alder Court".
                    var display = char.IsLetter(unit[^1]) ? $"{unit} {name}" : null;
                    building.AddFlat(IdFor("property", $"{unit}|{name}"), unit, display);
                }
            }

            buildings.Add(building);
        }

        return buildings;
    }

    /// <summary>
    /// Adds the buildings and properties when the database has none, and the contractors and their assignments when it
    /// has no contractors (run at startup, after migrations). Running it again changes nothing.
    /// </summary>
    public static Task EnsureSeededAsync(MeridianDbContext db, CancellationToken cancellationToken = default) =>
        EnsureSeededAsync(db, Assignments, cancellationToken);

    /// <summary>As <see cref="EnsureSeededAsync(MeridianDbContext, CancellationToken)"/>, with other assignments (tests).</summary>
    public static async Task EnsureSeededAsync(
        MeridianDbContext db,
        IEnumerable<(string? Building, Category Category, string Contractor)> assignments,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Buildings.AnyAsync(cancellationToken))
        {
            db.Buildings.AddRange(CreateBuildings());
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.Contractors.AnyAsync(cancellationToken))
        {
            db.Contractors.AddRange(CreateContractors());
            db.ContractorAssignments.AddRange(CreateContractorAssignments(assignments));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>A stable id from a name (first 16 bytes of its SHA-256), so seeded ids never change.</summary>
    public static Guid IdFor(string kind, string name) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"meridian-seed:{kind}:{name}"))[..16]);

    private static string[] Range(int first, int last) =>
        Enumerable.Range(first, last - first + 1).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
}
