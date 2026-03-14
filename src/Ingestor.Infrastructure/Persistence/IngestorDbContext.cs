using Microsoft.EntityFrameworkCore;

namespace Ingestor.Infrastructure.Persistence;

public class IngestorDbContext(DbContextOptions<IngestorDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IngestorDbContext).Assembly);
    }
}