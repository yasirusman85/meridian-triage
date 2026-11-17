namespace Meridian.Domain.Jobs;

/// <summary>
/// Job lifecycle states (AD-6): <c>Prepared → Approved → SentToContractor → Attended</c>, plus <c>Rejected</c> from
/// <c>Prepared</c>. Review moves a job from Prepared to Approved or Rejected; approval sends it to its contractor, and the contractor's
/// link marks it Attended.
/// </summary>
public enum JobStatus
{
    Prepared,
    Approved,
    SentToContractor,
    Attended,
    Rejected,
}
