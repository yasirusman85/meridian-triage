using Meridian.Application.Attendance;
using Meridian.Application.Escalation;
using Meridian.Application.Messages;

namespace Meridian.Web.Workers;

/// <summary>
/// The background worker (AD-9): once at start and then every minute, runs every due escalation step
/// (<see cref="EscalationService.TickAsync"/>), flags emergencies sent to a contractor that are past 24 hours
/// (<see cref="CheckOverdueAttendance"/>, RULE-8), and recovers orphan messages older than
/// <see cref="RecoverOrphanMessages.DefaultMinimumAge"/> (RULE-11). Each run uses its own scope; a failing run is
/// logged and the next minute tries again (every step is idempotent).
/// </summary>
public sealed class EscalationWorker : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EscalationWorker> _logger;

    public EscalationWorker(IServiceScopeFactory scopes, ILogger<EscalationWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting before the first run.
        await Task.Yield();
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    /// <summary>One tick: escalation, the overdue attendance check, then orphan recovery. Exceptions other than shutdown are logged, not thrown.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var steps = await scope.ServiceProvider.GetRequiredService<EscalationService>().TickAsync(cancellationToken);
            foreach (var step in steps)
            {
                _logger.LogInformation("Escalation {Step} for job {Reference}", step.Step, step.Reference);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Escalation tick failed; retrying next minute");
        }

        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            foreach (var reference in await scope.ServiceProvider.GetRequiredService<CheckOverdueAttendance>().ExecuteAsync(cancellationToken))
            {
                _logger.LogWarning("Attendance overdue for job {Reference}", reference);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Attendance check failed; retrying next minute");
        }

        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var recovered = await scope.ServiceProvider.GetRequiredService<RecoverOrphanMessages>()
                .ExecuteAsync(RecoverOrphanMessages.DefaultMinimumAge, cancellationToken);
            if (recovered.Count > 0)
            {
                _logger.LogWarning("Recovered {Count} orphan message(s)", recovered.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Orphan recovery failed; retrying next minute");
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
