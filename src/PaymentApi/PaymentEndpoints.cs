using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PaymentApi.Contracts;
using PaymentDemo.Core;

namespace PaymentApi;

public static class PaymentEndpoints
{
    private static readonly HashSet<string> ValidSimulatedOutcomes =
        new(StringComparer.OrdinalIgnoreCase) { "approved", "declined", "timeout", "error" };

    public static void MapPaymentEndpoints(this WebApplication app)
    {
        app.MapPost("/payments", SubmitPayment);
        app.MapGet("/payments/{transactionId}", GetPayment);
    }

    public static async Task<IResult> SubmitPayment(
        PaymentSubmissionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ITransactionRepository repository,
        IIdempotencyCache idempotencyCache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { error = "Idempotency-Key header is required." });
        }

        if (string.IsNullOrWhiteSpace(request.DemoAccountId))
        {
            return Results.BadRequest(new { error = "demoAccountId is required." });
        }

        if (request.AmountMinorUnits <= 0)
        {
            return Results.BadRequest(new { error = "amountMinorUnits must be greater than zero." });
        }

        if (string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Length != 3)
        {
            return Results.BadRequest(new { error = "currency must be a 3-letter code." });
        }

        if (request.SimulatedOutcome is not null && !ValidSimulatedOutcomes.Contains(request.SimulatedOutcome))
        {
            return Results.BadRequest(new
            {
                error = "simulatedOutcome must be one of: approved, declined, timeout, error."
            });
        }

        // Cache-aside fast path: a hit here still just points at SQLite as the source
        // of truth. A miss (including "the cache isn't available") falls through to
        // the same path as a brand-new submission - correctness never depends on Redis.
        string? cachedTransactionId;
        using (Activity? lookupActivity = PaymentDemoTelemetry.Source.StartActivity("idempotency-cache-lookup"))
        {
            cachedTransactionId =
                await idempotencyCache.TryGetTransactionIdAsync(request.DemoAccountId, idempotencyKey, cancellationToken);
            lookupActivity?.SetTag("cache.hit", cachedTransactionId is not null);
        }

        if (cachedTransactionId is not null)
        {
            Transaction? cached = repository.FindById(cachedTransactionId);
            if (cached is not null)
            {
                return Results.Accepted(
                    $"/payments/{cached.TransactionId}",
                    new PaymentSubmissionResponse { TransactionId = cached.TransactionId, State = cached.State.ToString() });
            }
        }

        Transaction candidate = new()
        {
            TransactionId = $"txn_{Guid.NewGuid():N}"[..14],
            IdempotencyKey = idempotencyKey,
            DemoAccountId = request.DemoAccountId,
            AmountMinorUnits = request.AmountMinorUnits,
            Currency = request.Currency.ToUpperInvariant(),
            RequestedOutcome = request.SimulatedOutcome,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Add() itself resolves duplicate idempotency keys: concurrent callers with the
        // same key race to claim it, and the loser gets the winner's transaction back
        // rather than creating a second one. The cache is only ever a shortcut to this.
        Transaction stored = repository.Add(candidate);
        await idempotencyCache.SetAsync(stored.DemoAccountId, stored.IdempotencyKey, stored.TransactionId, cancellationToken);

        return Results.Accepted(
            $"/payments/{stored.TransactionId}",
            new PaymentSubmissionResponse { TransactionId = stored.TransactionId, State = stored.State.ToString() });
    }

    public static IResult GetPayment(string transactionId, ITransactionRepository repository)
    {
        Transaction? transaction = repository.FindById(transactionId);

        if (transaction is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new TransactionResponse
        {
            TransactionId = transaction.TransactionId,
            DemoAccountId = transaction.DemoAccountId,
            AmountMinorUnits = transaction.AmountMinorUnits,
            Currency = transaction.Currency,
            State = transaction.State.ToString(),
            Attempts = transaction.Attempts,
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt,
            FailureExplanation = transaction.FailureExplanation
        });
    }
}
