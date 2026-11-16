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
