using System.Security.Cryptography;
using Meridian.Domain.Jobs;

namespace Meridian.Application.Attendance;

/// <summary>
/// The single-job link a contractor opens (AD-11): <c>/c/&lt;token&gt;</c>, where the token is 32 random bytes as URL-safe
/// base64 without padding. Only the token's hash is stored on the job (<see cref="Job.HashContractorToken"/>).
/// </summary>
public static class ContractorJobLink
{
    public const string PathPrefix = "/c/";

    /// <summary>A new random token.</summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(Job.ContractorTokenBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>The path of the link for a token.</summary>
    public static string PathFor(string token) => PathPrefix + token;
}
