namespace PaymentDemo.Core;

public interface ITransactionRepository
{
    Transaction Add(Transaction transaction);

    Transaction? FindById(string transactionId);

    Transaction? FindByIdempotencyKey(string demoAccountId, string idempotencyKey);

    /// <summary>
    /// Atomically claims the oldest transaction eligible for work - either still in
    /// <see cref="TransactionState.Pending"/>, or stuck in <see cref="TransactionState.Submitted"/>
    /// for longer than <paramref name="staleSubmittedThreshold"/> (meaning whatever worker
    /// claimed it previously likely crashed before finishing). Flips the winner to
    /// <see cref="TransactionState.Submitted"/>, persists that change, and returns it.
    /// Returns null when there is nothing to claim. Safe to call from more than one
    /// worker at a time - two callers can never claim the same transaction.
    /// </summary>
    Transaction? ClaimNextPending(TimeSpan staleSubmittedThreshold);

    /// <summary>
    /// Persists the current in-memory state of a previously claimed transaction
    /// (state, attempts, processor reference, updated-at).
    /// </summary>
    void Save(Transaction transaction);
}
