namespace Meridian.Application.Escalation;

/// <summary>
/// The <c>Escalation</c> configuration (R18, R19): how long each person has to acknowledge an emergency before the next
/// step. Who is notified comes from the staff list (<see cref="Meridian.Application.Staff.StaffDirectory"/>). The default is 30 minutes.
/// </summary>
public sealed class EscalationOptions
{
    public const string SectionName = "Escalation";
    public const int DefaultWindowMinutes = 30;

    public EscalationOptions(int windowMinutes)
    {
        if (windowMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowMinutes), windowMinutes, "The window must be at least one minute.");
        }

        WindowMinutes = windowMinutes;
        Window = TimeSpan.FromMinutes(windowMinutes);
    }

    public static EscalationOptions Default { get; } = new(DefaultWindowMinutes);

    public int WindowMinutes { get; }

    /// <summary>How long each person has to acknowledge before the next step.</summary>
    public TimeSpan Window { get; }
}
