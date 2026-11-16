using Meridian.Application.Abstractions;
using Meridian.Application.Outbox;
using Meridian.Application.Triage;
using Meridian.Infrastructure.Outbox;
using Meridian.Infrastructure.Persistence;
using Meridian.Infrastructure.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Meridian";
    public const string DefaultConnectionString = "Data Source=meridian.db";
    public const string OpenRouterReferer = "http://localhost";
    public const string OpenRouterTitle = "meridian-triage";

    /// <param name="services">The service collection.</param>
    /// <param name="openRouterApiKey">
    /// Supplies the OpenRouter key. Defaults to the <c>OPENROUTER_API_KEY</c> environment variable; tests pass their own.
    /// </param>
    public static IServiceCollection AddMeridianInfrastructure(
        this IServiceCollection services,
        Func<string?>? openRouterApiKey = null)
    {
        var readApiKey = openRouterApiKey ?? TriageOptions.ReadApiKey;

        services.AddDbContext<MeridianDbContext>((sp, options) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? DefaultConnectionString;
            options.UseSqlite(connectionString);
        });

        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IPropertyRepository, PropertyRepository>();
        services.AddScoped<IContractorRepository, ContractorRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IOutbox, SimulatedOutbox>();
        services.AddSingleton<IClock, SystemClock>();

        // AD-1: the recorded-response stub is the default; OpenRouter only when the key is set (or Triage:Provider says so).
        services.AddHttpClient(OpenRouterTriageModel.HttpClientName, (sp, client) =>
            {
                var options = TriageOptions.From(sp.GetRequiredService<IConfiguration>());
                client.BaseAddress = new Uri(options.OpenRouterBaseUrl.TrimEnd('/') + "/");
                var key = readApiKey();
                if (key is not null)
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {key}");
                }

                client.DefaultRequestHeaders.TryAddWithoutValidation("HTTP-Referer", OpenRouterReferer);
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Title", OpenRouterTitle);
            })
            .RedactLoggedHeaders(_ => true);

        services.AddSingleton<ITriageModel>(sp =>
        {
            var options = TriageOptions.From(sp.GetRequiredService<IConfiguration>());
            if (!options.UsesOpenRouter(readApiKey()))
            {
                return StubTriageModel.FromDirectory(StubTriageModel.DefaultDirectory);
            }

            return new OpenRouterTriageModel(
                sp.GetRequiredService<IHttpClientFactory>(),
                options.Model,
                OpenRouterTriageModel.LoadSystemPrompt(),
                sp.GetService<ILogger<OpenRouterTriageModel>>());
        });
        return services;
    }

    /// <summary>
    /// Applies pending EF migrations (creates the database on first run), then seeds buildings and properties, and
    /// contractors and their assignments, each when there are none (<see cref="SeedData"/>).
    /// </summary>
    public static async Task MigrateMeridianDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await SeedData.EnsureSeededAsync(db, cancellationToken);
    }
}
