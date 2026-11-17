using Meridian.Application.Outbox;
using Meridian.Application.Templates;
using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;
using Meridian.Domain.Outbox;

namespace Meridian.Application.Attendance;

/// <summary>
/// Sends a job to a contractor (AD-8, AD-11): a new token, the contractor message with its link written to the outbox,
/// and the job's <c>SentToContractor</c> change. Used by approval and by resend, inside their unit of work.
/// </summary>
internal static class ContractorDispatch
{
    /// <returns>The id of the outbound message written for the contractor.</returns>
    public static Guid Send(
        Job job,
        Contractor contractor,
        string? address,
        IOutbox outbox,
        string actor,
        DateTime nowUtc,
        bool resend)
    {
        var token = ContractorJobLink.NewToken();
        var body = ContractorTemplates.Render(
            job.Reference,
            address,
            job.Category.ToString(),
            job.SummaryEn,
            ContractorJobLink.PathFor(token));
        var outbound = outbox.Send(
            new OutboundRequest(job.Id, OutboundChannels.Sms, $"{contractor.Name} ({contractor.Phone})", ContractorTemplates.JobId, body),
            nowUtc);

        if (resend)
        {
            job.ResendToContractor(contractor.Id, token, outbound.Id, actor, nowUtc);
        }
        else
        {
            job.SendToContractor(contractor.Id, token, outbound.Id, actor, nowUtc);
        }

        return outbound.Id;
    }
}
