using Ingestor.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ingestor.Infrastructure.Persistence.Configurations;

internal sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> builder)
    {
        builder.ToTable("import_jobs");
        
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id)
            .HasConversion(new ImportJobIdConverter())
            .ValueGeneratedNever();
        
        builder.Property(j => j.SupplierCode)
            .IsRequired()
            .HasMaxLength(50);
        
        builder.Property(j => j.ImportType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);
        
        builder.Property(j => j.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);
        
        builder.Property(j => j.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);
        
        builder.Property(j => j.PayloadReference)
            .IsRequired()
            .HasMaxLength(500);
        
        builder.Property(j => j.ReceivedAt)
            .IsRequired();
        builder.Property(j => j.StartedAt);
        builder.Property(j => j.CompletedAt);
        
        builder.Property(j => j.CurrentAttempt)
            .IsRequired();
        builder.Property(j => j.MaxAttempts)
            .IsRequired();
        builder.Property(j => j.ProcessedItemCount)
            .IsRequired();
        
        builder.Property(j => j.LastErrorCode)
            .HasMaxLength(100);
        builder.Property(j => j.LastErrorMessage)
            .HasMaxLength(1000);

        builder.HasIndex(j => j.IdempotencyKey)
            .IsUnique();

        builder.HasIndex(j => j.Status);
    }
}