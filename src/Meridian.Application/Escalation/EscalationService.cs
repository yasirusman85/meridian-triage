using Meridian.Application.Abstractions;
using Meridian.Application.Outbox;
using Meridian.Application.Staff;
using Meridian.Application.Templates;
using Meridian.Domain.Jobs;
using Meridian.Domain.Outbox;

namespace Meridian.Application.Escalation;

/// <summary>One escalation step a tick took on a job.</summary>
/// <param name="Step"><c>notified-primary</c>, <c>notified-backup</c> or <c>failed</c>.</param>
public sealed record EscalationStep(Guid JobId, string Reference, string Step, Guid? OutboundId);

/// <summary>
/// Out-of-hours escalation (R18, R19, AD-9): an open Emergency job not yet acknowledged notifies the primary contact
/// at the next tick, the backup once the primary has not acknowledged within the window, and is marked
/// failed once the backup has not acknowledged within a further window. Plain code, no model call (AD-3); time only
/// from <see cref="IClock"/>. Each tick does at most one step per job and every step changes the job's state, so
/// running a tick twice sends nothing twice. Urgent and Routine jobs are never escalated; a job raised to Emergency
/// later (a follow-up) starts at the next tick.
/// </summary>
public sealed class EscalationService
{
    public const string NotifiedPrimaryStep = "notified-primary";
    public const string NotifiedBackupStep = "notified-backup";
    public const string FailedStep = "failed";

    private readonly IJobRepository _jobs;
    private readonly IPropertyRepository _properties;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IOutbox _outbox;
    private readonly StaffDirectory _staff;
    private readonly EscalationOptions _options;

    public EscalationService(
        IJobRepository jobs,
        IPropertyRepository properties,
        IUnitOfWork unitOfWork,
        IClock clock,
        IOutbox outbox,
        StaffDirectory staff,
        EscalationOptions options)
    {
        _jobs = jobs;
        _properties = properties;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _outbox = outbox;
        _staff = staff;
        _options = options;
    }

    /// <summary>
    /// Does every due escalation step, in one unit of work (the jobs are read inside it, so an acknowledgement saved
    /// meanwhile is seen and nothing is sent after it).
    /// </summary>
    /// <returns>The steps taken, in queue order; empty when nothing was due.</returns>
    public Task<IReadOnlyList<EscalationStep>> TickAsync(CancellationToken cancellationToken) =>
        _unitOfWork.ExecuteAsync<IReadOnlyList<EscalationStep>>(
            async ct =>
            {
                var now = _clock.UtcNow;
                var escalating = await _jobs.ListEscalatingAsync(ct);
                if (escalating.Count == 0)
                {
                    return Array.Empty<EscalationStep>();
                }

                var properties = (await _properties.ListAsync(ct)).ToDictionary(p => p.Id, p => p.Name);
                var steps = new List<EscalationStep>();
                foreach (var job in escalating.OrderBy(j => j.ReceivedAtUtc).ThenBy(j => j.Reference, StringComparer.Ordinal))
                {
                    if (Step(job, now, properties) is { } step)
                    {
                        steps.Add(step);
                    }
                }

                return steps;
            },
            cancellationToken);

    private EscalationStep? Step(Job job, DateTime now, IReadOnlyDictionary<Guid, string> properties)
    {
        if (!job.IsEscalating)
        {
            return null;
        }

        switch (job.EscalationState)
        {
            case EscalationState.None:
                return Notify(job, _staff.Primary, StaffTemplates.EscalationPrimaryId, NotifiedPrimaryStep, now, properties);
            case EscalationState.NotifiedRachel when now - job.EscalationPrimaryNotifiedAtUtc!.Value >= _options.Window:
                return Notify(job, _staff.Backup, StaffTemplates.EscalationBackupId, NotifiedBackupStep, now, properties);
            case EscalationState.NotifiedBackup when now - job.EscalationBackupNotifiedAtUtc!.Value >= _options.Window:
                job.FailEscalation(now);
                return new EscalationStep(job.Id, job.Reference, FailedStep, null);
            default:
                return null;
        }
    }

    private EscalationStep Notify(
        Job job,
        StaffMember staff,
        string templateId,
        string step,
        DateTime now,
        IReadOnlyDictionary<Guid, string> properties)
    {
        var address = job.PropertyId is { } propertyId ? properties.GetValueOrDefault(propertyId) : null;
        var body = StaffTemplates.Render(templateId, job.Reference, address, job.Category.ToString(), job.RuleIds);
        var outbound = _outbox.Send(new OutboundRequest(job.Id, OutboundChannels.Sms, staff.Recipient, templateId, body), now);
        job.NotifyEscalation(staff.Id, outbound.Id, now);
        return new EscalationStep(job.Id, job.Reference, step, outbound.Id);
    }
}
