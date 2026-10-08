using System.Diagnostics;
using PaymentDemo.Core;
using ProcessorSimulator.Contracts;

namespace ProcessorSimulator;

public static class SimulatorEndpoints
{
    public static void MapSimulatorEndpoints(this WebApplication app)
    {
        app.MapPost("/simulate", Simulate);
    }

    public static IResult Simulate(SimulateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TransactionId))
        {
            return Results.BadRequest(new { error = "transactionId is required." });
        }

        SimulatedOutcome outcome = ParseOutcome(request.RequestedOutcome);
        string reference = $"sim_ref_{Guid.NewGuid():N}"[..13];

        Activity.Current?.SetTag("simulate.transaction_id", request.TransactionId);
        Activity.Current?.SetTag("simulate.outcome", outcome.ToString());

        return outcome switch
        {
            SimulatedOutcome.Timeout => Results.StatusCode(StatusCodes.Status504GatewayTimeout),
            SimulatedOutcome.Error => Results.StatusCode(StatusCodes.Status500InternalServerError),
            _ => Results.Ok(new SimulateResponse
            {
                Outcome = outcome.ToString().ToLowerInvariant(),
                ProcessorReference = reference
            })
        };
    }

    private static SimulatedOutcome ParseOutcome(string? requestedOutcome)
    {
        if (string.IsNullOrWhiteSpace(requestedOutcome))
        {
            return SimulatedOutcome.Approved;
        }

        return Enum.TryParse<SimulatedOutcome>(requestedOutcome, ignoreCase: true, out SimulatedOutcome parsed)
            ? parsed
            : SimulatedOutcome.Approved;
    }
}
