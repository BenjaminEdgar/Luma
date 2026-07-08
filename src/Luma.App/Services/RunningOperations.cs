using System.Diagnostics;

namespace Luma.App.Services;

public sealed record RunningOperation(Guid Id, string Name, DateTimeOffset StartedAt);

public interface IRunningOperationCoordinator
{
    event EventHandler? Changed;
    IReadOnlyList<RunningOperation> Active { get; }
    RunningOperationLease Begin(string name, CancellationToken parentToken);
    void CancelAll();
}

public sealed class RunningOperationCoordinator : IRunningOperationCoordinator
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, (RunningOperation Operation, CancellationTokenSource Cancellation)> _active = [];
    public event EventHandler? Changed;

    public IReadOnlyList<RunningOperation> Active
    {
        get { lock (_gate) return _active.Values.Select(value => value.Operation).OrderBy(value => value.StartedAt).ToArray(); }
    }

    public RunningOperationLease Begin(string name, CancellationToken parentToken)
    {
        var operation = new RunningOperation(Guid.NewGuid(), name, DateTimeOffset.UtcNow);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        lock (_gate) _active.Add(operation.Id, (operation, cancellation));
        Changed?.Invoke(this, EventArgs.Empty);
        return new RunningOperationLease(operation, cancellation, Complete);
    }

    public void CancelAll()
    {
        CancellationTokenSource[] cancellations;
        lock (_gate) cancellations = _active.Values.Select(value => value.Cancellation).ToArray();
        foreach (var cancellation in cancellations)
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void Complete(Guid id)
    {
        CancellationTokenSource? cancellation = null;
        lock (_gate)
        {
            if (_active.Remove(id, out var entry)) cancellation = entry.Cancellation;
        }
        cancellation?.Dispose();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class RunningOperationLease(RunningOperation operation, CancellationTokenSource cancellation, Action<Guid> complete) : IDisposable
{
    private int _disposed;
    public RunningOperation Operation { get; } = operation;
    public CancellationToken Token => cancellation.Token;
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) complete(Operation.Id); }
}

public sealed record ProviderDiagnostic(bool IsAvailable, string Message);

public sealed class ProviderDiagnostics
{
    public async Task<ProviderDiagnostic> CheckAsync(string command, CancellationToken token)
    {
        var launch = CliAiClient.ResolveCommand(command);
        if (launch is null) return new(false, $"{command} CLI was not found. Install it, then restart Luma.");
        var resolved = launch.Value;
        var arguments = command switch
        {
            "codex" => new[] { "login", "status" },
            "grok" => new[] { "models" },
            _ => new[] { "auth", "status" }
        };
        var psi = new ProcessStartInfo(resolved.Executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var prefix in resolved.PrefixArguments) psi.ArgumentList.Add(prefix);
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {command}.");
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = string.Join(' ', new[] { await outputTask, await errorTask }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
            return process.ExitCode == 0
                ? new(true, $"{command} is ready")
                : new(false, string.IsNullOrWhiteSpace(output) ? $"{command} is not authenticated. Sign in from a terminal." : output);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new(false, $"Timed out checking {command}. Open it in a terminal and verify sign-in."); }
        catch (Exception ex) { return new(false, $"Could not check {command}: {ex.Message}"); }
    }
}
