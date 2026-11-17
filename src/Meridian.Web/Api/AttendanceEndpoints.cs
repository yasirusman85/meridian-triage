using Meridian.Application.Attendance;
using Meridian.Application.Abstractions;
using Meridian.Application.Review;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Web.Api;

/// <summary>
/// Contractor attendance (R20, R21, AD-11): the contractor's single-job view and "Mark attended" through the link
/// token, and a person sending an overdue job to a fallback contractor. A wrong, used or expired token is 404 with no
/// detail. The resend names its person in the <see cref="ReviewEndpoints.ActingAsHeader"/> header (no login).
/// </summary>
public static class AttendanceEndpoints
{
    public static RouteGroupBuilder MapAttendanceApi(this RouteGroupBuilder api)
    {
        api.MapGet("/contractor-jobs/{token}", async (string token, GetContractorJob getJob, CancellationToken ct) =>
            await getJob.ExecuteAsync(token, ct) is { } job
                ? Results.Ok(new ContractorJobDto(job.Reference, job.Address, job.Category, job.Summary, job.ContractorName))
                : ContractorJobNotFound());

        api.MapPost("/contractor-jobs/{token}/attended", async (string token, MarkAttended markAttended, CancellationToken ct) =>
            await markAttended.ExecuteAsync(token, ct) is { } reference
                ? Results.Ok(new ContractorAttendedDto(reference, "Attended"))
                : ContractorJobNotFound());

        api.MapPost("/jobs/{id:guid}/resend", async (
            Guid id,
            [FromHeader(Name = ReviewEndpoints.ActingAsHeader)] string? actingAs,
            ResendRequest? request,
            ResendToContractor resend,
            GetJob getJob,
            CancellationToken ct) =>
            await ReviewEndpoints.ToResultAsync(await resend.ExecuteAsync(id, actingAs, request?.ContractorId, ct), id, getJob, ct));

        return api;
    }

    private static IResult ContractorJobNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Job not found");
}

/// <summary>What the contractor sees of its job: nothing about any other job, the tenant or the property's other details.</summary>
public sealed record ContractorJobDto(string Reference, string? Address, string Category, string? Summary, string ContractorName);

/// <summary>The result of "Mark attended".</summary>
public sealed record ContractorAttendedDto(string Reference, string Status);

/// <summary>The fallback contractor a person sends an overdue job to.</summary>
public sealed record ResendRequest(Guid? ContractorId);
