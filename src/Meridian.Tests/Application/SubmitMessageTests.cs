using Meridian.Application.Abstractions;
using Meridian.Application.Messages;
using Meridian.Application.Triage;
using Meridian.Domain.Jobs;

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
        Assert.Equal("MER-000001", result.Reference);
        // One unit of work for the message and job, one for the model outcome.
        Assert.Equal(2, uow.Executions);
        var job = Assert.Single(jobs.Added);
        Assert.Equal(result.JobId, job.Id);
        Assert.Equal(text, Assert.Single(job.Messages).Text);
        Assert.Equal(Now, job.ReceivedAtUtc);
        Assert.All(job.Events, e => Assert.Equal(Now, e.OccurredAtUtc));
    }

    [Fact]
    public async Task Message_and_job_are_committed_before_the_model_is_called_and_a_model_failure_still_returns_the_job()
    {
        var jobs = new FakeJobRepository();
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(Now);
        var model = new FakeTriageModel().Throws(new HttpRequestException("down"));
        int? executionsWhenCalled = null;
        int? jobsWhenCalled = null;
        model.OnCall = () =>
        {
            executionsWhenCalled = uow.Executions;
            jobsWhenCalled = jobs.Added.Count;
        };
        var submit = new SubmitMessage(jobs, uow, clock, new PrepareJob(model, jobs, uow, clock));

        var result = await submit.ExecuteAsync(new SubmitMessageCommand("tenant@example.com", "Boiler broken"), default);

        Assert.True(result.Succeeded);
        Assert.Equal(1, executionsWhenCalled);
        Assert.Equal(1, jobsWhenCalled);
        var job = Assert.Single(jobs.Added);
        Assert.Equal(EventType.ModelPreparationFailed, job.Events[^1].Type);
    }

    private static (SubmitMessage Submit, FakeJobRepository Jobs, FakeUnitOfWork Uow) Create()
    {
        var jobs = new FakeJobRepository();
        var uow = new FakeUnitOfWork();
        var clock = new FakeClock(Now);
        var prepare = new PrepareJob(new FakeTriageModel(), jobs, uow, clock);
        return (new SubmitMessage(jobs, uow, clock, prepare), jobs, uow);
    }

    internal sealed class FakeJobRepository : IJobRepository
    {
        private long _next;

        public List<Job> Added { get; } = new();

        public Task<long> NextJobNumberAsync(CancellationToken cancellationToken) => Task.FromResult(++_next);

        public void Add(Job job) => Added.Add(job);

        public Task<Job?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Added.SingleOrDefault(j => j.Id == id));

        public Task<IReadOnlyList<Job>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Job>>(Added);
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
