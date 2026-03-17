namespace Ingestor.Application.Pipeline;

public static class RetryPolicy
{
    // Backoff: 4^(attemptNumber - 1) seconds → attempt 1: 1s, attempt 2: 4s, attempt 3: 16s
    public static TimeSpan CalculateDelay(int attemptNumber)
        => TimeSpan.FromSeconds(Math.Pow(4, attemptNumber - 1));
}
