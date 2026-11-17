using System.Text.Json.Serialization;
using Meridian.Application.Escalation;
using Meridian.Application.Messages;
using Meridian.Application.Outbox;
using Meridian.Application.Queue;
using Meridian.Application.Review;
using Meridian.Application.Triage;
using Meridian.Infrastructure;
using Meridian.Web.Api;
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

// The "acting as" people (no login): config section Staff, Rachel and Dawn by default.
builder.Services.AddSingleton(new StaffDirectory(builder.Configuration.GetSection("Staff").Get<List<Meridian.Application.Review.StaffMember>>()));
builder.Services.AddScoped<GetJob>();
builder.Services.AddScoped<ReviewJob>();

// Out-of-hours escalation (R18, R19, AD-9): staff and window from Escalation:*, a worker ticking every minute.
builder.Services.AddSingleton(sp => EscalationConfiguration.Read(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddScoped<EscalationService>();
builder.Services.AddScoped<AcknowledgeEscalation>();
builder.Services.AddScoped<ListNotifications>();
builder.Services.AddHostedService<EscalationWorker>();

var app = builder.Build();

await app.Services.MigrateMeridianDatabaseAsync();

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
app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>Entry point, public for WebApplicationFactory in tests.</summary>
public partial class Program
{
}
