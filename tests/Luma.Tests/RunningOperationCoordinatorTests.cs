using Luma.App.Services;

namespace Luma.Tests;

public sealed class RunningOperationCoordinatorTests
{
    [Fact]
    public void LeaseTracksAndCompletesOperation()
    {
        var coordinator = new RunningOperationCoordinator();
        using (var operation = coordinator.Begin("test", CancellationToken.None))
        {
            Assert.Single(coordinator.Active);
            Assert.Equal("test", operation.Operation.Name);
        }
        Assert.Empty(coordinator.Active);
    }

    [Fact]
    public void CancelAllCancelsEveryActiveOperation()
    {
        var coordinator = new RunningOperationCoordinator();
        using var first = coordinator.Begin("first", CancellationToken.None);
        using var second = coordinator.Begin("second", CancellationToken.None);

        coordinator.CancelAll();

        Assert.True(first.Token.IsCancellationRequested);
        Assert.True(second.Token.IsCancellationRequested);
    }

    [Fact]
    public void ParentCancellationFlowsIntoOperation()
    {
        var coordinator = new RunningOperationCoordinator();
        using var parent = new CancellationTokenSource();
        using var operation = coordinator.Begin("child", parent.Token);

        parent.Cancel();

        Assert.True(operation.Token.IsCancellationRequested);
    }

}
