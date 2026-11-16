namespace Meridian.Application.Escalation;

/// <summary>A staff member who can be notified of and acknowledge an emergency.</summary>
/// <param name="Id">The acting-as id, recorded as the event actor and recipient, e.g. <c>rachel</c>.</param>
/// <param name="Name">How the person is shown, e.g. <c>Rachel</c>.</param>
/// <param name="Phone">The (fictional) number the simulated SMS goes to.</param>
public sealed record StaffMember(string Id, string Name, string Phone)
{
    /// <summary>The outbound recipient: name and number, e.g. <c>Rachel (07700 900461)</c>.</summary>
    public string Recipient => $"{Name} ({Phone})";
}

/// <summary>
/// The <c>Escalation</c> configuration (R18, R19): who is notified first, who is the backup, and how long each has to
/// acknowledge. Defaults are the seeded Rachel and Dawn with fictional numbers (Ofcom drama range) and 30 minutes.
/// </summary>
public sealed class EscalationOptions
{
    public const string SectionName = "Escalation";
    public const int DefaultWindowMinutes = 30;

    public static readonly StaffMember DefaultPrimary = new("rachel", "Rachel", "07700 900461");
    public static readonly StaffMember DefaultBackup = new("dawn", "Dawn", "07700 900872");

    public EscalationOptions(StaffMember primary, StaffMember backup, int windowMinutes)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(backup);
        Require(primary, nameof(primary));
        Require(backup, nameof(backup));
        if (string.Equals(primary.Id, backup.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The primary and the backup must be different people.", nameof(backup));
        }

        if (windowMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowMinutes), windowMinutes, "The window must be at least one minute.");
        }

        Primary = primary;
        Backup = backup;
        Window = TimeSpan.FromMinutes(windowMinutes);
    }

    public static EscalationOptions Default { get; } = new(DefaultPrimary, DefaultBackup, DefaultWindowMinutes);

    public StaffMember Primary { get; }

    public StaffMember Backup { get; }

    /// <summary>How long each person has to acknowledge before the next step.</summary>
    public TimeSpan Window { get; }

    /// <summary>The configured staff member with this acting-as id (case-insensitive), or null.</summary>
    public StaffMember? FindStaff(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var trimmed = id.Trim();
        return new[] { Primary, Backup }.FirstOrDefault(s => string.Equals(s.Id, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static void Require(StaffMember member, string paramName)
    {
        if (string.IsNullOrWhiteSpace(member.Id) || string.IsNullOrWhiteSpace(member.Name) || string.IsNullOrWhiteSpace(member.Phone))
        {
            throw new ArgumentException("A staff member needs an id, a name and a phone number.", paramName);
        }
    }
}
