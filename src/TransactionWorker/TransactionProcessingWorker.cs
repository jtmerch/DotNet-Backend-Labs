using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentDemo.Core;

namespace TransactionWorker;

public sealed class TransactionProcessingWorker : BackgroundService
{
    private readonly ITransactionRepository _repository;
    private readonly ProcessorSimulatorClient _simulatorClient;
    private readonly IWorkerDelay _delay;
    private readonly IFailureExplainer _failureExplainer;
    private readonly WorkerOptions _options;
    private readonly ILogger<TransactionProcessingWorker> _logger;

    public TransactionProcessingWorker(
        ITransactionRepository repository,
        ProcessorSimulatorClient simulatorClient,
        IWorkerDelay delay,
        IFailureExplainer failureExplainer,
        IOptions<WorkerOptions> options,
        ILogger<TransactionProcessingWorker> logger)
    {
        _repository = repository;
        _simulatorClient = simulatorClient;
        _delay = delay;
        _failureExplainer = failureExplainer;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool processedSomething = await ProcessNextPendingTransactionAsync(stoppingToken);

            if (!processedSomething)
            {
                await _delay.WaitAsync(_options.PollInterval, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Claims and fully resolves at most one transaction, including its retries.
    /// Returns false when there was nothing pending to claim. Exposed internally so
    /// tests can drive one pass at a time instead of running the full hosted loop.
    /// </summary>
    internal async Task<bool> ProcessNextPendingTransactionAsync(CancellationToken cancellationToken)
    {
        Transaction? transaction;
        using (Activity? claimActivity = PaymentDemoTelemetry.Source.StartActivity("claim-next-pending"))
        {
            transaction = _repository.ClaimNextPending(_options.StaleClaimThreshold);
            claimActivity?.SetTag("claim.found", transaction is not null);
            if (transaction is not null)
            {
                claimActivity?.SetTag("transaction.id", transaction.TransactionId);
            }
        }

        if (transaction is null)
        {
            return false;
        }

        for (int attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            transaction.Attempts = attempt;

            // Scoped tightly to the simulator call itself - the retry backoff below is
            // dead time between attempts, not part of this attempt, and deserves its
            // own span rather than inflating this one's duration.
            ProcessorSimulationResult result;
            using (Activity? attemptActivity = PaymentDemoTelemetry.Source.StartActivity("simulate-attempt"))
            {
                attemptActivity?.SetTag("transaction.id", transaction.TransactionId);
                attemptActivity?.SetTag("attempt.number", attempt);

                result = await _simulatorClient.SimulateAsync(transaction, cancellationToken);
                attemptActivity?.SetTag("attempt.transient_failure", result.IsTransientFailure);
                attemptActivity?.SetTag("attempt.outcome", result.IsTransientFailure ? result.FailureReason : result.Outcome);
            }

            if (!result.IsTransientFailure)
            {
                transaction.State = result.Outcome == "approved" ? TransactionState.Approved : TransactionState.Declined;
                transaction.ProcessorReference = result.ProcessorReference;
                transaction.UpdatedAt = DateTimeOffset.UtcNow;
                _repository.Save(transaction);

                _logger.LogInformation(
                    "Transaction {TransactionId} resolved as {State} after {Attempts} attempt(s).",
                    transaction.TransactionId, transaction.State, transaction.Attempts);
                return true;
            }

            _logger.LogWarning(
                "Transaction {TransactionId} attempt {Attempt} failed: {Reason}",
                transaction.TransactionId, attempt, result.FailureReason);

            if (attempt == _options.MaxAttempts)
            {
                transaction.State = TransactionState.Failed;
                transaction.UpdatedAt = DateTimeOffset.UtcNow;

                // Best-effort only: a failed or slow explanation never changes the
                // fact that this transaction is correctly marked Failed.
                try
                {
                    transaction.FailureExplanation = await _failureExplainer.ExplainAsync(transaction, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failure explanation unavailable for transaction {TransactionId}.", transaction.TransactionId);
                }

                _repository.Save(transaction);

                _logger.LogWarning(
                    "Transaction {TransactionId} marked Failed after exhausting {MaxAttempts} attempt(s).",
                    transaction.TransactionId, _options.MaxAttempts);
                return true;
            }

            using (Activity? backoffActivity = PaymentDemoTelemetry.Source.StartActivity("retry-backoff-wait"))
            {
                backoffActivity?.SetTag("transaction.id", transaction.TransactionId);
                await _delay.WaitAsync(_options.RetryBackoff, cancellationToken);
            }
        }

        throw new UnreachableException("The loop above always returns before exhausting its attempts.");
    }
}
