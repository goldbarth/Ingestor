using Ingestor.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ingestor.Infrastructure.Persistence.Configurations;

internal sealed class ImportPayloadConfiguration : IEntityTypeConfiguration<ImportPayload>
{
    public void Configure(EntityTypeBuilder<ImportPayload> builder)
    {
        builder.ToTable("import_payloads");
        
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasConversion(new ImportPayloadIdConverter())
            .ValueGeneratedNever();
        
        builder.Property(p => p.JobId)
            .HasConversion(new ImportJobIdConverter())
            .IsRequired();
        
        builder.Property(p =>p.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.RawData)
            .IsRequired();

        builder.Property(p => p.SizeBytes)
            .IsRequired();
        
        builder.Property(p => p.ReceivedAt)
            .IsRequired();
        
        // FK without navigation property (clean domain)
        builder.HasIndex(p => p.JobId);

    }
}