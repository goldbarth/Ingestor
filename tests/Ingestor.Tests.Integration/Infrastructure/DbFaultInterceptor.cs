using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ingestor.Tests.Integration.Infrastructure;

/// <summary>
/// EF Core interceptor that simulates a transient DB failure on the next SaveChangesAsync call.
/// Set <see cref="ShouldFail"/> to true before the operation under test; the interceptor throws
/// once and then resets automatically.
/// </summary>
public sealed class DbFaultInterceptor : SaveChangesInterceptor
{
    public bool ShouldFail { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
        {
            ShouldFail = false;
            throw new TimeoutException("Simulated DB timeout during SaveChangesAsync.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
