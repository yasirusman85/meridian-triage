using Meridian.Application.Abstractions;
using Meridian.Application.Messages;
using Meridian.Domain.Rules;
using Meridian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Web.Demo;

/// <summary>
/// The demo seed (<c>Demo:SeedClientMessages</c>): on an empty database, the six client messages go through the same
/// <see cref="SubmitMessage"/> use case as a pasted message, in their original order, so the queue shows five prepared
/// jobs without an API key (the stub answers them). Each message is received at the most recent past occurrence of the
/// weekday and time it shows, in Europe/London, so the Friday-evening Walsh messages land in winter and stay in order.
/// Runs once: a database that already has messages is left alone.
/// </summary>
public static class DemoSeed
{
    public const string ConfigurationKey = "Demo:SeedClientMessages";

    /// <summary>Submits the demo messages when the database has none.</summary>
    /// <returns>The jobs made, in message order; empty when nothing was seeded.</returns>
    public static async Task<IReadOnlyList<SubmittedJob>> SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using (var check = services.CreateAsyncScope())
        {
            var db = check.ServiceProvider.GetRequiredService<MeridianDbContext>();
            if (await db.Messages.AnyAsync(cancellationToken))
            {
                return Array.Empty<SubmittedJob>();
            }
        }

        var now = services.GetRequiredService<IClock>().UtcNow;
        var made = new List<SubmittedJob>();
        foreach (var message in DemoClientMessages.All)
        {
            // A scope per message: its own database context, and a clock fixed to this message's received time.
            await using var scope = services.CreateAsyncScope();
            var clock = new FixedClock(ReceivedAtUtc(message, now));
            var submit = ActivatorUtilities.CreateInstance<SubmitMessage>(scope.ServiceProvider, clock);
            var result = await submit.ExecuteAsync(new SubmitMessageCommand(message.Sender, message.Text), cancellationToken);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"The demo message from {message.Sender} was refused: {string.Join("; ", result.Errors.Keys)}.");
            }

            made.AddRange(result.Jobs);
        }

        return made;
    }

    /// <summary>
    /// The most recent moment before <paramref name="nowUtc"/> when the message's weekday and local time came round,
    /// as UTC. Today counts only if its time has already passed.
    /// </summary>
    public static DateTime ReceivedAtUtc(DemoClientMessages.DemoMessage message, DateTime nowUtc)
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, EmergencyRules.London);
        var daysBack = ((int)localNow.DayOfWeek - (int)message.Day + 7) % 7;
        var local = localNow.Date.AddDays(-daysBack).Add(message.LocalTime.ToTimeSpan());
        if (local > localNow)
        {
            local = local.AddDays(-7);
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), EmergencyRules.London);
    }

    /// <summary>A clock that always says the same moment: the received time of one demo message.</summary>
    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTime utc)
        {
            UtcNow = utc;
        }

        public DateTime UtcNow { get; }
    }
}
