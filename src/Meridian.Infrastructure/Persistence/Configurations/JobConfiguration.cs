using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;
using Meridian.Domain.Messages;
using Meridian.Domain.Properties;
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

        builder.Ignore(j => j.FollowUpCount);
        builder.Ignore(j => j.IsOpen);

        // Escalation (AD-6): a separate field from the lifecycle status, with a time for each step.
        builder.Property(j => j.EscalationState).IsRequired().HasMaxLength(32);
        builder.Property(j => j.EscalationPrimaryNotifiedAtUtc);
        builder.Property(j => j.EscalationBackupNotifiedAtUtc);
        builder.Property(j => j.EscalationAcknowledgedAtUtc);
        builder.Property(j => j.EscalationAcknowledgedBy).HasMaxLength(JobEvent.MaxActorLength);
        builder.Property(j => j.EscalationFailedAtUtc);
        builder.HasIndex(j => j.EscalationState);
        builder.Ignore(j => j.IsEscalating);
        builder.Ignore(j => j.CanAcknowledgeEscalation);

        // Unresolved properties stay null (AD-13).
        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(j => j.PropertyId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(j => j.PropertyId);

        // The contractor ContractorLookup suggested (R12); null means a person must choose.
        builder.HasOne<Contractor>()
            .WithMany()
            .HasForeignKey(j => j.SuggestedContractorId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(j => j.SuggestedContractorId);
        builder.Property(j => j.SuggestedContractorRule).HasMaxLength(16);

        // Contractor attendance (AD-11): only the token's hash is stored; one live token per job, none after attended.
        builder.Property(j => j.ContractorTokenHash).HasMaxLength(Job.ContractorTokenHashLength);
        builder.HasIndex(j => j.ContractorTokenHash).IsUnique();
        builder.HasOne<Contractor>()
            .WithMany()
            .HasForeignKey(j => j.SentToContractorId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(j => j.SentToContractorId);
        builder.Property(j => j.AttendedAtUtc);
        builder.Property(j => j.ContractorResentAtUtc);
        builder.Property(j => j.AttendanceOverdueFlaggedAtUtc);

        // Many jobs per message (one per issue) and many messages per job (the first plus follow-ups).
        builder.HasMany(j => j.Messages)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "JobMessages",
                right => right.HasOne<Message>().WithMany().HasForeignKey("MessageId").OnDelete(DeleteBehavior.Restrict),
                left => left.HasOne<Job>().WithMany().HasForeignKey("JobId").OnDelete(DeleteBehavior.Restrict),
                join =>
                {
                    join.ToTable("JobMessages");
                    join.HasKey("JobId", "MessageId");
                    join.HasIndex("MessageId");
                });
        builder.Navigation(j => j.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(j => j.Events)
            .WithOne()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(j => j.Events).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
