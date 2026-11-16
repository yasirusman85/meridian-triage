namespace Meridian.Domain.Jobs;

/// <summary>Ordered Routine &lt; Urgent &lt; Emergency (AD-4). The numeric values carry the order.</summary>
public enum Urgency
{
    Routine = 0,
    Urgent = 1,
    Emergency = 2,
}
