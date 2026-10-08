using System.Net.Http.Json;
using PaymentDemo.Core;

namespace TransactionWorker;

public sealed class ProcessorSimulatorClient
{
    private readonly HttpClient _httpClient;

    public ProcessorSimulatorClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ProcessorSimulationResult> SimulateAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        var payload = new SimulateRequestPayload
        {
            TransactionId = transaction.TransactionId,
            AmountMinorUnits = transaction.AmountMinorUnits,
            RequestedOutcome = transaction.RequestedOutcome
        };

        try
        {
            using HttpResponseMessage response =
                await _httpClient.PostAsJsonAsync("/simulate", payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return ProcessorSimulationResult.TransientFailure(
                    $"simulator returned HTTP {(int)response.StatusCode}");
            }

            SimulateResponsePayload? body =
                await response.Content.ReadFromJsonAsync<SimulateResponsePayload>(cancellationToken: cancellationToken);

            if (body is null)
            {
                return ProcessorSimulationResult.TransientFailure("simulator returned an empty body");
            }

            return ProcessorSimulationResult.Success(body.Outcome, body.ProcessorReference);
        }
        catch (HttpRequestException ex)
        {
            return ProcessorSimulationResult.TransientFailure($"request to simulator failed: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The HttpClient's own request timeout fired, not our cancellation token.
            return ProcessorSimulationResult.TransientFailure("request to simulator timed out");
        }
    }

    private sealed class SimulateRequestPayload
    {
        public required string TransactionId { get; init; }
        public long AmountMinorUnits { get; init; }
        public string? RequestedOutcome { get; init; }
    }

    private sealed class SimulateResponsePayload
    {
        public required string Outcome { get; init; }
        public required string ProcessorReference { get; init; }
    }
}

public readonly record struct ProcessorSimulationResult
{
    public bool IsTransientFailure { get; private init; }
    public string? Outcome { get; private init; }
    public string? ProcessorReference { get; private init; }
    public string? FailureReason { get; private init; }

    public static ProcessorSimulationResult Success(string outcome, string processorReference) => new()
    {
        IsTransientFailure = false,
        Outcome = outcome,
        ProcessorReference = processorReference
    };

    public static ProcessorSimulationResult TransientFailure(string reason) => new()
    {
        IsTransientFailure = true,
        FailureReason = reason
    };
}
