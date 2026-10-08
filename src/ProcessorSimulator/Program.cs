using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ProcessorSimulator;

var builder = WebApplication.CreateBuilder(args);

string otlpEndpoint = builder.Configuration["Otel:OtlpEndpoint"] ?? "http://localhost:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("processor-simulator"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint)));

var app = builder.Build();

app.MapSimulatorEndpoints();

app.Run();

public partial class Program
{
}
