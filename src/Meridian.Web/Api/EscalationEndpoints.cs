using Meridian.Application.Escalation;
using Meridian.Application.Templates;
using Meridian.Domain.Jobs;
using Meridian.Domain.Outbox;

namespace Meridian.Web.Api;

/// <summary>Staff acknowledge an emergency from the queue; staff notifications for the notifications panel (R18, R19).</summary>
public static class EscalationEndpoints
{
    /// <summary>The acting-as header (no login in the first version): a configured staff id, e.g. <c>rachel</c> or <c>dawn</c>.</summary>
    public const string ActingAsHeader = "X-Acting-As";

    public static IEndpointRouteBuilder MapEscalationApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/jobs/{id:guid}/acknowledge", async (Guid id, HttpRequest request, AcknowledgeEscalation acknowledge, CancellationToken ct) =>
        {
            var actingAs = request.Headers[ActingAsHeader].ToString();
            var result = await acknowledge.ExecuteAsync(id, actingAs, ct);
            return result.Outcome switch
            {
                AcknowledgeOutcome.Acknowledged => Results.Ok(EscalationDto.From(result.Job!)),
                AcknowledgeOutcome.UnknownStaff => Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Unknown acting person",
                    detail: $"Send the {ActingAsHeader} header with a staff id, e.g. rachel or dawn."),
                AcknowledgeOutcome.JobNotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Job not found"),
                _ => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "This job cannot be acknowledged",
                    detail: result.Detail),
            };
        });

        api.MapGet("/notifications", async (ListNotifications listNotifications, CancellationToken ct) =>
        {
            var items = await listNotifications.ExecuteAsync(ct);
            return Results.Ok(items.Select(n => new NotificationDto(
                n.Message.Id,
                n.Message.JobId,
                n.JobReference,
                n.Message.Channel,
                n.Message.Recipient,
                n.Message.TemplateId,
                OutboundAudience.For(n.Message.TemplateId),
                n.Message.Body,
                n.Message.Status,
                n.Message.CreatedAtUtc)));
        });

        return app;
    }
}

/// <summary>A job's escalation after an acknowledgement.</summary>
public sealed record EscalationDto(
    Guid JobId,
    string Reference,
    EscalationState EscalationState,
    string? AcknowledgedBy,
    DateTime? AcknowledgedAtUtc)
{
    public static EscalationDto From(Job job) =>
        new(job.Id, job.Reference, job.EscalationState, job.EscalationAcknowledgedBy, job.EscalationAcknowledgedAtUtc);
}

/// <summary>A staff notification written to the outbox (AD-8); <c>status</c> is <c>Simulated</c>: no real SMS is sent.</summary>
public sealed record NotificationDto(
    Guid Id,
    Guid JobId,
    string? JobReference,
    string Channel,
    string Recipient,
    string TemplateId,
    string Audience,
    string Body,
    OutboundStatus Status,
    DateTime CreatedAtUtc);
