using Ingestor.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ingestor.Infrastructure.Persistence.Configurations;

internal sealed class OutboxEntryConfiguration : IEntityTypeConfiguration<OutboxEntry>
{
    public void Configure(EntityTypeBuilder<OutboxEntry> builder)
    {
        builder.ToTable("outbox_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(new OutboxEntryIdConverter())
            .ValueGeneratedNever();

        builder.Property(e => e.JobId)
            .HasConversion(new ImportJobIdConverter())
            .IsRequired();

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(e => e.AttemptNumber)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.ScheduledFor);
        builder.Property(e => e.LockedAt);
        builder.Property(e => e.ProcessedAt);

        // Composite index for efficient worker polling: WHERE status = 'Pending' ORDER BY created_at
        builder.HasIndex(e => new { e.Status, e.CreatedAt });
    }
}
