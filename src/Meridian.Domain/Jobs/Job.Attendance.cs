using System.Security.Cryptography;
using System.Text;
using Meridian.Domain.Contractors;

namespace Meridian.Domain.Jobs;

/// <summary>
/// Contractor attendance (AD-6, AD-11, R20, R21). A sent job carries a single-job capability token: only its SHA-256
/// hash is stored on the job, and the raw token lives only in the outbound message and its link. Marking the job
/// attended, or resending it, invalidates the token it was sent with.
/// </summary>
public sealed partial class Job
{
    /// <summary>Random bytes in a contractor token before URL-safe base64 encoding.</summary>
    public const int ContractorTokenBytes = 32;

    /// <summary>Length of a stored token hash: SHA-256 as hex.</summary>
    public const int ContractorTokenHashLength = 64;

    /// <summary>The emergency promise (RULE-8): attended within this long of the first message.</summary>
    public static readonly TimeSpan EmergencyAttendanceWindow = TimeSpan.FromHours(24);

    /// <summary>After a resend to a fallback contractor, the alert re-raises only if the job is still not attended this long after it.</summary>
    public static readonly TimeSpan ResendAttendanceWindow = TimeSpan.FromHours(4);

    /// <summary>SHA-256 of the contractor token, hex; null when no token is live (never sent, attended).</summary>
    public string? ContractorTokenHash { get; private set; }

    /// <summary>The contractor the job was last sent to; null until sent.</summary>
    public Guid? SentToContractorId { get; private set; }

    /// <summary>When the job was last re-sent to a fallback contractor (UTC); null while it has only had its first send.</summary>
    public DateTime? ContractorResentAtUtc { get; private set; }

    /// <summary>When the contractor marked the job attended (UTC); null until then.</summary>
    public DateTime? AttendedAtUtc { get; private set; }

    /// <summary>When the attendance-overdue alert was raised for the current send (UTC); null while not flagged.</summary>
    public DateTime? AttendanceOverdueFlaggedAtUtc { get; private set; }

    /// <summary>The stored form of a contractor token: SHA-256 of its UTF-8 bytes, as 64 hex characters.</summary>
    public static string HashContractorToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("A contractor token is required.", nameof(token));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>True when <paramref name="token"/> is the live token of this job.</summary>
    public bool HasContractorToken(string token) =>
        !string.IsNullOrWhiteSpace(token)
        && ContractorTokenHash is { } hash
        && string.Equals(hash, HashContractorToken(token), StringComparison.Ordinal);

    /// <summary>
    /// Sends an Approved job to its contractor (AD-6: Approved → SentToContractor), in the same unit of work as the
    /// approval. Stores the token's hash and appends <see cref="EventType.SentToContractor"/> with the contractor and
    /// the outbound message the link was sent in.
    /// </summary>
    /// <param name="token">The raw token; only its hash is kept.</param>
    /// <param name="outboundId">The outbound message written for the contractor (AD-8).</param>
    /// <param name="actor">The person who approved or resent the job.</param>
    /// <exception cref="JobStateException">The job is not Approved.</exception>
    public void SendToContractor(Guid contractorId, string token, Guid outboundId, string actor, DateTime nowUtc)
    {
        RequirePersonActor(actor);
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        if (Status != JobStatus.Approved)
        {
            throw new JobStateException($"Job {Reference} is {Status} and cannot be sent to a contractor.", Status);
        }

        RequireSendArguments(contractorId, token, outboundId);
        Status = JobStatus.SentToContractor;
        Dispatch(contractorId, token, outboundId, actor, nowUtc);
    }

    /// <summary>
    /// Sends a job that is already SentToContractor to another contractor (the person's choice of fallback). The old
    /// token stops working, the new one is stored, the overdue flag is cleared, and <see cref="EventType.SentToContractor"/>
    /// is appended again. The status stays SentToContractor.
    /// </summary>
    /// <exception cref="JobStateException">The job is not SentToContractor.</exception>
    public void ResendToContractor(Guid contractorId, string token, Guid outboundId, string actor, DateTime nowUtc)
    {
        RequirePersonActor(actor);
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        if (Status != JobStatus.SentToContractor)
        {
            throw new JobStateException($"Job {Reference} is {Status} and cannot be re-sent to a contractor.", Status);
        }

        RequireSendArguments(contractorId, token, outboundId);
        AttendanceOverdueFlaggedAtUtc = null;
        ContractorResentAtUtc = nowUtc;
        Dispatch(contractorId, token, outboundId, actor, nowUtc);
    }

    /// <summary>
    /// The contractor marks the job attended through its link (AD-6: SentToContractor → Attended). The token is
    /// invalidated and <see cref="EventType.JobAttended"/> is appended with the actor <c>contractor:&lt;id&gt;</c>.
    /// </summary>
    /// <exception cref="JobStateException">The job is not SentToContractor.</exception>
    public void MarkAttended(DateTime nowUtc)
    {
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        if (Status != JobStatus.SentToContractor || SentToContractorId is not { } contractorId)
        {
            throw new JobStateException($"Job {Reference} is {Status} and cannot be marked attended.", Status);
        }

        var from = Status;
        Status = JobStatus.Attended;
        AttendedAtUtc = nowUtc;
        ContractorTokenHash = null;
        Append(
            EventType.JobAttended,
            nowUtc,
            ContractorActor(contractorId),
            new { from = from.ToString(), to = Status.ToString(), contractorId });
    }

    /// <summary>
    /// True when an Emergency job sent to a contractor is overdue and has not been flagged for its current send (RULE-8,
    /// AD-9): never re-sent and more than 24 hours past its first message, or re-sent and more than 4 hours past the
    /// resend (the 24-hour promise itself stays measured from the first message).
    /// </summary>
    public bool IsAttendanceOverdue(DateTime nowUtc) =>
        Urgency == Urgency.Emergency
        && Status == JobStatus.SentToContractor
        && AttendanceOverdueFlaggedAtUtc is null
        && (ContractorResentAtUtc is { } resentAt
            ? nowUtc - resentAt > ResendAttendanceWindow
            : nowUtc - ReceivedAtUtc > EmergencyAttendanceWindow);

    /// <summary>
    /// Raises the attendance-overdue alert once (the worker's check, AD-9): appends <see cref="EventType.AttendanceOverdue"/>
    /// and sets the flag. Running it again changes nothing.
    /// </summary>
    /// <returns>True when the alert was raised now.</returns>
    public bool FlagAttendanceOverdue(DateTime nowUtc)
    {
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        if (!IsAttendanceOverdue(nowUtc))
        {
            return false;
        }

        AttendanceOverdueFlaggedAtUtc = nowUtc;
        Append(
            EventType.AttendanceOverdue,
            nowUtc,
            JobEvent.SystemActor,
            new { reference = Reference, contractorId = SentToContractorId, firstMessageAtUtc = ReceivedAtUtc });
        return true;
    }

    /// <summary>
    /// The rule behind the contractor the job is with: <see cref="SuggestedContractorRule"/> while the job was sent to the
    /// contractor first suggested for it, otherwise null (a fallback chosen by a person has no suggestion rule).
    /// </summary>
    public ContractorRule? SentContractorRule =>
        SentToContractorId is null || SentToContractorId == SuggestedContractorId ? SuggestedContractorRule : null;

    /// <summary>The actor written for a contractor's own action: <c>contractor:&lt;id&gt;</c>.</summary>
    public static string ContractorActor(Guid contractorId) => "contractor:" + contractorId.ToString("D");

    private static void RequireSendArguments(Guid contractorId, string token, Guid outboundId)
    {
        if (contractorId == Guid.Empty)
        {
            throw new ArgumentException("A contractor id is required.", nameof(contractorId));
        }

        if (outboundId == Guid.Empty)
        {
            throw new ArgumentException("An outbound message id is required.", nameof(outboundId));
        }

        HashContractorToken(token);
    }

    private void Dispatch(Guid contractorId, string token, Guid outboundId, string actor, DateTime nowUtc)
    {
        ContractorTokenHash = HashContractorToken(token);
        SentToContractorId = contractorId;
        Append(
            EventType.SentToContractor,
            nowUtc,
            actor,
            new { contractorId, outboundId });
    }
}
