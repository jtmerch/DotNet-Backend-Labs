using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentDemo.Core;
using TransactionWorker;

namespace TransactionWorker.Tests;

public sealed class TransactionProcessingWorkerTests
{
    private static Transaction NewPendingTransaction(
        string transactionId, string? requestedOutcome = null, DateTimeOffset? createdAt = null)
    {
        DateTimeOffset timestamp = createdAt ?? DateTimeOffset.UtcNow;
        return new Transaction
        {
            TransactionId = transactionId,
            IdempotencyKey = $"key_{transactionId}",
            DemoAccountId = "acct_demo_001",
            AmountMinorUnits = 1000,
            Currency = "USD",
            RequestedOutcome = requestedOutcome,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    private static TransactionProcessingWorker BuildWorker(
        ITransactionRepository repository,
        ScriptedHttpMessageHandler handler,
        FakeWorkerDelay delay,
        int maxAttempts = 3,
        TimeSpan? staleClaimThreshold = null,
        IFailureExplainer? failureExplainer = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5099") };
        var simulatorClient = new ProcessorSimulatorClient(httpClient);
        var options = Options.Create(new WorkerOptions
        {
            MaxAttempts = maxAttempts,
            RetryBackoff = TimeSpan.FromSeconds(2),
            PollInterval = TimeSpan.FromSeconds(1),
            StaleClaimThreshold = staleClaimThreshold ?? TimeSpan.FromSeconds(30)
        });

        return new TransactionProcessingWorker(
            repository, simulatorClient, delay, failureExplainer ?? new CannedFailureExplainer(),
            options, NullLogger<TransactionProcessingWorker>.Instance);
    }

    [Fact]
    public async Task ProcessNextPendingTransactionAsync_WithNothingPending_ReturnsFalseAndMakesNoCalls()
    {
        var repository = new InMemoryTransactionRepository();
        var handler = new ScriptedHttpMessageHandler();
        var delay = new FakeWorkerDelay();
        TransactionProcessingWorker worker = BuildWorker(repository, handler, delay);

        bool processed = await worker.ProcessNextPendingTransactionAsync(CancellationToken.None);

        Assert.False(processed);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(delay.Requested);
    }

    [Fact]
    public async Task ProcessNextPendingTransactionAsync_ApprovedOnFirstTry_MarksApprovedWithOneAttempt()
    {
        var repository = new InMemoryTransactionRepository();
        repository.Add(NewPendingTransaction("txn_1"));
        var handler = new ScriptedHttpMessageHandler(ScriptedHttpMessageHandler.Approved());
        var delay = new FakeWorkerDelay();
        TransactionProcessingWorker worker = BuildWorker(repository, handler, delay);

        bool processed = await worker.ProcessNextPendingTransactionAsync(CancellationToken.None);

        Assert.True(processed);
        Transaction stored = repository.FindById("txn_1")!;
        Assert.Equal(TransactionState.Approved, stored.State);
        Assert.Equal(1, stored.Attempts);
        Assert.Equal("sim_ref_test", stored.ProcessorReference);
        Assert.Empty(delay.Requested);
    }

    [Fact]
    public async Task ProcessNextPendingTransactionAsync_DeclinedOnFirstTry_MarksDeclined()
    {
        var repository = new InMemoryTransactionRepository();
        repository.Add(NewPendingTransaction("txn_1"));
        var handler = new ScriptedHttpMessageHandler(ScriptedHttpMessageHandler.Declined());
        var delay = new FakeWorkerDelay();
        TransactionProcessingWorker worker = BuildWorker(repository, handler, delay);

        await worker.ProcessNextPendingTransactionAsync(CancellationToken.None);

        Assert.Equal(TransactionState.Declined, repository.FindById("txn_1")!.State);
    }

    [Fact]
    public async Task ProcessNextPendingTransactionAsync_TimeoutThenApproved_RetriesOnceThenSucceeds()
    {
        var repository = new InMemoryTransactionRepository();
        repository.Add(NewPendingTransaction("txn_1"));
        var handler = new ScriptedHttpMessageHandler(
            ScriptedHttpMessageHandler.Timeout(),
            ScriptedHttpMessageHandler.Approved());
        var delay = new FakeWorkerDelay();
        TransactionProcessingWorker worker = BuildWorker(repository, handler, delay);

        await worker.ProcessNextPendingTransactionAsync(CancellationToken.None);

        Transaction stored = repository.FindById("txn_1")!;
        Assert.Equal(TransactionState.Approved, stored.State);
        Assert.Equal(2, stored.Attempts);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, delay.Requested);
    }

    [Fact]
    public async Task ProcessNextPendingTransactionAsync_ExhaustsRetryBudget_MarksFailed()
    {
        var repository = new InMemoryTransactionRepository();
        repository.Add(NewPendingTransaction("txn_1"));
        var handler = new ScriptedHttpMessageHandler(
            ScriptedHttpMessageHandler.Error(),
            ScriptedHttpMessageHandler.Error(),
            ScriptedHttpMessageHandler.Error());
        var delay = new FakeWorkerDelay();
        TransactionProcessingWorker worker = BuildWorker(repository, handler, delay, maxAttempts: 3);

        await worker.ProcessNextPendingTransactionAsync(CancellationToken.None);

        Transaction stored = repository.FindById("txn_1")!;
        Assert.Equal(TransactionState.Failed, stored.State);
        Assert.Equal(3, stored.Attempts);
        Assert.Equal(3, handler.CallCount);
        // Backoff happens between attempts, not after the final failed one.
        Assert.Equal(2, delay.Requested.Count);
        Assert.False(string.IsNullOrWhiteSpace(stored.FailureExplanation));
    }

    [Fact]
    public async Task ProcessNextPendingTransactionAsync_OnFailure_CallsFailureExplainerExactlyOnce()
    {
        var repository = new InMemoryTransactionRepository();
        repository.Add(NewPendingTransaction("txn_1"));
        var handler = new ScriptedHttpMessageHandler(
            ScriptedHttpMessageHandler.Error(), ScriptedHttpMessageHandler.Error());
        var delay = new FakeWorkerDelay();
        var explainer = new RecordingFailureExplainer("explanation-for-txn_1");
        TransactionProcessingWorker worker = BuildWorker(
            repository, handler, delay, maxAttempts: 2, failureExplainer: explainer);

        await worker.ProcessNextPendingTransactionAsync(CancellationToken.None);

        Assert.Equal(1, explainer.CallCount);
        Assert.Equal("explanation-for-txn_1", repository.FindById("txn_1")!.FailureExplanation);
    }

    [Fact]
    public async Task ProcessNextPendingTransactionAsync_OnSuccess_NeverCallsFailureExplainer()
    {
        var repository = new InMemoryTransactionRepository();
        repository.Add(NewPendingTransaction("txn_1"));
        var handler = new ScriptedHttpMessageHandler(ScriptedHttpMessageHandler.Approved());
        var delay = new FakeWorkerDelay();
        var explainer = new RecordingFailureExplainer("unused");
        TransactionProcessingWorker worker = BuildWorker(repository, handler, delay, failureExplainer: explainer);

        await worker.ProcessNextPendingTransactionAsync(CancellationToken.None);

        Assert.Equal(0, explainer.CallCount);
    }

    [Fact]
    public async Task ProcessNextPendingTransactionAsync_ExplainerThrows_StillMarksFailed()
    {
        var repository = new InMemoryTransactionRepository();
        repository.Add(NewPendingTransaction("txn_1"));
        var handler = new ScriptedHttpMessageHandler(ScriptedHttpMessageHandler.Error());
        var delay = new FakeWorkerDelay();
        TransactionProcessingWorker worker = BuildWorker(
            repository, handler, delay, maxAttempts: 1, failureExplainer: new ThrowingFailureExplainer());

        bool processed = await worker.ProcessNextPendingTransactionAsync(CancellationToken.None);

        Assert.True(processed);
        Transaction stored = repository.FindById("txn_1")!;
        Assert.Equal(TransactionState.Failed, stored.State);
        Assert.Null(stored.FailureExplanation);
    }

    [Fact]
    public async Task ProcessNextPendingTransactionAsync_ProcessesOldestPendingTransactionFirst()
    {
        var repository = new InMemoryTransactionRepository();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        repository.Add(NewPendingTransaction("txn_newer", createdAt: now.AddSeconds(5)));
        repository.Add(NewPendingTransaction("txn_older", createdAt: now));
        var handler = new ScriptedHttpMessageHandler(ScriptedHttpMessageHandler.Approved());
        var delay = new FakeWorkerDelay();
        TransactionProcessingWorker worker = BuildWorker(repository, handler, delay);

        await worker.ProcessNextPendingTransactionAsync(CancellationToken.None);

        Assert.Equal(TransactionState.Approved, repository.FindById("txn_older")!.State);
        Assert.Equal(TransactionState.Pending, repository.FindById("txn_newer")!.State);
    }

    [Fact]
    public async Task ProcessNextPendingTransactionAsync_RecoversATransactionStuckSinceAPriorCrash()
    {
        var repository = new InMemoryTransactionRepository();
        Transaction orphaned = NewPendingTransaction("txn_1", createdAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        repository.Add(orphaned);

        // Simulate a previous worker instance that claimed this transaction and then
        // crashed before resolving it: it's Submitted, and hasn't been touched since.
        orphaned.State = TransactionState.Submitted;
        orphaned.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        var handler = new ScriptedHttpMessageHandler(ScriptedHttpMessageHandler.Approved());
        var delay = new FakeWorkerDelay();
        TransactionProcessingWorker worker = BuildWorker(
            repository, handler, delay, staleClaimThreshold: TimeSpan.FromSeconds(30));

        bool processed = await worker.ProcessNextPendingTransactionAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(TransactionState.Approved, repository.FindById("txn_1")!.State);
    }
}
