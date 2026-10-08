using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ProcessorSimulator.Contracts;

namespace ProcessorSimulator.Tests;

public sealed class SimulatorEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SimulatorEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Simulate_WithNoRequestedOutcome_DefaultsToApproved()
    {
        var request = new SimulateRequest { TransactionId = "txn_abc", AmountMinorUnits = 1000 };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/simulate", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SimulateResponse>();
        Assert.Equal("approved", body!.Outcome);
        Assert.StartsWith("sim_ref_", body.ProcessorReference);
    }

    [Theory]
    [InlineData("approved", HttpStatusCode.OK)]
    [InlineData("declined", HttpStatusCode.OK)]
    [InlineData("timeout", HttpStatusCode.GatewayTimeout)]
    [InlineData("error", HttpStatusCode.InternalServerError)]
    public async Task Simulate_WithRequestedOutcome_ReturnsMatchingResult(string requestedOutcome, HttpStatusCode expectedStatus)
    {
        var request = new SimulateRequest
        {
            TransactionId = "txn_abc",
            AmountMinorUnits = 1000,
            RequestedOutcome = requestedOutcome
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/simulate", request);

        Assert.Equal(expectedStatus, response.StatusCode);

        if (expectedStatus == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadFromJsonAsync<SimulateResponse>();
            Assert.Equal(requestedOutcome, body!.Outcome);
        }
    }

    [Fact]
    public async Task Simulate_MissingTransactionId_ReturnsBadRequest()
    {
        var request = new SimulateRequest { AmountMinorUnits = 1000 };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/simulate", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
