using Meridian.Application.Abstractions;
using Meridian.Application.Review;
using Meridian.Application.Staff;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Web.Api;

/// <summary>
/// Review endpoints (R13–R15): the job detail, approve, reject and edit, plus the lists the review form needs. Every
/// action names its person in the <see cref="ActingAsHeader"/> header (no login); a missing or unknown person is 400.
/// </summary>
public static class ReviewEndpoints
{
    public const string ActingAsHeader = "X-Acting-As";

    public static RouteGroupBuilder MapReviewApi(this RouteGroupBuilder api)
    {
        api.MapGet("/staff", (StaffDirectory staff) =>
            Results.Ok(staff.All.Select(s => new StaffMemberDto(s.Id, s.Name))));

        api.MapGet("/contractors", async (IContractorRepository contractors, CancellationToken ct) =>
            Results.Ok((await contractors.ListAsync(ct))
                .OrderBy(c => c.Trade.ToString(), StringComparer.Ordinal)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .Select(c => new ContractorDto(c.Id, c.Name, c.Trade, c.Phone))));

        api.MapGet("/properties", async (IPropertyRepository properties, CancellationToken ct) =>
            Results.Ok((await properties.ListAsync(ct))
                .OrderBy(p => p.Building?.Name, StringComparer.Ordinal)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => new PropertyDto(p.Id, p.Name, p.BuildingId, p.Building?.Name))));

        api.MapGet("/jobs/{id:guid}", async (Guid id, GetJob getJob, CancellationToken ct) =>
            await DetailAsync(id, getJob, ct));

        api.MapPost("/jobs/{id:guid}/approve", async (
            Guid id,
            [FromHeader(Name = ActingAsHeader)] string? actingAs,
            ReviewJob review,
            GetJob getJob,
            CancellationToken ct) =>
            await ToResultAsync(await review.ApproveAsync(id, actingAs, ct), id, getJob, ct));

        api.MapPost("/jobs/{id:guid}/reject", async (
            Guid id,
            [FromHeader(Name = ActingAsHeader)] string? actingAs,
            RejectJobRequest? request,
            ReviewJob review,
            GetJob getJob,
            CancellationToken ct) =>
            await ToResultAsync(await review.RejectAsync(id, actingAs, request?.Reason, ct), id, getJob, ct));

        api.MapPatch("/jobs/{id:guid}", async (
            Guid id,
            [FromHeader(Name = ActingAsHeader)] string? actingAs,
            EditJobRequest? request,
            ReviewJob review,
            GetJob getJob,
            CancellationToken ct) =>
        {
            var edit = new JobEdit(request?.Category, request?.Urgency, request?.UrgencyReason, request?.ContractorId, request?.PropertyId);
            return await ToResultAsync(await review.EditAsync(id, actingAs, edit, ct), id, getJob, ct);
        });

        return api;
    }

    internal static async Task<IResult> ToResultAsync(ReviewResult result, Guid id, GetJob getJob, CancellationToken ct) =>
        result.Outcome switch
        {
            ReviewOutcome.Done => await DetailAsync(id, getJob, ct),
            ReviewOutcome.NotFound => JobNotFound(),
            ReviewOutcome.Invalid => Results.ValidationProblem(result.Errors.ToDictionary(e => e.Key, e => e.Value)),
            ReviewOutcome.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The job cannot be changed this way",
                detail: result.Detail,
                extensions: new Dictionary<string, object?> { ["missing"] = result.Missing }),
            _ => throw new InvalidOperationException($"Unknown review outcome {result.Outcome}."),
        };

    internal static async Task<IResult> DetailAsync(Guid id, GetJob getJob, CancellationToken ct)
    {
        var detail = await getJob.ExecuteAsync(id, ct);
        return detail is null ? JobNotFound() : Results.Ok(ToDto(detail));
    }

    internal static IResult JobNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Job not found");

    private static JobDetailDto ToDto(JobDetail d) => new(
        d.Id,
        d.Reference,
        d.Status,
        d.Urgency,
        d.Category,
        d.ReceivedAtUtc,
        d.Explanation,
        d.SummaryEn,
        d.Language,
        d.PropertyId,
        d.PropertyName,
        d.BuildingName,
        d.Contractor is { } c ? new JobDetailContractorDto(c.Id, c.Name, c.Phone, c.Trade, c.Rule) : null,
        d.Rules.Select(r => new JobDetailRuleDto(r.Id, r.MatchedPhrase)).ToList(),
        d.Messages.Select(m => new JobDetailMessageDto(m.Id, m.Sender, m.Text, m.ReceivedAtUtc)).ToList(),
        d.Events.Select(e => new JobDetailEventDto(
            e.Type,
            e.OccurredAtUtc,
            e.Actor,
            e.Summary,
            e.Sequence,
            e.PayloadJson,
            e.Model is { } m
                ? new JobDetailModelRunDto(m.ModelId, m.PromptVersion, m.LatencyMs, m.InputTokens, m.OutputTokens, m.RawOutput, m.Reason, m.Attempts)
                : null)).ToList(),
        d.CanReview,
        d.MissingForApproval);
}
