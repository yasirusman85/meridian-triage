using Meridian.Application.Messages;
using Meridian.Application.Outbox;
using Meridian.Application.Queue;

namespace Meridian.Web.Api;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapMeridianApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/messages", async (SubmitMessageRequest? request, SubmitMessage submit, CancellationToken ct) =>
        {
            var command = new SubmitMessageCommand(request?.Sender, request?.Text);
            var result = await submit.ExecuteAsync(command, ct);
            if (!result.Succeeded)
            {
                return Results.ValidationProblem(result.Errors.ToDictionary(e => e.Key, e => e.Value));
            }

            return Results.Created(
                (string?)null,
                new SubmitMessageResponse(
                    result.MessageId!.Value,
                    result.Jobs.Select(j => new SubmittedJobDto(j.JobId, j.Reference, j.IsFollowUp ? "followUp" : "created")).ToList()));
        });

        api.MapGet("/jobs", async (ListQueue listQueue, CancellationToken ct) =>
        {
            var items = await listQueue.ExecuteAsync(ct);
            return Results.Ok(items.Select(i => new JobListItemDto(
                i.Id, i.Reference, i.ReceivedAtUtc, i.Status, i.Urgency, i.Category, i.Sender, i.Preview, i.Explanation, i.SummaryEn, i.RuleIds,
                i.PropertyId, i.PropertyName, i.FollowUpCount,
                i.BuildingName, i.SuggestedContractorId, i.SuggestedContractorName, i.SuggestedContractorPhone, i.SuggestedContractorRule,
                i.EscalationState, i.EscalationAcknowledgedBy, i.EscalationAcknowledgedAtUtc, i.AttendanceOverdue)));
        });

        api.MapGet("/jobs/{id:guid}/outbound", async (Guid id, ListOutbound listOutbound, CancellationToken ct) =>
        {
            var items = await listOutbound.ExecuteAsync(id, ct);
            if (items is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Job not found");
            }

            return Results.Ok(items.Select(o => new OutboundMessageDto(
                o.Id, o.JobId, o.Channel, o.Recipient, o.TemplateId, o.Body, o.Status, o.CreatedAtUtc)));
        });

        api.MapReviewApi();

        // Unknown API routes are 404 problem details, never the SPA page.
        api.MapFallback(() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found"));

        return app;
    }
}
