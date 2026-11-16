using Meridian.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("Jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.Reference).IsRequired().HasMaxLength(16);
        builder.HasIndex(j => j.Reference).IsUnique();
        builder.Property(j => j.Status).IsRequired().HasMaxLength(32);
        builder.Property(j => j.Urgency).IsRequired().HasMaxLength(32);
        builder.Property(j => j.Category).IsRequired().HasMaxLength(32);
        builder.Property(j => j.ReceivedAtUtc).IsRequired();
        builder.Property(j => j.CreatedAtUtc).IsRequired();
        builder.Property(j => j.Explanation);
        builder.Property(j => j.SummaryEn);
        builder.Property(j => j.Language).HasMaxLength(16);
        builder.Ignore(j => j.HasModelOutcome);

        // Fired emergency rule ids, comma-separated (e.g. "gas,carbon-monoxide"); empty when none fired.
        builder.Ignore(j => j.RuleIds);
        builder.Property<string>("_ruleIds")
            .HasField("_ruleIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("RuleIds")
            .IsRequired()
            .HasMaxLength(256)
            .HasDefaultValue(string.Empty);

        builder.HasMany(j => j.Messages)
            .WithOne()
            .HasForeignKey(m => m.JobId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(j => j.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(j => j.Events)
            .WithOne()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(j => j.Events).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
