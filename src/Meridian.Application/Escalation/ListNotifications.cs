using Meridian.Application.Abstractions;
using Meridian.Application.Outbox;
using Meridian.Application.Templates;
using Meridian.Domain.Outbox;

namespace Meridian.Application.Escalation;

/// <summary>A staff notification with the reference of the job it is about.</summary>
public sealed record StaffNotification(OutboundMessage Message, string? JobReference);

/// <summary>Staff notifications written to the outbox (escalation SMS, simulated), newest first, for the notifications panel.</summary>
public sealed class ListNotifications
{
    private readonly IOutbox _outbox;
    private readonly IJobRepository _jobs;

    public ListNotifications(IOutbox outbox, IJobRepository jobs)
    {
        _outbox = outbox;
        _jobs = jobs;
    }

    public async Task<IReadOnlyList<StaffNotification>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var messages = await _outbox.ListByTemplatesAsync(StaffTemplates.Ids, cancellationToken);
        if (messages.Count == 0)
        {
            return Array.Empty<StaffNotification>();
        }

        var references = (await _jobs.ListAsync(cancellationToken)).ToDictionary(j => j.Id, j => j.Reference);
        return messages.Select(m => new StaffNotification(m, references.GetValueOrDefault(m.JobId))).ToList();
    }
}
