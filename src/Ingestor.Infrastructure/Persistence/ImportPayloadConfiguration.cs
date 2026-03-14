using Ingestor.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ingestor.Infrastructure.Persistence;

internal sealed class ImportPayloadConfiguration : IEntityTypeConfiguration<ImportPayload>
{
    public void Configure(EntityTypeBuilder<ImportPayload> builder)
    {
        builder.ToTable("import_payloads");
        
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .ValueGeneratedNever();
        
        builder.Property(p => p.JobId)
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