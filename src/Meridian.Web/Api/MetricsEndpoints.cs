using Meridian.Application.Metrics;

namespace Meridian.Web.Api;

/// <summary>The numbers page (R24): GET /api/metrics, computed from job events only (AD-5).</summary>
public static class MetricsEndpoints
{
    public static RouteGroupBuilder MapMetricsApi(this RouteGroupBuilder api)
    {
        api.MapGet("/metrics", async (MetricsQuery metrics, CancellationToken ct) =>
            Results.Ok(MetricsDto.From(await metrics.ExecuteAsync(ct))));

        return api;
    }
}

public sealed record MetricCountDto(string Name, int Count);

public sealed record DurationMeasureDto(int Jobs, double? MedianMinutes, double? Percentile90Minutes);

public sealed record PromiseMeasureDto(int Eligible, int Met);

public sealed record MetricsWindowDto(
    int JobCount,
    IReadOnlyList<MetricCountDto> ByCategory,
    IReadOnlyList<MetricCountDto> ByUrgency,
    IReadOnlyList<MetricCountDto> OpenByStatus,
    DurationMeasureDto FirstHumanLook,
    DurationMeasureDto TimeToApproval,
    DurationMeasureDto TimeToAttendance,
    PromiseMeasureDto Emergency24Hours,
    PromiseMeasureDto NonEmergency5WorkingDays)
{
    public static MetricsWindowDto From(MetricsWindow w) => new(
        w.JobCount,
        w.ByCategory.Select(c => new MetricCountDto(c.Name, c.Count)).ToList(),
        w.ByUrgency.Select(c => new MetricCountDto(c.Name, c.Count)).ToList(),
        w.OpenByStatus.Select(c => new MetricCountDto(c.Name, c.Count)).ToList(),
        new DurationMeasureDto(w.FirstHumanLook.Jobs, w.FirstHumanLook.MedianMinutes, w.FirstHumanLook.Percentile90Minutes),
        new DurationMeasureDto(w.TimeToApproval.Jobs, w.TimeToApproval.MedianMinutes, w.TimeToApproval.Percentile90Minutes),
        new DurationMeasureDto(w.TimeToAttendance.Jobs, w.TimeToAttendance.MedianMinutes, w.TimeToAttendance.Percentile90Minutes),
        new PromiseMeasureDto(w.Emergency24Hours.Eligible, w.Emergency24Hours.Met),
        new PromiseMeasureDto(w.NonEmergency5WorkingDays.Eligible, w.NonEmergency5WorkingDays.Met));
}

public sealed record MetricsDto(DateTime GeneratedAtUtc, MetricsWindowDto Last30Days, MetricsWindowDto AllTime)
{
    public static MetricsDto From(MetricsReport report) => new(
        report.GeneratedAtUtc,
        MetricsWindowDto.From(report.Last30Days),
        MetricsWindowDto.From(report.AllTime));
}
