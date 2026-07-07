using Luma.App.Services;

namespace Luma.Tests;

public sealed class RetryPolicyTests
{
    [Theory]
    [InlineData(0, 2, true)]
    [InlineData(1, 2, true)]
    [InlineData(2, 2, false)]
    [InlineData(3, 2, false)]
    public void ShouldRetryRespectsBound(int attemptSoFar, int max, bool expected) =>
        Assert.Equal(expected, RetryPolicy.ShouldRetry(attemptSoFar, max));
}
