using Meridian.Application.Abstractions;

namespace Meridian.Tests;

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTime utcNow) => UtcNow = utcNow;

    public DateTime UtcNow { get; set; }
}
