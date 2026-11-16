namespace Meridian.Domain.Jobs;

/// <summary>Job lifecycle states (AD-6). Transitions beyond Prepared arrive in later stories.</summary>
public enum JobStatus
{
    Prepared,
    Approved,
    SentToContractor,
    Attended,
    Rejected,
}
