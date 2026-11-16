namespace Meridian.Domain.Jobs;

/// <summary>
/// Job lifecycle states (AD-6): <c>Prepared → Approved → SentToContractor → Attended</c>, plus <c>Rejected</c> from
/// <c>Prepared</c>. Review moves a job from Prepared to Approved or Rejected; later stories add the rest.
/// </summary>
public enum JobStatus
{
    Prepared,
    Approved,
    SentToContractor,
    Attended,
    Rejected,
}
