using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PaymentDemo.Core;
using TransactionWorker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));

builder.Services.AddSingleton<ITransactionRepository>(_ =>
    new SqliteTransactionRepository(SqliteDatabasePathResolver.ResolveDefaultDatabasePath()));

string otlpEndpoint = builder.Configuration["Otel:OtlpEndpoint"] ?? "http://localhost:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("transaction-worker"))
    .WithTracing(tracing => tracing
        .AddHttpClientInstrumentation()
        .AddSource(PaymentDemoTelemetry.SourceName)
        .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint)));

builder.Services.AddSingleton<IWorkerDelay, SystemWorkerDelay>();

builder.Services.AddHttpClient<ProcessorSimulatorClient>(client =>
{
    string baseUrl = builder.Configuration["Simulator:BaseUrl"] ?? "http://localhost:5099";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

// AI-assisted failure explanations are entirely optional. The key is read from the
// environment only - never from appsettings.json or any committed file - and if it's
// absent, a deterministic canned explainer is used instead. Nothing here ever makes
// the worker's core correctness depend on this being configured.
string? anthropicApiKey = builder.Configuration["ANTHROPIC_API_KEY"];
if (!string.IsNullOrWhiteSpace(anthropicApiKey))
{
    string model = builder.Configuration["Ai:Model"] ?? "claude-3-5-haiku-latest";
    builder.Services.AddSingleton<IFailureExplainer>(
        _ => new ClaudeFailureExplainer(ClaudeFailureExplainer.BuildHttpClient(anthropicApiKey), model));
}
else
{
    builder.Services.AddSingleton<IFailureExplainer, CannedFailureExplainer>();
}

builder.Services.AddHostedService<TransactionProcessingWorker>();

var host = builder.Build();
host.Run();
