using Starter;

namespace Starter.Tests;

public sealed class DuplicateSubmissionGuardTests
{
    [Fact]
    public async Task Submit_TwoConcurrentCallsWithSameKey_CreatesExactlyOnceAndReturnsSameResult()
    {
        var guard = new DuplicateSubmissionGuard();
        var gate = new ManualResetEventSlim(initialState: false);
        int callCount = 0;

        string CreateNew()
        {
            Interlocked.Increment(ref callCount);
            gate.Wait(TimeSpan.FromSeconds(5));
            return Guid.NewGuid().ToString();
        }

        // Force the race window deterministically: start the first call and wait until
        // it has entered CreateNew (and is blocked on the gate) before starting the
        // second - then give the second a chance to also enter CreateNew before
        // releasing either of them. A correct implementation never lets the second
        // call reach CreateNew at all; a buggy one does.
        Task<string> first = Task.Run(() => guard.Submit("shared-key", CreateNew));
        bool firstEntered = SpinWait.SpinUntil(() => callCount >= 1, TimeSpan.FromSeconds(2));
        Assert.True(firstEntered, "First call never entered CreateNew.");

        Task<string> second = Task.Run(() => guard.Submit("shared-key", CreateNew));
        SpinWait.SpinUntil(() => callCount >= 2, TimeSpan.FromMilliseconds(500));

        gate.Set();
        string[] results = await Task.WhenAll(first, second);

        Assert.Equal(1, callCount);
        Assert.Equal(results[0], results[1]);
    }

    [Fact]
    public void Submit_SameKeyCalledSequentially_ReturnsSameResultWithoutCreatingAgain()
    {
        var guard = new DuplicateSubmissionGuard();
        int callCount = 0;
        string CreateNew()
        {
            callCount++;
            return $"created-{callCount}";
        }

        string first = guard.Submit("key-1", CreateNew);
        string second = guard.Submit("key-1", CreateNew);

        Assert.Equal(1, callCount);
        Assert.Equal(first, second);
    }
}
