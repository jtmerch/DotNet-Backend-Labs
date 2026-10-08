using PaymentDemo.Core;

namespace PaymentDemo.Core.Tests;

/// <summary>
/// Same behavioral contract run against every ITransactionRepository implementation,
/// so the in-memory and SQLite stores are held to identical, implementation-agnostic
/// expectations rather than duplicating near-identical test files.
/// </summary>
public abstract class TransactionRepositoryContractTests
{
    protected abstract ITransactionRepository CreateRepository();

    // Comfortably larger than anything these tests do, so "no stale reclaim" tests
    // aren't accidentally sensitive to how long the test itself took to run.
    private static readonly TimeSpan NoStaleReclaim = TimeSpan.FromMinutes(5);

    private static Transaction NewTransaction(
        string transactionId, string idempotencyKey, DateTimeOffset createdAt, DateTimeOffset? updatedAt = null) => new()
    {
        TransactionId = transactionId,
        IdempotencyKey = idempotencyKey,
        DemoAccountId = "acct_demo_001",
        AmountMinorUnits = 1500,
        Currency = "USD",
        RequestedOutcome = null,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt ?? createdAt
    };

    [Fact]
    public void Add_NewTransaction_CanBeFoundById()
    {
        ITransactionRepository repository = CreateRepository();
        Transaction transaction = NewTransaction("txn_1", "key_1", DateTimeOffset.UtcNow);

        repository.Add(transaction);

        Transaction? found = repository.FindById("txn_1");
        Assert.NotNull(found);
        Assert.Equal("txn_1", found!.TransactionId);
        Assert.Equal(TransactionState.Pending, found.State);
    }

    [Fact]
    public void Add_DuplicateIdempotencyKey_ReturnsOriginalTransactionNotANewOne()
    {
        ITransactionRepository repository = CreateRepository();
        Transaction first = NewTransaction("txn_1", "shared_key", DateTimeOffset.UtcNow);
        Transaction second = NewTransaction("txn_2", "shared_key", DateTimeOffset.UtcNow);

        Transaction firstResult = repository.Add(first);
        Transaction secondResult = repository.Add(second);

        Assert.Equal(firstResult.TransactionId, secondResult.TransactionId);
        Assert.Null(repository.FindById("txn_2"));
    }

    [Fact]
    public void FindById_UnknownId_ReturnsNull()
    {
        ITransactionRepository repository = CreateRepository();

        Assert.Null(repository.FindById("does_not_exist"));
    }

    [Fact]
    public void ClaimNextPending_WithNoPendingTransactions_ReturnsNull()
    {
        ITransactionRepository repository = CreateRepository();

        Assert.Null(repository.ClaimNextPending(NoStaleReclaim));
    }

    [Fact]
    public void ClaimNextPending_ClaimsOldestFirstAndFlipsStateToSubmitted()
    {
        ITransactionRepository repository = CreateRepository();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        repository.Add(NewTransaction("txn_newer", "key_newer", now.AddSeconds(5)));
        repository.Add(NewTransaction("txn_older", "key_older", now));

        Transaction? claimed = repository.ClaimNextPending(NoStaleReclaim);

        Assert.NotNull(claimed);
        Assert.Equal("txn_older", claimed!.TransactionId);
        Assert.Equal(TransactionState.Submitted, claimed.State);
        Assert.Equal(TransactionState.Submitted, repository.FindById("txn_older")!.State);
        Assert.Equal(TransactionState.Pending, repository.FindById("txn_newer")!.State);
    }

    [Fact]
    public void ClaimNextPending_CalledTwice_NeverReturnsTheSameTransactionTwice()
    {
        ITransactionRepository repository = CreateRepository();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        repository.Add(NewTransaction("txn_a", "key_a", now));
        repository.Add(NewTransaction("txn_b", "key_b", now.AddSeconds(1)));

        Transaction? firstClaim = repository.ClaimNextPending(NoStaleReclaim);
        Transaction? secondClaim = repository.ClaimNextPending(NoStaleReclaim);
        Transaction? thirdClaim = repository.ClaimNextPending(NoStaleReclaim);

        Assert.NotNull(firstClaim);
        Assert.NotNull(secondClaim);
        Assert.Null(thirdClaim);
        Assert.NotEqual(firstClaim!.TransactionId, secondClaim!.TransactionId);
    }

    [Fact]
    public void Save_PersistsStateAttemptsAndProcessorReference()
    {
        ITransactionRepository repository = CreateRepository();
        repository.Add(NewTransaction("txn_1", "key_1", DateTimeOffset.UtcNow));
        Transaction claimed = repository.ClaimNextPending(NoStaleReclaim)!;

        claimed.State = TransactionState.Approved;
        claimed.Attempts = 2;
        claimed.ProcessorReference = "sim_ref_test";
        claimed.UpdatedAt = DateTimeOffset.UtcNow;
        repository.Save(claimed);

        Transaction reloaded = repository.FindById("txn_1")!;
        Assert.Equal(TransactionState.Approved, reloaded.State);
        Assert.Equal(2, reloaded.Attempts);
        Assert.Equal("sim_ref_test", reloaded.ProcessorReference);
    }

    [Fact]
    public void ClaimNextPending_SubmittedTransactionNotYetStale_IsNotReclaimed()
    {
        ITransactionRepository repository = CreateRepository();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        repository.Add(NewTransaction("txn_1", "key_1", now.AddMinutes(-1), updatedAt: now.AddSeconds(-5)));
        repository.ClaimNextPending(NoStaleReclaim); // moves it to Submitted, updated 5s ago

        Transaction? reclaimed = repository.ClaimNextPending(TimeSpan.FromSeconds(30));

        Assert.Null(reclaimed);
    }

    [Fact]
    public void ClaimNextPending_SubmittedTransactionOlderThanThreshold_IsReclaimed()
    {
        ITransactionRepository repository = CreateRepository();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        repository.Add(NewTransaction("txn_1", "key_1", now.AddMinutes(-2), updatedAt: now.AddMinutes(-2)));
        repository.ClaimNextPending(NoStaleReclaim); // moves it to Submitted, "crashes" there

        // Force the clock-in-the-past scenario: a worker claimed it two minutes ago and
        // never finished, so it's now eligible again under a 30s staleness threshold.
        Transaction stillSubmitted = repository.FindById("txn_1")!;
        stillSubmitted.UpdatedAt = now.AddMinutes(-2);
        repository.Save(stillSubmitted);

        Transaction? reclaimed = repository.ClaimNextPending(TimeSpan.FromSeconds(30));

        Assert.NotNull(reclaimed);
        Assert.Equal("txn_1", reclaimed!.TransactionId);
        Assert.Equal(TransactionState.Submitted, repository.FindById("txn_1")!.State);
    }

    [Fact]
    public void ClaimNextPending_CalledConcurrently_NeverDoubleClaimsAnyTransaction()
    {
        ITransactionRepository repository = CreateRepository();
        const int transactionCount = 25;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        for (int i = 0; i < transactionCount; i++)
        {
            repository.Add(NewTransaction($"txn_{i}", $"key_{i}", now.AddMilliseconds(i)));
        }

        Transaction?[] results = new Transaction?[transactionCount + 10];
        Parallel.For(0, results.Length, i =>
        {
            results[i] = repository.ClaimNextPending(NoStaleReclaim);
        });

        List<string> claimedIds = results.Where(t => t is not null).Select(t => t!.TransactionId).ToList();

        Assert.Equal(transactionCount, claimedIds.Count);
        Assert.Equal(transactionCount, claimedIds.Distinct().Count());
    }
}
