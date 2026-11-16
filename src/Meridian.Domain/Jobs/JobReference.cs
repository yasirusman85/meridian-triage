namespace Meridian.Domain.Jobs;

/// <summary>Human job reference shown to tenants: <c>MER-</c> plus a zero-padded six-digit sequence.</summary>
public static class JobReference
{
    public const string Prefix = "MER-";

    public static string FromNumber(long number)
    {
        if (number < 1 || number > 999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, "Job number must be between 1 and 999999.");
        }

        return Prefix + number.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
