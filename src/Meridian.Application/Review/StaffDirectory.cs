namespace Meridian.Application.Review;

/// <summary>A person who can review jobs; <see cref="Id"/> is what the event log records as the actor.</summary>
public sealed record StaffMember(string Id, string Name);

/// <summary>
/// The people the "acting as" switch offers (no login in the first version). From config section <c>Staff</c>,
/// or <see cref="Default"/> (Rachel and Dawn) when it is empty.
/// </summary>
public sealed class StaffDirectory
{
    public static readonly IReadOnlyList<StaffMember> Default = new[]
    {
        new StaffMember("rachel", "Rachel"),
        new StaffMember("dawn", "Dawn"),
    };

    private readonly Dictionary<string, StaffMember> _byId;

    public StaffDirectory(IEnumerable<StaffMember>? members = null)
    {
        var list = (members ?? Array.Empty<StaffMember>())
            .Where(m => !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Name))
            .Select(m => new StaffMember(m.Id.Trim().ToLowerInvariant(), m.Name.Trim()))
            .ToList();
        if (list.Count == 0)
        {
            list = Default.ToList();
        }

        _byId = new Dictionary<string, StaffMember>(StringComparer.Ordinal);
        foreach (var member in list)
        {
            if (!_byId.TryAdd(member.Id, member))
            {
                throw new ArgumentException($"Staff id {member.Id} is listed more than once.", nameof(members));
            }
        }

        All = list;
    }

    public IReadOnlyList<StaffMember> All { get; }

    /// <summary>The staff member with this id (case ignored, surrounding spaces trimmed); null when missing or unknown.</summary>
    public StaffMember? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : _byId.GetValueOrDefault(id.Trim().ToLowerInvariant());
}
