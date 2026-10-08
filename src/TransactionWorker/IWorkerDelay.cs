namespace TransactionWorker;

/// <summary>
/// Seam for the worker's poll/backoff waits, so tests can run the retry loop without
/// real sleeps instead of asserting against wall-clock timing.
/// </summary>
public interface IWorkerDelay
{
    Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken);
}

public sealed class SystemWorkerDelay : IWorkerDelay
{
    public Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken) =>
        Task.Delay(duration, cancellationToken);
}
