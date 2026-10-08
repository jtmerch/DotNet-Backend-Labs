using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using PaymentDemo.Core;

namespace TransactionWorker;

/// <summary>
/// Calls the Anthropic Messages API to get a plain-English explanation of why a
/// transaction failed. Only used when ANTHROPIC_API_KEY is actually set - see
/// Program.cs, which falls back to CannedFailureExplainer otherwise. The prompt
/// explicitly tells the model this is synthetic demo data, since it is.
/// </summary>
public sealed class ClaudeFailureExplainer : IFailureExplainer
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public ClaudeFailureExplainer(HttpClient httpClient, string model)
    {
        _httpClient = httpClient;
        _model = model;
    }

    public async Task<string> ExplainAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        string prompt =
            "This is synthetic demo data from a payment simulator - no real cardholder or " +
            "merchant data. In one or two plain-English sentences, explain the likely cause " +
            $"of this failed transaction and suggest what a payments engineer should check:\n" +
            $"- requested outcome: {transaction.RequestedOutcome ?? "approved"}\n" +
            $"- attempts made: {transaction.Attempts}\n" +
            $"- amount: {transaction.AmountMinorUnits} {transaction.Currency} (minor units)";

        var request = new ClaudeRequest
        {
            Model = _model,
            MaxTokens = 200,
            Messages = [new ClaudeMessage { Role = "user", Content = prompt }]
        };

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/v1/messages", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        ClaudeResponse? body = await response.Content.ReadFromJsonAsync<ClaudeResponse>(cancellationToken: cancellationToken);
        string? text = body?.Content.FirstOrDefault()?.Text;

        return string.IsNullOrWhiteSpace(text)
            ? "AI explanation request returned no text."
            : text.Trim();
    }

    public static HttpClient BuildHttpClient(string apiKey)
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.anthropic.com") };
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private sealed class ClaudeRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("max_tokens")]
        public required int MaxTokens { get; init; }

        [JsonPropertyName("messages")]
        public required ClaudeMessage[] Messages { get; init; }
    }

    private sealed class ClaudeMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    private sealed class ClaudeResponse
    {
        [JsonPropertyName("content")]
        public required ClaudeContentBlock[] Content { get; init; }
    }

    private sealed class ClaudeContentBlock
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }
    }
}
