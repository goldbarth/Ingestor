using Ingestor.Domain.DeliveryItems;
using Ingestor.Domain.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Infrastructure.Persistence;

public class IngestorDbContext(DbContextOptions<IngestorDbContext> options) : DbContext(options)
{
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportPayload> ImportPayloads => Set<ImportPayload>();
    public DbSet<OutboxEntry> OutboxEntries => Set<OutboxEntry>();
    public DbSet<DeliveryItem> DeliveryItems => Set<DeliveryItem>();
    public DbSet<ImportAttempt> ImportAttempts => Set<ImportAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IngestorDbContext).Assembly);
    }
}