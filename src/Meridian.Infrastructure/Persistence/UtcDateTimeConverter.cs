using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Meridian.Infrastructure.Persistence;

/// <summary>Stores every DateTime as UTC and reads it back with Kind = Utc (AD-9).</summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(v => ToUtc(v), v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
