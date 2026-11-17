using System.Text.Json.Serialization;
using Meridian.Application.Attendance;
using Meridian.Application.Escalation;
using Meridian.Application.Metrics;
using Meridian.Application.Messages;
using Meridian.Application.Outbox;
using Meridian.Application.Queue;
using Meridian.Application.Review;
using Meridian.Application.Staff;
using Meridian.Application.Triage;
using Meridian.Infrastructure;
using Meridian.Web.Api;
using Meridian.Web.Configuration;
using Meridian.Web.Demo;
using Meridian.Web.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddMeridianInfrastructure();
builder.Services.AddScoped<PrepareJob>();
builder.Services.AddScoped<SubmitMessage>();
builder.Services.AddScoped<ListQueue>();
builder.Services.AddScoped<AcknowledgeTenant>();
builder.Services.AddScoped<ListOutbound>();
builder.Services.AddScoped<RecoverOrphanMessages>();

// The one staff list (config section Staff): the "acting as" people (no login) and the escalation recipients (R18).
builder.Services.AddSingleton(StaffConfiguration.Read(builder.Configuration));
builder.Services.AddScoped<GetJob>();
builder.Services.AddScoped<ReviewJob>();

// Out-of-hours escalation (R18, R19, AD-9): the window from Escalation:WindowMinutes, a worker ticking every minute.
builder.Services.AddSingleton(sp => EscalationConfiguration.Read(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddScoped<EscalationService>();
builder.Services.AddScoped<AcknowledgeEscalation>();
builder.Services.AddScoped<ListNotifications>();

// The numbers page (R24): computed from events only.
builder.Services.AddScoped<MetricsQuery>();
builder.Services.AddHostedService<EscalationWorker>();

// Contractor attendance (R20, R21, AD-11): the single-job link, mark attended, the overdue check and resend.
builder.Services.AddScoped<GetContractorJob>();
builder.Services.AddScoped<MarkAttended>();
builder.Services.AddScoped<ResendToContractor>();
builder.Services.AddScoped<CheckOverdueAttendance>();

var app = builder.Build();

await app.Services.MigrateMeridianDatabaseAsync();

// Demo: on an empty database the six forwarded client messages go through the real pipeline (see DemoSeed).
if (builder.Configuration.GetValue(DemoSeed.ConfigurationKey, false))
{
    await DemoSeed.SeedAsync(app.Services, CancellationToken.None);
}

// RULE-11: a message stored by an intake that stopped before its jobs were saved still gets a job and its
// acknowledgement. Nothing is in flight before the app serves requests, so no minimum age.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<RecoverOrphanMessages>().ExecuteAsync(TimeSpan.Zero, CancellationToken.None);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapMeridianApi();
app.MapEscalationApi();
app.MapGroup("/api").MapAttendanceApi();
app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>Entry point, public for WebApplicationFactory in tests.</summary>
public partial class Program
{
}
