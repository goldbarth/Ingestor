using Ingestor.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ingestor.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(new AuditEventIdConverter())
            .ValueGeneratedNever();

        builder.Property(e => e.JobId)
            .HasConversion(new ImportJobIdConverter())
            .IsRequired();

        builder.Property(e => e.OldStatus)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(e => e.NewStatus)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(e => e.TriggeredBy)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(e => e.OccurredAt)
            .IsRequired();

        builder.Property(e => e.Comment)
            .HasMaxLength(500);

        builder.HasIndex(e => e.JobId);
    }
}