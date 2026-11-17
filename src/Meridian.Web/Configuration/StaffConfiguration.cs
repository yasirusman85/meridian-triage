using Meridian.Application.Staff;

namespace Meridian.Web.Configuration;

/// <summary>
/// Reads the one staff list from the <c>Staff</c> array: each entry has <c>Id</c> (the acting-as key), <c>Name</c>,
/// <c>Phone</c> and <c>Role</c> (<c>Primary</c> or <c>Backup</c>). An empty list gives <see cref="StaffDirectory.Default"/>.
/// An invalid entry stops startup, naming the entry.
/// </summary>
public static class StaffConfiguration
{
    public const string SectionName = "Staff";

    public static StaffDirectory Read(IConfiguration configuration)
    {
        var members = new List<StaffMember>();
        var entries = configuration.GetSection(SectionName).GetChildren().ToList();
        foreach (var (entry, index) in entries.Select((e, i) => (e, i)))
        {
            var role = EscalationConfiguration.Blank(entry["Role"]);
            if (role is null || !Enum.TryParse<StaffRole>(role, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                throw new InvalidOperationException($"{SectionName}:{index}:Role must be Primary or Backup.");
            }

            members.Add(new StaffMember(
                EscalationConfiguration.Blank(entry["Id"]) ?? string.Empty,
                EscalationConfiguration.Blank(entry["Name"]) ?? string.Empty,
                EscalationConfiguration.Blank(entry["Phone"]) ?? string.Empty,
                parsed));
        }

        return new StaffDirectory(members);
    }
}
