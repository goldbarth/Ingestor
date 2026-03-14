using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ingestor.Infrastructure.Persistence;

// Used exclusively by EF Core tooling (dotnet ef migrations add/update).
// Not part of the runtime DI container.
internal sealed class IngestorDbContextFactory : IDesignTimeDbContextFactory<IngestorDbContext>
{
    public IngestorDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IngestorDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=ingestor;Username=ingestor;Password=changeme")
            .Options;

        return new IngestorDbContext(options);
    }
}