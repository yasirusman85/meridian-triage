using Meridian.Application.Escalation;

namespace Meridian.Web.Workers;

/// <summary>
/// Reads <see cref="EscalationOptions"/> from the <c>Escalation</c> section: <c>Escalation:Primary</c> and
/// <c>Escalation:Backup</c> (each <c>Id</c>, <c>Name</c>, <c>Phone</c>) and <c>Escalation:WindowMinutes</c>. A missing
/// value falls back to the seeded default (Rachel, Dawn, 30 minutes); an invalid one stops startup.
/// </summary>
public static class EscalationConfiguration
{
    public static EscalationOptions Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(EscalationOptions.SectionName);
        var windowText = Blank(section["WindowMinutes"]);
        var window = EscalationOptions.DefaultWindowMinutes;
        if (windowText is not null && !int.TryParse(windowText, out window))
        {
            throw new InvalidOperationException($"{EscalationOptions.SectionName}:WindowMinutes must be a whole number of minutes.");
        }

        return new EscalationOptions(
            Staff(section.GetSection("Primary"), EscalationOptions.DefaultPrimary),
            Staff(section.GetSection("Backup"), EscalationOptions.DefaultBackup),
            window);
    }

    private static StaffMember Staff(IConfigurationSection section, StaffMember fallback) =>
        new(
            Blank(section["Id"]) ?? fallback.Id,
            Blank(section["Name"]) ?? fallback.Name,
            Blank(section["Phone"]) ?? fallback.Phone);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
