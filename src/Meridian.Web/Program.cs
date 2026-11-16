using System.Text.Json.Serialization;
using Meridian.Application.Messages;
using Meridian.Application.Outbox;
using Meridian.Application.Queue;
using Meridian.Application.Review;
using Meridian.Application.Triage;
using Meridian.Infrastructure;
using Meridian.Web.Api;

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
builder.Services.AddSingleton(new StaffDirectory(builder.Configuration.GetSection("Staff").Get<List<StaffMember>>()));
builder.Services.AddScoped<GetJob>();
builder.Services.AddScoped<ReviewJob>();

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
app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>Entry point, public for WebApplicationFactory in tests.</summary>
public partial class Program
{
}
