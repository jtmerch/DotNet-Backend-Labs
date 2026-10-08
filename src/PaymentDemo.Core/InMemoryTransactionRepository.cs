using System.Collections.Concurrent;

namespace PaymentDemo.Core;

/// <summary>
/// Thread-safe in-memory store. Resets whenever the process restarts; that is a known
/// and documented limitation for this stage of the project, not an oversight.
/// </summary>
public sealed class InMemoryTransactionRepository : ITransactionRepository
{
    private readonly ConcurrentDictionary<string, Transaction> _byId = new();
    private readonly ConcurrentDictionary<string, string> _byIdempotencyKey = new();
    private readonly object _claimLock = new();

    public Transaction Add(Transaction transaction)
    {
        string idempotencyIndexKey = BuildIdempotencyIndexKey(transaction.DemoAccountId, transaction.IdempotencyKey);

        string winningTransactionId = _byIdempotencyKey.GetOrAdd(idempotencyIndexKey, transaction.TransactionId);

        if (winningTransactionId == transaction.TransactionId)
        {
            _byId[transaction.TransactionId] = transaction;
            return transaction;
        }

        // Another request already claimed this idempotency key first: return that one.
        return _byId[winningTransactionId];
    }

    public Transaction? FindById(string transactionId)
    {
        return _byId.TryGetValue(transactionId, out Transaction? transaction) ? transaction : null;
    }

    public Transaction? FindByIdempotencyKey(string demoAccountId, string idempotencyKey)
    {
        string idempotencyIndexKey = BuildIdempotencyIndexKey(demoAccountId, idempotencyKey);

        if (!_byIdempotencyKey.TryGetValue(idempotencyIndexKey, out string? transactionId))
        {
            return null;
        }

        return FindById(transactionId);
    }

    public Transaction? ClaimNextPending(TimeSpan staleSubmittedThreshold)
    {
        lock (_claimLock)
        {
            DateTimeOffset staleCutoff = DateTimeOffset.UtcNow - staleSubmittedThreshold;

            Transaction? next = _byId.Values
                .Where(t => IsEligible(t, staleCutoff))
                .OrderBy(t => t.State == TransactionState.Pending ? t.CreatedAt : t.UpdatedAt)
                .FirstOrDefault();

            if (next is null)
            {
                return null;
            }

            next.State = TransactionState.Submitted;
            next.UpdatedAt = DateTimeOffset.UtcNow;
            return next;
        }
    }

    private static bool IsEligible(Transaction transaction, DateTimeOffset staleCutoff)
    {
        return transaction.State == TransactionState.Pending
            || (transaction.State == TransactionState.Submitted && transaction.UpdatedAt < staleCutoff);
    }

    public void Save(Transaction transaction)
    {
        // The in-memory store already holds the same object reference the caller is
        // mutating, so there is nothing left to persist - this exists to satisfy the
        // interface and mirror what the SQLite-backed implementation must do.
    }

    private static string BuildIdempotencyIndexKey(string demoAccountId, string idempotencyKey)
    {
        return $"{demoAccountId}:{idempotencyKey}";
    }
}
