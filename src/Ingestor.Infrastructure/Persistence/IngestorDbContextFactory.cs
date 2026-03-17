using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ingestor.Infrastructure.Persistence;

// Used exclusively by EF Core tooling (dotnet ef migrations add/update).
// Not part of the runtime DI container.
internal sealed class IngestorDbContextFactory : IDesignTimeDbContextFactory<IngestorDbContext>
{
    public IngestorDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING") 
                               ?? throw new InvalidOperationException("Missing environment variable CONNECTION_STRING. " +
                                                                      "Please set it or configure it in your .env file.");
        
        var options = new DbContextOptionsBuilder<IngestorDbContext>()
            
            .UseNpgsql(connectionString)
            .Options;

        return new IngestorDbContext(options);
    }
}