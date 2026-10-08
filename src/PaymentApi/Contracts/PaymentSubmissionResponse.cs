namespace PaymentApi.Contracts;

public sealed class PaymentSubmissionResponse
{
    public required string TransactionId { get; init; }
    public required string State { get; init; }
}
