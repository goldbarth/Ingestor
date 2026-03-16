using Ingestor.Domain.DeliveryItems;
using Ingestor.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ingestor.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryItemConfiguration : IEntityTypeConfiguration<DeliveryItem>
{
    public void Configure(EntityTypeBuilder<DeliveryItem> builder)
    {
        builder.ToTable("delivery_items");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasConversion(new DeliveryItemIdConverter())
            .ValueGeneratedNever();

        builder.Property(d => d.JobId)
            .HasConversion(new ImportJobIdConverter())
            .IsRequired();

        builder.Property(d => d.ArticleNumber)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(d => d.ProductName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.Quantity)
            .IsRequired();

        builder.Property(d => d.ExpectedDate)
            .IsRequired();

        builder.Property(d => d.SupplierRef)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(d => d.ProcessedAt)
            .IsRequired();

        builder.HasIndex(d => d.JobId);
    }
}