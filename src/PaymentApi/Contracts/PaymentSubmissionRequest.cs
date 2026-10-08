namespace PaymentApi.Contracts;

public sealed class PaymentSubmissionRequest
{
    public string? DemoAccountId { get; set; }
    public long AmountMinorUnits { get; set; }
    public string? Currency { get; set; }
    public string? SimulatedOutcome { get; set; }
}
