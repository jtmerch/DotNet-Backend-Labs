using System.Collections.Concurrent;
using PaymentDemo.Core;

namespace PaymentApi.Tests;

/// <summary>In-memory stand-in for Redis so the main test suite never depends on a
/// real Redis instance being available.</summary>
public sealed class FakeIdempotencyCache : IIdempotencyCache
{
    private readonly ConcurrentDictionary<string, string> _store = new();

    public Task<string?> TryGetTransactionIdAsync(string demoAccountId, string idempotencyKey, CancellationToken cancellationToken)
    {
        _store.TryGetValue(BuildKey(demoAccountId, idempotencyKey), out string? transactionId);
        return Task.FromResult(transactionId);
    }

    public Task SetAsync(string demoAccountId, string idempotencyKey, string transactionId, CancellationToken cancellationToken)
    {
        _store[BuildKey(demoAccountId, idempotencyKey)] = transactionId;
        return Task.CompletedTask;
    }

    private static string BuildKey(string demoAccountId, string idempotencyKey) => $"{demoAccountId}:{idempotencyKey}";
}
