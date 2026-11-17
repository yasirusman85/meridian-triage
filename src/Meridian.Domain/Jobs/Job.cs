using System.Text.Json;
using Meridian.Domain.Contractors;
using Meridian.Domain.Messages;
using Meridian.Domain.Rules;

namespace Meridian.Domain.Jobs;

/// <summary>
/// One unit of maintenance work. State changes only through methods on this class,
/// and every change appends a <see cref="JobEvent"/> (AD-5).
/// </summary>
public sealed partial class Job
{
    private readonly List<Message> _messages = new();
    private readonly List<JobEvent> _events = new();

    /// <summary>Fired rule ids, comma-separated, in the order they first fired; stored as the <c>RuleIds</c> column.</summary>
    private string _ruleIds = string.Empty;

    private Job()
    {
        Reference = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Reference { get; private set; }

    public JobStatus Status { get; private set; }

    public Urgency Urgency { get; private set; }

    public Category Category { get; private set; }

    /// <summary>The property the job is at, once <c>PropertyResolver</c> matched one (AD-13); null when unresolved.</summary>
    public Guid? PropertyId { get; private set; }

    /// <summary>The contractor <c>ContractorLookup</c> suggested for the job's category and building; null means "needs a person".</summary>
    public Guid? SuggestedContractorId { get; private set; }

    /// <summary>Whether <see cref="SuggestedContractorId"/> is the building's usual contractor or the company default; null without a suggestion.</summary>
    public ContractorRule? SuggestedContractorRule { get; private set; }

    /// <summary>When the job's first message was received (UTC).</summary>
    public DateTime ReceivedAtUtc { get; private set; }

    /// <summary>When the job was opened (UTC).</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Why the job was prepared this way, from the model's proposal (R7). Null until prepared.</summary>
    public string? Explanation { get; private set; }

    /// <summary>English summary of the issue, from the model's proposal. Null until prepared.</summary>
    public string? SummaryEn { get; private set; }

    /// <summary>Language of the tenant's message as reported by the model (e.g. <c>pl</c>). Null until prepared.</summary>
    public string? Language { get; private set; }

    /// <summary>Ids of the emergency rules that fired on this job's messages (see <see cref="EmergencyRuleIds"/>), for the job view.</summary>
    public IReadOnlyList<string> RuleIds =>
        _ruleIds.Length == 0 ? Array.Empty<string>() : _ruleIds.Split(RuleIdSeparator);

    /// <summary>The job's messages: the first plus any follow-ups. A message may also belong to other jobs.</summary>
    public IReadOnlyList<Message> Messages => _messages;

    /// <summary>Messages attached after the first one (R8).</summary>
    public int FollowUpCount => Math.Max(0, _messages.Count - 1);

    /// <summary>True while the job can still take follow-ups: not <see cref="JobStatus.Attended"/> or <see cref="JobStatus.Rejected"/>.</summary>
    public bool IsOpen => Status is not (JobStatus.Attended or JobStatus.Rejected);

    /// <summary>The job's history in <see cref="JobEvent.Sequence"/> order, whatever order the events were loaded in.</summary>
    public IReadOnlyList<JobEvent> Events => _events.OrderBy(e => e.Sequence).ToList();

    /// <summary>
    /// Creates a job for one issue of a received message (or the message as a whole when there is no usable issue):
    /// status Prepared, category Unsure, urgency Routine. Appends <see cref="EventType.MessageReceived"/> then
    /// <see cref="EventType.JobPrepared"/>. A message can start several jobs.
    /// </summary>
    /// <param name="message">The received message; it becomes the job's first message.</param>
    /// <param name="reference">The job reference, e.g. <c>MER-000001</c>.</param>
    /// <param name="nowUtc">The current time from the application's clock port (AD-9).</param>
    /// <param name="propertyId">The resolved property, or null when it is unresolved (AD-13).</param>
    public static Job CreateFromMessage(Message message, string reference, DateTime nowUtc, Guid? propertyId = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith(JobReference.Prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Reference must start with " + JobReference.Prefix + ".", nameof(reference));
        }

        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        if (propertyId == Guid.Empty)
        {
            throw new ArgumentException("Property id must not be empty; pass null when unresolved.", nameof(propertyId));
        }

        var job = new Job
        {
            Id = Guid.NewGuid(),
            Reference = reference,
            Status = JobStatus.Prepared,
            Urgency = Urgency.Routine,
            Category = Category.Unsure,
            ReceivedAtUtc = message.ReceivedAtUtc,
            CreatedAtUtc = nowUtc,
            PropertyId = propertyId,
        };

        job._messages.Add(message);

        job.Append(
            EventType.MessageReceived,
            message.ReceivedAtUtc,
            JobEvent.SystemActor,
            new { messageId = message.Id });

        job.Append(
            EventType.JobPrepared,
            nowUtc,
            JobEvent.SystemActor,
            new
            {
                reference = job.Reference,
                status = job.Status.ToString(),
                urgency = job.Urgency.ToString(),
                category = job.Category.ToString(),
                propertyId = job.PropertyId,
            });

        return job;
    }

    /// <summary>
    /// Attaches a later message to this open job as a follow-up (R8, AD-13): links the message, appends
    /// <see cref="EventType.FollowUpAttached"/> with the message id and this job's reference, then raises urgency to
    /// <paramref name="proposal"/> through <see cref="RaiseUrgency"/> (never lowers it, AD-4).
    /// </summary>
    /// <param name="proposal">The urgency proposed for the follow-up's issue.</param>
    public void AttachFollowUp(Message message, Urgency proposal, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!Enum.IsDefined(proposal))
        {
            throw new ArgumentOutOfRangeException(nameof(proposal), proposal, "Unknown urgency.");
        }

        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        if (!IsOpen)
        {
            throw new InvalidOperationException($"Job {Reference} is {Status} and cannot take follow-ups.");
        }

        if (_messages.Any(m => m.Id == message.Id))
        {
            throw new InvalidOperationException("The message is already on this job.");
        }

        _messages.Add(message);
        Append(
            EventType.FollowUpAttached,
            nowUtc,
            JobEvent.SystemActor,
            new { messageId = message.Id, reference = Reference, proposedUrgency = proposal.ToString() });
        RaiseUrgency(proposal, FollowUpReason, nowUtc);
    }

    /// <summary>Explanation shown on a job whose message the model found no maintenance problem in.</summary>
    public const string NotMaintenanceExplanation =
        "The AI found no maintenance problem in this message. A person should check it.";

    /// <summary>True once a <see cref="EventType.ModelPrepared"/> or <see cref="EventType.ModelPreparationFailed"/> event exists.</summary>
    public bool HasModelOutcome =>
        _events.Any(e => e.Type is EventType.ModelPrepared or EventType.ModelPreparationFailed);

    /// <summary>
    /// Applies a validated model proposal: sets category, explanation, English summary and language, appends
    /// <see cref="EventType.ModelPrepared"/> with <paramref name="payloadJson"/>, then raises urgency to the
    /// proposal (never lowers it, AD-4).
    /// </summary>
    /// <param name="payloadJson">Event payload with the model metadata (model id, prompt version, latency, raw output, attempts).</param>
    public void ApplyModelPreparation(
        Category category,
        Urgency urgency,
        string explanation,
        string summaryEn,
        string language,
        string payloadJson,
        DateTime nowUtc)
    {
        RequireNoModelOutcome();
        if (string.IsNullOrWhiteSpace(explanation))
        {
            throw new ArgumentException("Explanation is required.", nameof(explanation));
        }

        if (string.IsNullOrWhiteSpace(summaryEn))
        {
            throw new ArgumentException("English summary is required.", nameof(summaryEn));
        }

        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("Language is required.", nameof(language));
        }

        RequireJsonObject(payloadJson, nameof(payloadJson));
        Guards.RequireUtc(nowUtc, nameof(nowUtc));

        Category = category;
        Explanation = explanation;
        SummaryEn = summaryEn;
        Language = language;
        AppendRaw(EventType.ModelPrepared, nowUtc, JobEvent.SystemActor, payloadJson);
        RaiseUrgency(urgency, "model", nowUtc);
    }

    /// <summary>
    /// Records a valid model answer with no issues (not maintenance): the job keeps category Unsure and its urgency,
    /// is flagged for a person with <see cref="NotMaintenanceExplanation"/>, and gets <see cref="EventType.ModelPrepared"/>.
    /// </summary>
    public void ApplyNotMaintenance(string language, string payloadJson, DateTime nowUtc)
    {
        RequireNoModelOutcome();
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("Language is required.", nameof(language));
        }

        RequireJsonObject(payloadJson, nameof(payloadJson));
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        Category = Category.Unsure;
        Language = language;
        Explanation = NotMaintenanceExplanation;
        AppendRaw(EventType.ModelPrepared, nowUtc, JobEvent.SystemActor, payloadJson);
    }

    /// <summary>
    /// Records that the model gave no usable proposal. The job keeps category Unsure and its urgency (RULE-11).
    /// </summary>
    /// <param name="payloadJson">Event payload with the failure reason, model metadata and validation errors.</param>
    public void RecordModelFailure(string payloadJson, DateTime nowUtc)
    {
        RequireNoModelOutcome();
        RequireJsonObject(payloadJson, nameof(payloadJson));
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        AppendRaw(EventType.ModelPreparationFailed, nowUtc, JobEvent.SystemActor, payloadJson);
    }

    /// <summary>
    /// Records the emergency rules' result for a message (R4): appends <see cref="EventType.RulesApplied"/> listing
    /// each rule id and matched phrase (an empty list when none fired), remembers the fired rule ids, and raises the
    /// job to Emergency with reason <c>rule:&lt;id&gt;</c> of the first hit. Never lowers urgency (AD-4).
    /// </summary>
    /// <param name="hits">The result of <see cref="EmergencyRules.Evaluate"/>.</param>
    public void ApplyRules(IReadOnlyList<RuleHit> hits, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(hits);
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        foreach (var hit in hits)
        {
            if (hit is null || !EmergencyRuleIds.All.Contains(hit.RuleId))
            {
                throw new ArgumentException("Unknown emergency rule.", nameof(hits));
            }
        }

        Append(
            EventType.RulesApplied,
            nowUtc,
            JobEvent.SystemActor,
            new { rules = hits.Select(h => new { id = h.RuleId, matchedPhrase = h.MatchedPhrase }) });

        if (hits.Count == 0)
        {
            return;
        }

        var ruleIds = RuleIds.Concat(hits.Select(h => h.RuleId)).Distinct(StringComparer.Ordinal);
        _ruleIds = string.Join(RuleIdSeparator, ruleIds);
        RaiseUrgency(Urgency.Emergency, RuleReasonPrefix + hits[0].RuleId, nowUtc);
    }

    /// <summary>
    /// Records that a tenant acknowledgement was written to the outbox (R16, AD-8): appends
    /// <see cref="EventType.AcknowledgementSent"/> with the template id and the outbound message id.
    /// </summary>
    /// <param name="outboundId">The id of the stored outbound message.</param>
    /// <param name="templateId">The fixed template it was rendered from, e.g. <c>acknowledgement</c> or <c>gas-safety</c>.</param>
    public void RecordAcknowledgement(Guid outboundId, string templateId, DateTime nowUtc)
    {
        if (outboundId == Guid.Empty)
        {
            throw new ArgumentException("An outbound message id is required.", nameof(outboundId));
        }

        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new ArgumentException("A template id is required.", nameof(templateId));
        }

        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        Append(
            EventType.AcknowledgementSent,
            nowUtc,
            JobEvent.SystemActor,
            new { templateId, outboundId });
    }

    /// <summary>
    /// Records the contractor fixed code suggests for the job (R12, AD-3), when the job is created or its category
    /// changes: sets <see cref="SuggestedContractorId"/> and <see cref="SuggestedContractorRule"/> and appends
    /// <see cref="EventType.ContractorSuggested"/> with the contractor id and rule (<c>building</c> or <c>default</c>).
    /// The same suggestion again changes nothing and writes no event. An Unsure job gets no suggestion.
    /// </summary>
    /// <returns>True when the suggestion changed.</returns>
    public bool SuggestContractor(ContractorSuggestion suggestion, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        if (suggestion.ContractorId == Guid.Empty)
        {
            throw new ArgumentException("A contractor id is required.", nameof(suggestion));
        }

        if (!Enum.IsDefined(suggestion.Rule))
        {
            throw new ArgumentOutOfRangeException(nameof(suggestion), suggestion.Rule, "Unknown contractor rule.");
        }

        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        if (Category == Category.Unsure)
        {
            throw new InvalidOperationException($"Job {Reference} is Unsure and needs a person, not a suggested contractor.");
        }

        if (SuggestedContractorId == suggestion.ContractorId && SuggestedContractorRule == suggestion.Rule)
        {
            return false;
        }

        SuggestedContractorId = suggestion.ContractorId;
        SuggestedContractorRule = suggestion.Rule;
        Append(
            EventType.ContractorSuggested,
            nowUtc,
            JobEvent.SystemActor,
            new { contractorId = suggestion.ContractorId, rule = suggestion.Rule.ToString().ToLowerInvariant() });
        return true;
    }

    /// <summary>
    /// The only way urgency goes down (AD-4): a person sets the urgency to <paramref name="level"/>, higher or lower,
    /// and <see cref="EventType.UrgencySetByPerson"/> records who, from what, to what and why. Called from the review
    /// use case only.
    /// </summary>
    /// <param name="actor">The acting person (e.g. Rachel); never the system actor.</param>
    /// <param name="reason">Why the person changed it; required.</param>
    public void SetUrgencyByPerson(Urgency level, string actor, string reason, DateTime nowUtc)
    {
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown urgency.");
        }

        if (string.IsNullOrWhiteSpace(actor) || string.Equals(actor.Trim(), JobEvent.SystemActor, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A person is required.", nameof(actor));
        }

        if (actor.Length > JobEvent.MaxActorLength)
        {
            throw new ArgumentException($"Actor must be at most {JobEvent.MaxActorLength} characters.", nameof(actor));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required.", nameof(reason));
        }

        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        var from = Urgency;
        Urgency = level;
        Append(
            EventType.UrgencySetByPerson,
            nowUtc,
            actor,
            new { from = from.ToString(), to = level.ToString(), reason });
    }

    /// <summary>
    /// The only automatic urgency change (AD-4): raises to <paramref name="level"/> and appends
    /// <see cref="EventType.UrgencyRaised"/>; a value lower than or equal to the current urgency is ignored.
    /// </summary>
    /// <returns>True when the urgency changed.</returns>
    public bool RaiseUrgency(Urgency level, string reason, DateTime nowUtc)
    {
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown urgency.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required.", nameof(reason));
        }

        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        if (level <= Urgency)
        {
            return false;
        }

        var from = Urgency;
        Urgency = level;
        Append(
            EventType.UrgencyRaised,
            nowUtc,
            JobEvent.SystemActor,
            new { from = from.ToString(), to = level.ToString(), reason });
        return true;
    }

    /// <summary>What the job still needs before it can be approved (AD-6).</summary>
    public static class ApprovalNeeds
    {
        /// <summary>The category is Unsure: a person must set one.</summary>
        public const string Category = "category";

        /// <summary>No contractor is suggested or chosen.</summary>
        public const string Contractor = "contractor";
    }

    /// <summary>
    /// What stops approval: <see cref="ApprovalNeeds.Category"/> while the category is Unsure and
    /// <see cref="ApprovalNeeds.Contractor"/> while there is no contractor. Empty when the job can be approved
    /// (status aside).
    /// </summary>
    public IReadOnlyList<string> MissingForApproval()
    {
        var missing = new List<string>();
        if (Category == Category.Unsure)
        {
            missing.Add(ApprovalNeeds.Category);
        }

        if (SuggestedContractorId is null)
        {
            missing.Add(ApprovalNeeds.Contractor);
        }

        return missing;
    }

    /// <summary>
    /// A person approves the job (AD-6: Prepared → Approved) and <see cref="EventType.JobApproved"/> records who, with
    /// the category, urgency and contractor approved. Never automatic. Nothing is sent to the contractor here.
    /// </summary>
    /// <exception cref="JobStateException">The job is not Prepared, or still has no category or contractor.</exception>
    public void Approve(string actor, DateTime nowUtc)
    {
        RequirePersonActor(actor);
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        RequirePrepared("approved");
        var missing = MissingForApproval();
        if (missing.Count > 0)
        {
            throw new JobStateException(
                $"Job {Reference} cannot be approved yet: it needs a {string.Join(" and a ", missing)}.", Status, missing);
        }

        var from = Status;
        Status = JobStatus.Approved;
        Append(
            EventType.JobApproved,
            nowUtc,
            actor,
            new
            {
                from = from.ToString(),
                to = Status.ToString(),
                category = Category.ToString(),
                urgency = Urgency.ToString(),
                contractorId = SuggestedContractorId,
                contractorRule = SuggestedContractorRule?.ToString().ToLowerInvariant(),
            });
    }

    /// <summary>
    /// A person rejects the job with a reason (AD-6: Prepared → Rejected); <see cref="EventType.JobRejected"/> records
    /// who and why. A rejected job takes no more follow-ups.
    /// </summary>
    /// <exception cref="JobStateException">The job is not Prepared.</exception>
    public void Reject(string actor, string reason, DateTime nowUtc)
    {
        RequirePersonActor(actor);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required.", nameof(reason));
        }

        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        RequirePrepared("rejected");
        var from = Status;
        Status = JobStatus.Rejected;
        Append(
            EventType.JobRejected,
            nowUtc,
            actor,
            new { from = from.ToString(), to = Status.ToString(), reason = reason.Trim() });
    }

    /// <summary>Field names written in <see cref="EventType.JobEdited"/> payloads.</summary>
    public static class EditedFields
    {
        public const string Category = "category";
        public const string Contractor = "contractor";
        public const string Property = "property";
    }

    /// <summary>
    /// A person sets the category of a Prepared job; <see cref="EventType.JobEdited"/> records the field, the value
    /// before and after. The same value again changes nothing. The caller re-runs the contractor lookup.
    /// </summary>
    /// <returns>True when the category changed.</returns>
    /// <exception cref="JobStateException">The job is not Prepared.</exception>
    public bool EditCategory(Category category, string actor, DateTime nowUtc)
    {
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown category.");
        }

        RequirePersonActor(actor);
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        RequirePrepared("edited");
        if (category == Category)
        {
            return false;
        }

        var before = Category;
        Category = category;
        AppendEdited(EditedFields.Category, before.ToString(), category.ToString(), actor, nowUtc);
        return true;
    }

    /// <summary>
    /// A person sets the property of a Prepared job (for example one the address did not resolve to);
    /// <see cref="EventType.JobEdited"/> records the property ids before and after.
    /// </summary>
    /// <returns>True when the property changed.</returns>
    /// <exception cref="JobStateException">The job is not Prepared.</exception>
    public bool EditProperty(Guid propertyId, string actor, DateTime nowUtc)
    {
        if (propertyId == Guid.Empty)
        {
            throw new ArgumentException("A property id is required.", nameof(propertyId));
        }

        RequirePersonActor(actor);
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        RequirePrepared("edited");
        if (PropertyId == propertyId)
        {
            return false;
        }

        var before = PropertyId;
        PropertyId = propertyId;
        AppendEdited(EditedFields.Property, before, propertyId, actor, nowUtc);
        return true;
    }

    /// <summary>
    /// A person chooses the contractor of a Prepared job: it becomes the job's contractor with rule
    /// <see cref="ContractorRule.Person"/>, and <see cref="EventType.JobEdited"/> records the contractor ids before and
    /// after. Choosing the contractor the job already has changes nothing.
    /// </summary>
    /// <returns>True when the contractor changed.</returns>
    /// <exception cref="JobStateException">The job is not Prepared.</exception>
    public bool ChooseContractor(Guid contractorId, string actor, DateTime nowUtc)
    {
        if (contractorId == Guid.Empty)
        {
            throw new ArgumentException("A contractor id is required.", nameof(contractorId));
        }

        RequirePersonActor(actor);
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        RequirePrepared("edited");
        if (SuggestedContractorId == contractorId)
        {
            return false;
        }

        var before = SuggestedContractorId;
        SuggestedContractorId = contractorId;
        SuggestedContractorRule = ContractorRule.Person;
        AppendEdited(EditedFields.Contractor, before, contractorId, actor, nowUtc);
        return true;
    }

    /// <summary>
    /// Removes the job's contractor after a person's edit left none to suggest (the category was set to Unsure, or
    /// the lookup has no contractor for it); <see cref="EventType.JobEdited"/> records it with the person as actor.
    /// </summary>
    /// <param name="cause">What led to it, e.g. <c>category</c>; written in the payload.</param>
    /// <returns>True when there was a contractor to remove.</returns>
    /// <exception cref="JobStateException">The job is not Prepared.</exception>
    public bool ClearContractor(string actor, string cause, DateTime nowUtc)
    {
        RequirePersonActor(actor);
        if (string.IsNullOrWhiteSpace(cause))
        {
            throw new ArgumentException("A cause is required.", nameof(cause));
        }

        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        RequirePrepared("edited");
        if (SuggestedContractorId is null)
        {
            return false;
        }

        var before = SuggestedContractorId;
        SuggestedContractorId = null;
        SuggestedContractorRule = null;
        Append(
            EventType.JobEdited,
            nowUtc,
            actor,
            new { field = EditedFields.Contractor, before, after = (Guid?)null, cause });
        return true;
    }

    /// <summary>Reason written when a person raises urgency in review without giving one.</summary>
    public const string RaisedInReviewReason = "raised in review";

    /// <summary>
    /// A person sets the urgency of a Prepared job through <see cref="SetUrgencyByPerson"/> (AD-4). Lowering needs a
    /// reason; raising without one records <see cref="RaisedInReviewReason"/>. The same urgency again changes nothing.
    /// </summary>
    /// <returns>True when the urgency changed.</returns>
    /// <exception cref="JobStateException">The job is not Prepared.</exception>
    public bool EditUrgency(Urgency level, string actor, string? reason, DateTime nowUtc)
    {
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown urgency.");
        }

        RequirePersonActor(actor);
        Guards.RequireUtc(nowUtc, nameof(nowUtc));
        RequirePrepared("edited");
        if (level == Urgency)
        {
            return false;
        }

        if (level < Urgency && string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to lower urgency.", nameof(reason));
        }

        SetUrgencyByPerson(level, actor, string.IsNullOrWhiteSpace(reason) ? RaisedInReviewReason : reason.Trim(), nowUtc);
        return true;
    }

    private void RequirePrepared(string action)
    {
        if (Status != JobStatus.Prepared)
        {
            throw new JobStateException($"Job {Reference} is {Status} and cannot be {action}.", Status);
        }
    }

    private static void RequirePersonActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || string.Equals(actor.Trim(), JobEvent.SystemActor, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A person is required.", nameof(actor));
        }

        if (actor.Length > JobEvent.MaxActorLength)
        {
            throw new ArgumentException($"Actor must be at most {JobEvent.MaxActorLength} characters.", nameof(actor));
        }
    }

    private void AppendEdited<T>(string field, T before, T after, string actor, DateTime nowUtc) =>
        Append(EventType.JobEdited, nowUtc, actor, new { field, before, after });

    private const char RuleIdSeparator = ',';
    private const string RuleReasonPrefix = "rule:";
    private const string FollowUpReason = "follow-up";

    private void RequireNoModelOutcome()
    {
        if (HasModelOutcome)
        {
            throw new InvalidOperationException("The model outcome for this job is already recorded.");
        }
    }

    private static void RequireJsonObject(string payloadJson, string paramName)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson ?? string.Empty);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Payload must be a JSON object.", paramName);
            }
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Payload must be valid JSON.", paramName, ex);
        }
    }

    private void Append(EventType type, DateTime occurredAtUtc, string actor, object payload) =>
        AppendRaw(type, occurredAtUtc, actor, JsonSerializer.Serialize(payload));

    private void AppendRaw(EventType type, DateTime occurredAtUtc, string actor, string payloadJson) =>
        _events.Add(new JobEvent(Id, type, occurredAtUtc, actor, payloadJson, NextSequence()));

    /// <summary>The next event's sequence: one more than the highest so far, 1 for the first (AD-5).</summary>
    private int NextSequence() => _events.Count == 0 ? 1 : _events.Max(e => e.Sequence) + 1;
}
