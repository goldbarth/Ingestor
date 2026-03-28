using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ingestor.Tests.Integration.Infrastructure;

/// <summary>
/// EF Core interceptor that simulates transient DB failures during SaveChangesAsync.
/// <list type="bullet">
///   <item>Set <see cref="ShouldFail"/> to true to fault on the very next call (one-shot).</item>
///   <item>Call <see cref="TriggerFaultAfter"/> to fault on the Nth subsequent call (one-shot).</item>
/// </list>
/// Both modes reset automatically after firing.
/// </summary>
public sealed class DbFaultInterceptor : SaveChangesInterceptor
{
    private int _faultCountdown;

    public bool ShouldFail { get; set; }

    /// <summary>
    /// Schedules a fault to fire after <paramref name="successfulSaves"/> successful
    /// SaveChangesAsync calls. The fault fires once and then resets automatically.
    /// </summary>
    public void TriggerFaultAfter(int successfulSaves)
        => _faultCountdown = successfulSaves + 1;

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

        if (_faultCountdown > 0 && --_faultCountdown == 0)
            throw new TimeoutException("Simulated DB timeout during SaveChangesAsync.");

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
