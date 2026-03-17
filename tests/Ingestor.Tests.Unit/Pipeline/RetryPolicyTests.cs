using FluentAssertions;
using Ingestor.Application.Pipeline;

namespace Ingestor.Tests.Unit.Pipeline;

public sealed class RetryPolicyTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 4)]
    [InlineData(3, 16)]
    public void CalculateDelay_ReturnsExponentialBackoff(int attemptNumber, int expectedSeconds)
    {
        var delay = RetryPolicy.CalculateDelay(attemptNumber);

        delay.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void CalculateDelay_DelaysAreStrictlyIncreasing()
    {
        var delays = Enumerable.Range(1, 3)
            .Select(RetryPolicy.CalculateDelay)
            .ToList();

        delays.Should().BeInAscendingOrder();
    }
}