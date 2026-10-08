namespace ProcessorSimulator.Contracts;

public sealed class SimulateResponse
{
    public required string Outcome { get; init; }
    public required string ProcessorReference { get; init; }
}
