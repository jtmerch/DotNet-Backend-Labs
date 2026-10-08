using PaymentDemo.Core;

namespace TransactionWorker.Tests;

public sealed class RecordingFailureExplainer : IFailureExplainer
{
    private readonly string _explanation;

    public RecordingFailureExplainer(string explanation)
    {
        _explanation = explanation;
    }

    public int CallCount { get; private set; }

    public Task<string> ExplainAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(_explanation);
    }
}

public sealed class ThrowingFailureExplainer : IFailureExplainer
{
    public Task<string> ExplainAsync(Transaction transaction, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Simulated AI provider outage.");
}
