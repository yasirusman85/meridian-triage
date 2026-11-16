namespace Meridian.Application.Abstractions;

/// <summary>The only source of time (AD-9). Always returns UTC.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
