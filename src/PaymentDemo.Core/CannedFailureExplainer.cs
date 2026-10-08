namespace PaymentDemo.Core;

/// <summary>
/// Default explainer when no AI provider is configured. Deterministic, no network -
/// used in tests and in any run without an API key set.
/// </summary>
public sealed class CannedFailureExplainer : IFailureExplainer
{
    public Task<string> ExplainAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        string explanation =
            $"Transaction {transaction.TransactionId} failed after {transaction.Attempts} attempt(s) " +
            $"with requested outcome '{transaction.RequestedOutcome ?? "approved"}'. " +
            "No AI explanation available for this run - set ANTHROPIC_API_KEY to enable one.";

        return Task.FromResult(explanation);
    }
}
