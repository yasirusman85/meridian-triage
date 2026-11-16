using Meridian.Application.Messages;
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

            return Results.Created((string?)null, new SubmitMessageResponse(result.JobId!.Value, result.Reference!));
        });

        api.MapGet("/jobs", async (ListQueue listQueue, CancellationToken ct) =>
        {
            var items = await listQueue.ExecuteAsync(ct);
            return Results.Ok(items.Select(i => new JobListItemDto(
                i.Id, i.Reference, i.ReceivedAtUtc, i.Status, i.Urgency, i.Category, i.Sender, i.Preview)));
        });

        // Unknown API routes are 404 problem details, never the SPA page.
        api.MapFallback(() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found"));

        return app;
    }
}
