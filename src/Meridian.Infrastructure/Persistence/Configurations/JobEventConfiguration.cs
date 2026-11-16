using Meridian.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

internal sealed class JobEventConfiguration : IEntityTypeConfiguration<JobEvent>
{
    public void Configure(EntityTypeBuilder<JobEvent> builder)
    {
        builder.ToTable("JobEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Type).IsRequired().HasMaxLength(64);
        builder.Property(e => e.OccurredAtUtc).IsRequired();
        builder.Property(e => e.Actor).IsRequired().HasMaxLength(JobEvent.MaxActorLength);
        builder.Property(e => e.PayloadJson).IsRequired();
        builder.HasIndex(e => new { e.JobId, e.OccurredAtUtc });
    }
}
