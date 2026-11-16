using Meridian.Application.Abstractions;

namespace Meridian.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
