using Meridian.Application.Abstractions;
using Meridian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Meridian";
    public const string DefaultConnectionString = "Data Source=meridian.db";

    public static IServiceCollection AddMeridianInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<MeridianDbContext>((sp, options) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? DefaultConnectionString;
            options.UseSqlite(connectionString);
        });

        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<IClock, SystemClock>();
        return services;
    }

    /// <summary>Applies pending EF migrations (creates the database on first run).</summary>
    public static async Task MigrateMeridianDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
