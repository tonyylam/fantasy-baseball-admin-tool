using FantasyKeeper.Api.Services.Google;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class RetryPolicyTests
{
    [Fact]
    public async Task WithOneRetryAsync_FailsOnceThenSucceeds_ReturnsResult()
    {
        var attempts = 0;

        var result = await RetryPolicy.WithOneRetryAsync(() =>
        {
            attempts++;
            if (attempts == 1) throw new InvalidOperationException("transient");
            return Task.FromResult(42);
        }, TimeSpan.Zero);

        Assert.Equal(42, result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task WithOneRetryAsync_FailsTwice_ThrowsAfterSecondAttempt()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => RetryPolicy.WithOneRetryAsync<int>(() =>
        {
            attempts++;
            throw new InvalidOperationException("still failing");
        }, TimeSpan.Zero));

        Assert.Equal(2, attempts);
    }
}
