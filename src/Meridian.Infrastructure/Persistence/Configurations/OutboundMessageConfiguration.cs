using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;
using Meridian.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

internal sealed class OutboundMessageConfiguration : IEntityTypeConfiguration<OutboundMessage>
{
    public void Configure(EntityTypeBuilder<OutboundMessage> builder)
    {
        builder.ToTable("OutboundMessages");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Channel).IsRequired().HasMaxLength(OutboundMessage.MaxChannelLength);
        builder.Property(o => o.Recipient).IsRequired();
        builder.Property(o => o.TemplateId).IsRequired().HasMaxLength(OutboundMessage.MaxTemplateIdLength);
        builder.Property(o => o.Body).IsRequired();
        builder.Property(o => o.Status).IsRequired().HasMaxLength(32);
        builder.Property(o => o.CreatedAtUtc).IsRequired();
        builder.HasOne<Job>()
            .WithMany()
            .HasForeignKey(o => o.JobId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(o => o.MessageId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => new { o.JobId, o.CreatedAtUtc });
        builder.HasIndex(o => o.MessageId);
    }
}
