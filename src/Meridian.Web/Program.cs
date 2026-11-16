using System.Text.Json.Serialization;
using Meridian.Application.Messages;
using Meridian.Application.Queue;
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

var app = builder.Build();

await app.Services.MigrateMeridianDatabaseAsync();

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
