namespace ProcessorSimulator.Contracts;

public sealed class SimulateRequest
{
    public string? TransactionId { get; set; }
    public long AmountMinorUnits { get; set; }
    public string? RequestedOutcome { get; set; }
}
