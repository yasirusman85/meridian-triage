using Meridian.Domain.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

internal sealed class BuildingConfiguration : IEntityTypeConfiguration<Building>
{
    public void Configure(EntityTypeBuilder<Building> builder)
    {
        builder.ToTable("Buildings");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Name).IsRequired().HasMaxLength(128);
        builder.HasIndex(b => b.Name).IsUnique();
        builder.HasMany(b => b.Properties)
            .WithOne(p => p.Building)
            .HasForeignKey(p => p.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(b => b.Properties).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.ToTable("Properties");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Unit).HasMaxLength(16);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(160);
        builder.HasIndex(p => new { p.BuildingId, p.Unit }).IsUnique();
    }
}
