using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

internal sealed class JobNumberSequenceConfiguration : IEntityTypeConfiguration<JobNumberSequence>
{
    public void Configure(EntityTypeBuilder<JobNumberSequence> builder)
    {
        builder.ToTable("JobNumberSequence");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasData(new JobNumberSequence { Id = JobNumberSequence.SingletonId, LastValue = 0 });
    }
}
