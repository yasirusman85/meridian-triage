using Meridian.Domain.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Sender).IsRequired();
        // No length limit and no conversion: the text is stored exactly as received (RULE-9).
        builder.Property(m => m.Text).IsRequired();
        builder.Property(m => m.ReceivedAtUtc).IsRequired();
    }
}
