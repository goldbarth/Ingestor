namespace Ingestor.Domain.Common;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}