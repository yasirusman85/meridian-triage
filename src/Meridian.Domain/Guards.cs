namespace Meridian.Domain;

internal static class Guards
{
    public static void RequireUtc(DateTime value, string paramName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Times must be UTC (AD-9).", paramName);
        }
    }
}
