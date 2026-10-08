namespace PaymentApi.Contracts;

public sealed class TransactionResponse
{
    public required string TransactionId { get; init; }
    public required string DemoAccountId { get; init; }
    public required long AmountMinorUnits { get; init; }
    public required string Currency { get; init; }
    public required string State { get; init; }
    public required int Attempts { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public string? FailureExplanation { get; init; }
}
