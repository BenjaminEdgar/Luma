namespace Luma.App.Services;

public static class RetryPolicy
{
    public static bool ShouldRetry(int attemptSoFar, int max = 2) => attemptSoFar < max;
}
