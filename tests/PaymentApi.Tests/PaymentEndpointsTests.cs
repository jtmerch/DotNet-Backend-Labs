using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaymentApi.Contracts;
using PaymentDemo.Core;

namespace PaymentApi.Tests;

public sealed class PaymentEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public PaymentEndpointsTests(WebApplicationFactory<Program> factory)
    {
        // The app wires up the shared SQLite store by default; tests swap in a fresh
        // in-memory repository per test class instead, so runs stay fast and isolated
        // from whatever is on disk.
        WebApplicationFactory<Program> isolatedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ITransactionRepository, InMemoryTransactionRepository>();
                services.AddSingleton<IIdempotencyCache, FakeIdempotencyCache>();
            }));

        _client = isolatedFactory.CreateClient();
    }

    private static PaymentSubmissionRequest ValidRequest(string? simulatedOutcome = null) => new()
    {
        DemoAccountId = "acct_demo_001",
        AmountMinorUnits = 2599,
        Currency = "USD",
        SimulatedOutcome = simulatedOutcome
    };

    private HttpRequestMessage BuildSubmission(PaymentSubmissionRequest request, string idempotencyKey)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return message;
    }

    [Fact]
    public async Task SubmitPayment_WithValidRequest_ReturnsAcceptedWithPendingState()
    {
        using HttpRequestMessage message = BuildSubmission(ValidRequest(), Guid.NewGuid().ToString());

        HttpResponseMessage response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaymentSubmissionResponse>();
        Assert.NotNull(body);
        Assert.Equal("Pending", body!.State);
        Assert.StartsWith("txn_", body.TransactionId);
    }

    [Fact]
    public async Task SubmitPayment_MissingIdempotencyKey_ReturnsBadRequest()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/payments", ValidRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0, "USD")]
    [InlineData(-5, "USD")]
    [InlineData(2599, "US")]
    [InlineData(2599, "")]
    public async Task SubmitPayment_WithInvalidFields_ReturnsBadRequest(long amount, string currency)
    {
        var request = new PaymentSubmissionRequest
        {
            DemoAccountId = "acct_demo_001",
            AmountMinorUnits = amount,
            Currency = currency
        };
        using HttpRequestMessage message = BuildSubmission(request, Guid.NewGuid().ToString());

        HttpResponseMessage response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SubmitPayment_UnknownSimulatedOutcome_ReturnsBadRequest()
    {
        using HttpRequestMessage message = BuildSubmission(ValidRequest("not-a-real-outcome"), Guid.NewGuid().ToString());

        HttpResponseMessage response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SubmitPayment_RepeatedIdempotencyKey_ReturnsOriginalTransactionNotANewOne()
    {
        string idempotencyKey = Guid.NewGuid().ToString();

        using HttpRequestMessage first = BuildSubmission(ValidRequest(), idempotencyKey);
        HttpResponseMessage firstResponse = await _client.SendAsync(first);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<PaymentSubmissionResponse>();

        using HttpRequestMessage second = BuildSubmission(ValidRequest(), idempotencyKey);
        HttpResponseMessage secondResponse = await _client.SendAsync(second);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<PaymentSubmissionResponse>();

        Assert.Equal(firstBody!.TransactionId, secondBody!.TransactionId);
    }

    [Fact]
    public async Task SubmitPayment_ConcurrentDuplicateRequests_CreateExactlyOneTransaction()
    {
        string idempotencyKey = Guid.NewGuid().ToString();

        Task<HttpResponseMessage>[] submissions = Enumerable.Range(0, 20)
            .Select(_ => _client.SendAsync(BuildSubmission(ValidRequest(), idempotencyKey)))
            .ToArray();

        HttpResponseMessage[] responses = await Task.WhenAll(submissions);

        var transactionIds = new HashSet<string>();
        foreach (HttpResponseMessage response in responses)
        {
            var body = await response.Content.ReadFromJsonAsync<PaymentSubmissionResponse>();
            transactionIds.Add(body!.TransactionId);
        }

        Assert.Single(transactionIds);
    }

    [Fact]
    public async Task GetPayment_UnknownId_ReturnsNotFound()
    {
        HttpResponseMessage response = await _client.GetAsync("/payments/txn_does_not_exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPayment_KnownId_ReturnsSubmittedDetails()
    {
        using HttpRequestMessage submission = BuildSubmission(ValidRequest(), Guid.NewGuid().ToString());
        HttpResponseMessage submissionResponse = await _client.SendAsync(submission);
        var submissionBody = await submissionResponse.Content.ReadFromJsonAsync<PaymentSubmissionResponse>();

        HttpResponseMessage getResponse = await _client.GetAsync($"/payments/{submissionBody!.TransactionId}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getBody = await getResponse.Content.ReadFromJsonAsync<TransactionResponse>();
        Assert.Equal(submissionBody.TransactionId, getBody!.TransactionId);
        Assert.Equal("acct_demo_001", getBody.DemoAccountId);
        Assert.Equal(2599, getBody.AmountMinorUnits);
        Assert.Equal("USD", getBody.Currency);
        Assert.Equal("Pending", getBody.State);
    }
}
