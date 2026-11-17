using Meridian.Application.Escalation;

namespace Meridian.Web.Configuration;

/// <summary>
/// Reads <see cref="EscalationOptions"/> from <c>Escalation:WindowMinutes</c>, a whole number of minutes (default 30).
/// Who is notified comes from <c>Staff</c> (<see cref="StaffConfiguration"/>). An invalid value stops startup.
/// </summary>
public static class EscalationConfiguration
{
    public static EscalationOptions Read(IConfiguration configuration)
    {
        var text = Blank(configuration[$"{EscalationOptions.SectionName}:WindowMinutes"]);
        if (text is null)
        {
            return EscalationOptions.Default;
        }

        return int.TryParse(text, out var minutes)
            ? new EscalationOptions(minutes)
            : throw new InvalidOperationException($"{EscalationOptions.SectionName}:WindowMinutes must be a whole number of minutes.");
    }

    internal static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
