namespace PaymentDemo.Core;

/// <summary>
/// Used when Redis wasn't reachable at startup. Every lookup is a miss and every set
/// is a no-op, so the caller always falls through to SQLite - correct, just without
/// the cache speedup.
/// </summary>
public sealed class NullIdempotencyCache : IIdempotencyCache
{
    public Task<string?> TryGetTransactionIdAsync(string demoAccountId, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    public Task SetAsync(string demoAccountId, string idempotencyKey, string transactionId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
