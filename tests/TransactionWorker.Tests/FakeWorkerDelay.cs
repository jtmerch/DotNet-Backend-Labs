using TransactionWorker;

namespace TransactionWorker.Tests;

/// <summary>Completes immediately and records what was asked of it, so retry/backoff
/// tests run fast and deterministically instead of waiting on real timers.</summary>
public sealed class FakeWorkerDelay : IWorkerDelay
{
    public List<TimeSpan> Requested { get; } = new();

    public Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        Requested.Add(duration);
        return Task.CompletedTask;
    }
}
