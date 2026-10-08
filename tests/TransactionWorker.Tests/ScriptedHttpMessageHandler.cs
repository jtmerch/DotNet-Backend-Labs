using System.Net;

namespace TransactionWorker.Tests;

/// <summary>Returns one scripted response per call, in order, standing in for the
/// ProcessorSimulator over HTTP so tests don't need a real second process running.</summary>
public sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses;

    public ScriptedHttpMessageHandler(params Func<HttpResponseMessage>[] responses)
    {
        _responses = new Queue<Func<HttpResponseMessage>>(responses);
    }

    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No more scripted responses were configured for this test.");
        }

        return Task.FromResult(_responses.Dequeue()());
    }

    public static Func<HttpResponseMessage> Approved() => () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = JsonContent("""{"outcome":"approved","processorReference":"sim_ref_test"}""")
    };

    public static Func<HttpResponseMessage> Declined() => () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = JsonContent("""{"outcome":"declined","processorReference":"sim_ref_test"}""")
    };

    public static Func<HttpResponseMessage> Timeout() => () => new HttpResponseMessage(HttpStatusCode.GatewayTimeout);

    public static Func<HttpResponseMessage> Error() => () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

    private static HttpContent JsonContent(string json) =>
        new StringContent(json, System.Text.Encoding.UTF8, "application/json");
}
