namespace Meridian.Application.Staff;

/// <summary>The escalation role: the first contact, or the backup who is told when the first contact does not acknowledge in time.</summary>
public enum StaffRole
{
    Primary,
    Backup,
}

/// <summary>
/// A person in the office. <see cref="Id"/> is the acting-as key the SPA sends and the event log records as the actor;
/// <see cref="Phone"/> is where the simulated SMS for an emergency goes (fictional numbers, Ofcom drama range).
/// </summary>
public sealed record StaffMember(string Id, string Name, string Phone, StaffRole Role)
{
    /// <summary>The outbound recipient: name and number, e.g. <c>Rachel (07700 900461)</c>.</summary>
    public string Recipient => $"{Name} ({Phone})";
}

/// <summary>
/// The one list of staff (no login in the first version): the "acting as" switch offers everyone in it, and escalation
/// notifies <see cref="Primary"/> and then <see cref="Backup"/>. Built from config section <c>Staff</c>, or
/// <see cref="Default"/> (Rachel primary, Dawn backup) when that is empty. Exactly one primary and one backup, with
/// different ids, are required.
/// </summary>
public sealed class StaffDirectory
{
    public static readonly IReadOnlyList<StaffMember> Default = new[]
    {
        new StaffMember("rachel", "Rachel", "07700 900461", StaffRole.Primary),
        new StaffMember("dawn", "Dawn", "07700 900872", StaffRole.Backup),
    };

    private readonly Dictionary<string, StaffMember> _byId;

    public StaffDirectory(IEnumerable<StaffMember>? members = null)
    {
        var list = (members ?? Array.Empty<StaffMember>())
            .Select(m => m with { Id = m.Id.Trim().ToLowerInvariant(), Name = m.Name.Trim(), Phone = m.Phone.Trim() })
            .ToList();
        if (list.Count == 0)
        {
            list = Default.ToList();
        }

        _byId = new Dictionary<string, StaffMember>(StringComparer.Ordinal);
        foreach (var member in list)
        {
            if (member.Id.Length == 0 || member.Name.Length == 0 || member.Phone.Length == 0)
            {
                throw new ArgumentException("A staff member needs an id, a name and a phone number.", nameof(members));
            }

            if (!_byId.TryAdd(member.Id, member))
            {
                throw new ArgumentException($"Staff id {member.Id} is listed more than once.", nameof(members));
            }
        }

        Primary = Single(list, StaffRole.Primary);
        Backup = Single(list, StaffRole.Backup);
        if (Primary.Id == Backup.Id)
        {
            throw new ArgumentException("The primary and the backup must be different people.", nameof(members));
        }

        All = list;
    }

    /// <summary>Everyone the acting-as switch offers, in configured order.</summary>
    public IReadOnlyList<StaffMember> All { get; }

    /// <summary>The first contact for an emergency.</summary>
    public StaffMember Primary { get; }

    /// <summary>Told when the primary has not acknowledged within the escalation window.</summary>
    public StaffMember Backup { get; }

    /// <summary>The staff member with this id (case ignored, surrounding spaces trimmed); null when missing or unknown.</summary>
    public StaffMember? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : _byId.GetValueOrDefault(id.Trim().ToLowerInvariant());

    private static StaffMember Single(IReadOnlyList<StaffMember> list, StaffRole role)
    {
        var matches = list.Where(m => m.Role == role).ToList();
        return matches.Count == 1
            ? matches[0]
            : throw new ArgumentException($"Exactly one staff member must have the role {role}; found {matches.Count}.", nameof(list));
    }
}
