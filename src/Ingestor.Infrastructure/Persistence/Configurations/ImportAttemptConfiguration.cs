using Ingestor.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ingestor.Infrastructure.Persistence.Configurations;

internal sealed class ImportAttemptConfiguration : IEntityTypeConfiguration<ImportAttempt>
{
    public void Configure(EntityTypeBuilder<ImportAttempt> builder)
    {
        builder.ToTable("import_attempts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasConversion(new ImportAttemptIdConverter())
            .ValueGeneratedNever();

        builder.Property(a => a.JobId)
            .HasConversion(new ImportJobIdConverter())
            .IsRequired();

        builder.Property(a => a.AttemptNumber)
            .IsRequired();
        builder.Property(a => a.StartedAt)
            .IsRequired();
        builder.Property(a => a.FinishedAt);
        builder.Property(a => a.DurationMs)
            .IsRequired();

        builder.Property(a => a.Outcome)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(a => a.ErrorCategory)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(a => a.ErrorCode)
            .HasMaxLength(100);
        builder.Property(a => a.ErrorMessage)
            .HasMaxLength(1000);

        builder.HasIndex(a => a.JobId);
    }
}