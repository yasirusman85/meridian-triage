using Meridian.Domain.Contractors;
using Meridian.Domain.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meridian.Infrastructure.Persistence.Configurations;

internal sealed class ContractorConfiguration : IEntityTypeConfiguration<Contractor>
{
    public void Configure(EntityTypeBuilder<Contractor> builder)
    {
        builder.ToTable("Contractors");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).IsRequired().HasMaxLength(Contractor.MaxNameLength);
        builder.HasIndex(c => c.Name).IsUnique();
        builder.Property(c => c.Trade).IsRequired().HasMaxLength(32);
        builder.Property(c => c.Phone).IsRequired().HasMaxLength(Contractor.MaxPhoneLength);
    }
}

internal sealed class ContractorAssignmentConfiguration : IEntityTypeConfiguration<ContractorAssignment>
{
    public void Configure(EntityTypeBuilder<ContractorAssignment> builder)
    {
        builder.ToTable("ContractorAssignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Category).IsRequired().HasMaxLength(32);
        builder.Ignore(a => a.IsDefault);

        // A null building is the company-wide default for the category.
        builder.HasOne<Building>()
            .WithMany()
            .HasForeignKey(a => a.BuildingId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Contractor>()
            .WithMany()
            .HasForeignKey(a => a.ContractorId)
            .OnDelete(DeleteBehavior.Restrict);

        // One usual contractor per building and category; SQLite treats NULLs as distinct, so defaults get their own index.
        builder.HasIndex(a => new { a.BuildingId, a.Category }).IsUnique();
        builder.HasIndex(a => a.Category)
            .IsUnique()
            .HasFilter("\"BuildingId\" IS NULL")
            .HasDatabaseName("IX_ContractorAssignments_DefaultCategory");
    }
}
