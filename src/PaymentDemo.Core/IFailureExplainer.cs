namespace PaymentDemo.Core;

/// <summary>
/// Produces a plain-English likely-cause explanation for a transaction that exhausted
/// its retry budget. Optional by design - the transaction is already correctly marked
/// Failed regardless of whether this succeeds, so a bad or missing explanation never
/// affects correctness, only the explanation text shown alongside it.
/// </summary>
public interface IFailureExplainer
{
    Task<string> ExplainAsync(Transaction transaction, CancellationToken cancellationToken);
}
