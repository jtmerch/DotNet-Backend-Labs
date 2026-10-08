namespace PaymentDemo.Core;

public sealed class Transaction
{
    public required string TransactionId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string DemoAccountId { get; init; }
    public required long AmountMinorUnits { get; init; }
    public required string Currency { get; init; }
    public required string? RequestedOutcome { get; init; }

    public TransactionState State { get; set; } = TransactionState.Pending;
    public int Attempts { get; set; }
    public string? ProcessorReference { get; set; }
    public string? FailureExplanation { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsTerminal =>
        State is TransactionState.Approved or TransactionState.Declined or TransactionState.Failed;
}
