using Meridian.Application.Abstractions;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence;

internal sealed class JobRepository : IJobRepository
{
    private readonly MeridianDbContext _db;

    public JobRepository(MeridianDbContext db)
    {
        _db = db;
    }

    public async Task<long> NextJobNumberAsync(CancellationToken cancellationToken)
    {
        // One atomic statement: the UPDATE takes SQLite's write lock, so two writers never get the same number.
        var values = await _db.Database
            .SqlQueryRaw<long>(
                "UPDATE \"JobNumberSequence\" SET \"LastValue\" = \"LastValue\" + 1 WHERE \"Id\" = {0} RETURNING \"LastValue\" AS \"Value\"",
                JobNumberSequence.SingletonId)
            .ToListAsync(cancellationToken);

        return values.Count == 1
            ? values[0]
            : throw new InvalidOperationException("Job number sequence row is missing.");
    }

    public void AddMessage(Message message) => _db.Messages.Add(message);

    public void Add(Job job) => _db.Jobs.Add(job);

    public Task<Job?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Jobs
            .Include(j => j.Messages)
            .Include(j => j.Events)
            .AsSplitQuery()
            .SingleOrDefaultAsync(j => j.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Job>> ListOpenAtPropertiesAsync(
        IReadOnlyCollection<Guid> propertyIds,
        DateTime openedSinceUtc,
        CancellationToken cancellationToken)
    {
        if (propertyIds.Count == 0)
        {
            return Array.Empty<Job>();
        }

        var ids = propertyIds.Cast<Guid?>().ToList();
        // Times are compared in memory: the UTC converter stores ISO text, so a SQL comparison would be on strings.
        var jobs = await _db.Jobs
            .Include(j => j.Messages)
            .Include(j => j.Events)
            .AsSplitQuery()
            .Where(j => ids.Contains(j.PropertyId))
            .Where(j => j.Status != JobStatus.Attended && j.Status != JobStatus.Rejected)
            .ToListAsync(cancellationToken);
        return jobs.Where(j => j.CreatedAtUtc >= openedSinceUtc).ToList();
    }

    public async Task<IReadOnlyList<Message>> ListMessagesWithoutJobsAsync(
        DateTime receivedBeforeUtc,
        CancellationToken cancellationToken)
    {
        var messages = await _db.Messages
            .Where(m => !_db.Jobs.Any(j => j.Messages.Any(x => x.Id == m.Id)))
            .ToListAsync(cancellationToken);

        // Times are compared in memory, as above.
        return messages
            .Where(m => m.ReceivedAtUtc < receivedBeforeUtc)
            .OrderBy(m => m.ReceivedAtUtc)
            .ThenBy(m => m.Id)
            .ToList();
    }

    public Task<bool> MessageHasJobsAsync(Guid messageId, CancellationToken cancellationToken) =>
        _db.Jobs.AnyAsync(j => j.Messages.Any(m => m.Id == messageId), cancellationToken);

    public async Task<IReadOnlyList<Job>> ListEscalatingAsync(CancellationToken cancellationToken)
    {
        var jobs = await _db.Jobs
            .Include(j => j.Events)
            .Where(j => j.Urgency == Urgency.Emergency)
            .Where(j => j.Status != JobStatus.Attended && j.Status != JobStatus.Rejected)
            .Where(j => j.EscalationState == EscalationState.None
                || j.EscalationState == EscalationState.NotifiedRachel
                || j.EscalationState == EscalationState.NotifiedBackup)
            .ToListAsync(cancellationToken);
        return jobs.Where(j => j.IsEscalating).ToList();
    }

    public Task<Job?> GetByContractorTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        _db.Jobs
            .Include(j => j.Events)
            .AsSplitQuery()
            .SingleOrDefaultAsync(j => j.ContractorTokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<Job>> ListAwaitingAttendanceAsync(CancellationToken cancellationToken) =>
        await _db.Jobs
            .Include(j => j.Events)
            .Where(j => j.Urgency == Urgency.Emergency)
            .Where(j => j.Status == JobStatus.SentToContractor)
            .Where(j => j.AttendanceOverdueFlaggedAtUtc == null)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Job>> ListAsync(CancellationToken cancellationToken) =>
        await _db.Jobs
            .AsNoTracking()
            .Include(j => j.Messages)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Job>> ListWithEventsAsync(CancellationToken cancellationToken) =>
        await _db.Jobs
            .AsNoTracking()
            .Include(j => j.Events)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
}
