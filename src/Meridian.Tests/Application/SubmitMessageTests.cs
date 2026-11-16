using Meridian.Application.Abstractions;
using Meridian.Application.Messages;
using Meridian.Application.Outbox;
using Meridian.Application.Triage;
using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;
using Meridian.Domain.Properties;
using Meridian.Infrastructure.Persistence;

namespace Meridian.Tests.Application;

public class SubmitMessageTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public async Task Blank_text_is_rejected_and_nothing_is_stored(string? text)
    {
        var (submit, jobs, uow) = Create();

        var result = await submit.ExecuteAsync(new SubmitMessageCommand("tenant@example.com", text), default);

        Assert.False(result.Succeeded);
        Assert.Contains("text", result.Errors.Keys);
        Assert.Empty(jobs.Added);
        Assert.Equal(0, uow.Executions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Blank_sender_is_rejected_and_nothing_is_stored(string? sender)
    {
        var (submit, jobs, uow) = Create();

        var result = await submit.ExecuteAsync(new SubmitMessageCommand(sender, "Boiler broken"), default);

        Assert.False(result.Succeeded);
        Assert.Contains("sender", result.Errors.Keys);
        Assert.Empty(jobs.Added);
        Assert.Equal(0, uow.Executions);
    }

    [Fact]
    public async Task Valid_message_creates_one_job_in_one_unit_of_work_using_the_clock()
    {
        var (submit, jobs, uow) = Create();
        const string text = " Mieszkanie 9, Beechwood House. ";

        var result = await submit.ExecuteAsync(new SubmitMessageCommand("a.nowak@example.com", text), default);

        Assert.True(result.Succeeded);
        var submitted = Assert.Single(result.Jobs);
        Assert.Equal("MER-000001", submitted.Reference);
        Assert.False(submitted.IsFollowUp);
        // One unit of work for the message, one for the jobs and model outcome.
        Assert.Equal(2, uow.Executions);
        var job = Assert.Single(jobs.Added);
        Assert.Equal(submitted.JobId, job.Id);
        Assert.Same(Assert.Single(jobs.Messages), Assert.Single(job.Messages));
        Assert.Equal(result.MessageId, job.Messages[0].Id);
        Assert.Equal(text, job.Messages[0].Text);
        Assert.Equal(Now, job.ReceivedAtUtc);
        Assert.All(job.Events, e => Assert.Equal(Now, e.OccurredAtUtc));
    }

    [Fact]
    public async Task The_message_is_committed_before_the_model_is_called_and_a_model_failure_still_makes_one_job()
    {
        var jobs = new FakeJobRepository();
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(Now);
        var model = new FakeTriageModel().Throws(new HttpRequestException("down"));
        int? executionsWhenCalled = null;
        int? messagesWhenCalled = null;
        model.OnCall = () =>
        {
            executionsWhenCalled = uow.Executions;
            messagesWhenCalled = jobs.Messages.Count;
        };
        var submit = new SubmitMessage(jobs, new FakePropertyRepository(), new FakeContractorRepository(), uow, clock, new PrepareJob(model), new AcknowledgeTenant(new FakeOutbox()));

        var result = await submit.ExecuteAsync(new SubmitMessageCommand("tenant@example.com", "Boiler broken"), default);

        Assert.True(result.Succeeded);
        Assert.Equal(1, executionsWhenCalled);
        Assert.Equal(1, messagesWhenCalled);
        var job = Assert.Single(jobs.Added);
        Assert.Equal(job.Id, Assert.Single(result.Jobs).JobId);
        Assert.Equal(Category.Unsure, job.Category);
        Assert.Null(job.PropertyId);
        Assert.Equal(EventType.ModelPreparationFailed, job.Events[^2].Type);
        Assert.Equal(EventType.AcknowledgementSent, job.Events[^1].Type);
    }

    private static (SubmitMessage Submit, FakeJobRepository Jobs, FakeUnitOfWork Uow) Create()
    {
        var jobs = new FakeJobRepository();
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(Now);
        var prepare = new PrepareJob(new FakeTriageModel());
        return (new SubmitMessage(jobs, new FakePropertyRepository(), new FakeContractorRepository(), uow, clock, prepare, new AcknowledgeTenant(new FakeOutbox())), jobs, uow);
    }

    internal sealed class FakeJobRepository : IJobRepository
    {
        private long _next;

        public List<Job> Added { get; } = new();

        public List<Message> Messages { get; } = new();

        public Task<long> NextJobNumberAsync(CancellationToken cancellationToken) => Task.FromResult(++_next);

        public void AddMessage(Message message) => Messages.Add(message);

        public void Add(Job job) => Added.Add(job);

        public Task<Job?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Added.SingleOrDefault(j => j.Id == id));

        public Task<IReadOnlyList<Job>> ListOpenAtPropertiesAsync(
            IReadOnlyCollection<Guid> propertyIds,
            DateTime openedSinceUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Job>>(Added
                .Where(j => j.PropertyId is { } id && propertyIds.Contains(id) && j.IsOpen && j.CreatedAtUtc >= openedSinceUtc)
                .ToList());

        public Task<IReadOnlyList<Message>> ListMessagesWithoutJobsAsync(DateTime receivedBeforeUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Message>>(Messages
                .Where(m => m.ReceivedAtUtc < receivedBeforeUtc && !Added.Any(j => j.Messages.Any(x => x.Id == m.Id)))
                .OrderBy(m => m.ReceivedAtUtc)
                .ToList());

        public Task<bool> MessageHasJobsAsync(Guid messageId, CancellationToken cancellationToken) =>
            Task.FromResult(Added.Any(j => j.Messages.Any(m => m.Id == messageId)));

        public Task<IReadOnlyList<Job>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Job>>(Added);
    }

    /// <summary>The seeded properties (<see cref="SeedData"/>), in memory.</summary>
    internal sealed class FakePropertyRepository : IPropertyRepository
    {
        public static readonly IReadOnlyList<Property> Seeded =
            SeedData.CreateBuildings().SelectMany(b => b.Properties).ToList();

        private readonly IReadOnlyList<Property> _properties;

        public FakePropertyRepository(IReadOnlyList<Property>? properties = null) => _properties = properties ?? Seeded;

        public static Property Named(string name) => Seeded.Single(p => p.Name == name);

        public Task<IReadOnlyList<Property>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(_properties);
    }

    /// <summary>The seeded contractors and assignments (<see cref="SeedData"/>), in memory.</summary>
    internal sealed class FakeContractorRepository : IContractorRepository
    {
        public static readonly IReadOnlyList<Contractor> Seeded = SeedData.CreateContractors();

        private readonly IReadOnlyList<ContractorAssignment> _assignments;

        public FakeContractorRepository(IReadOnlyList<ContractorAssignment>? assignments = null) =>
            _assignments = assignments ?? SeedData.CreateContractorAssignments();

        public static Contractor Named(string name) => Seeded.Single(c => c.Name == name);

        public Task<IReadOnlyList<Contractor>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(Seeded);

        public Task<IReadOnlyList<ContractorAssignment>> ListAssignmentsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_assignments);
    }

    internal sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Executions { get; private set; }

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
        {
            Executions++;
            return work(cancellationToken);
        }
    }
}
