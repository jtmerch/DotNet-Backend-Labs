namespace Starter;

/// <summary>
/// Should guarantee onCompleted fires at most once per call, even across retries and
/// abandoned attempts.
/// </summary>
public sealed class AttemptRunner
{
    public async Task<string> RunWithRetryAsync(
        Func<CancellationToken, Task<string>> operation,
        int maxAttempts,
        TimeSpan perAttemptTimeout,
        Action<string> onCompleted)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var cts = new CancellationTokenSource();
            Task<string> attemptTask = operation(cts.Token);
            Task winner = await Task.WhenAny(attemptTask, Task.Delay(perAttemptTimeout));

            if (winner == attemptTask)
            {
                string result = await attemptTask;
                onCompleted(result);
                return result;
            }

            // Bug: we're giving up on this attempt from the caller's point of view, but
            // nothing actually cancels or stops observing `attemptTask`. If the
            // operation doesn't respect the cancellation token, it can still complete
            // later - and this continuation still fires onCompleted when it does, even
            // though a subsequent attempt may have already succeeded.
            attemptTask.ContinueWith(
                t => onCompleted(t.Result),
                TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        throw new TimeoutException("Exhausted all attempts.");
    }
}
