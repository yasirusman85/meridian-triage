using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;
using Meridian.Domain.Properties;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence;

public sealed class MeridianDbContext : DbContext
{
    public MeridianDbContext(DbContextOptions<MeridianDbContext> options)
        : base(options)
    {
    }

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<JobEvent> JobEvents => Set<JobEvent>();

    public DbSet<Building> Buildings => Set<Building>();

    public DbSet<Property> Properties => Set<Property>();

    public DbSet<JobNumberSequence> JobNumberSequences => Set<JobNumberSequence>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as their names, never numbers.
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MeridianDbContext).Assembly);
    }
}
