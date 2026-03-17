using Ingestor.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ingestor.Infrastructure.Persistence.Configurations;

internal sealed class DeadLetterEntryConfiguration : IEntityTypeConfiguration<DeadLetterEntry>
{
    public void Configure(EntityTypeBuilder<DeadLetterEntry> builder)
    {
        builder.ToTable("dead_letter_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(new DeadLetterEntryIdConverter())
            .ValueGeneratedNever();

        builder.Property(e => e.JobId)
            .HasConversion(new ImportJobIdConverter())
            .IsRequired();

        builder.Property(e => e.Reason)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.ErrorMessage)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(e => e.SupplierCode)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.ImportType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(e => e.TotalAttempts)
            .IsRequired();

        builder.Property(e => e.DeadLetteredAt)
            .IsRequired();

        builder.HasIndex(e => e.JobId).IsUnique();
        builder.HasIndex(e => e.DeadLetteredAt);
    }
}