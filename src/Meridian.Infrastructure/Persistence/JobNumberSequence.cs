namespace Meridian.Infrastructure.Persistence;

/// <summary>Single-row counter behind the MER- job reference.</summary>
public sealed class JobNumberSequence
{
    public const int SingletonId = 1;

    public int Id { get; set; }

    public long LastValue { get; set; }
}
