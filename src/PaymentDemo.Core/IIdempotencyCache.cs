namespace PaymentDemo.Core;

/// <summary>
/// Cache-aside accelerator for idempotency-key lookups. SQLite remains the source of
/// truth; this exists purely to skip a database round-trip on repeat submissions.
/// Implementations must tolerate the backing store being unavailable - a miss here
/// just means the caller falls through to the database, not a failure.
/// </summary>
public interface IIdempotencyCache
{
    Task<string?> TryGetTransactionIdAsync(string demoAccountId, string idempotencyKey, CancellationToken cancellationToken);

    Task SetAsync(string demoAccountId, string idempotencyKey, string transactionId, CancellationToken cancellationToken);
}
