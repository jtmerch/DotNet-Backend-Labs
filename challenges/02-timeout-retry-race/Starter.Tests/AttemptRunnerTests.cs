using Starter;

namespace Starter.Tests;

public sealed class AttemptRunnerTests
{
    [Fact]
    public async Task RunWithRetryAsync_AbandonedAttemptLaterCompletes_OnCompletedFiresOnlyOnce()
    {
        var firstAttempt = new TaskCompletionSource<string>();
        var secondAttempt = new TaskCompletionSource<string>();
        int completedCount = 0;
        var completedResults = new List<string>();

        void OnCompleted(string result)
        {
            Interlocked.Increment(ref completedCount);
            lock (completedResults)
            {
                completedResults.Add(result);
            }
        }

        int callIndex = 0;
        Task<string> Operation(CancellationToken ct)
        {
            int thisCall = Interlocked.Increment(ref callIndex);
            // Deliberately ignores the cancellation token, like a real downstream call
            // that doesn't observe cancellation.
            return thisCall == 1 ? firstAttempt.Task : secondAttempt.Task;
        }

        var runner = new AttemptRunner();
        Task<string> runTask = runner.RunWithRetryAsync(
            Operation, maxAttempts: 2, perAttemptTimeout: TimeSpan.FromMilliseconds(50), OnCompleted);

        // Let the second attempt succeed quickly - the first attempt's timeout (50ms)
        // will have already elapsed and triggered the retry by the time this resolves.
        secondAttempt.SetResult("attempt-2-result");
        string result = await runTask;

        Assert.Equal("attempt-2-result", result);
        Assert.Equal(1, completedCount);

        // The abandoned first attempt finally "arrives" late.
        firstAttempt.SetResult("attempt-1-late-result");
        await Task.Delay(100); // let its continuation run; not timing-sensitive to the bug itself

        Assert.Equal(1, completedCount);
        Assert.DoesNotContain("attempt-1-late-result", completedResults);
    }

    [Fact]
    public async Task RunWithRetryAsync_FirstAttemptSucceedsWithinTimeout_ReturnsItsResult()
    {
        var runner = new AttemptRunner();

        string result = await runner.RunWithRetryAsync(
            _ => Task.FromResult("immediate-result"),
            maxAttempts: 3,
            perAttemptTimeout: TimeSpan.FromSeconds(1),
            _ => { });

        Assert.Equal("immediate-result", result);
    }
}
