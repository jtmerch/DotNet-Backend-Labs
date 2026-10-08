using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PaymentApi;
using PaymentDemo.Core;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ITransactionRepository>(_ =>
    new SqliteTransactionRepository(SqliteDatabasePathResolver.ResolveDefaultDatabasePath()));

string otlpEndpoint = builder.Configuration["Otel:OtlpEndpoint"] ?? "http://localhost:4317";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("payment-api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddSource(PaymentDemoTelemetry.SourceName)
        .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint)));

string redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
TimeSpan idempotencyCacheTtl = builder.Configuration.GetValue<TimeSpan?>("Redis:IdempotencyCacheTtl") ?? TimeSpan.FromHours(24);

// Deliberately not an eager "connect or crash at startup" dependency: Redis here is a
// speed optimization for idempotency lookups, not a source of truth, so a missing or
// unreachable Redis falls back to a no-op cache instead of taking the API down.
using ILoggerFactory startupLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
ILogger startupLogger = startupLoggerFactory.CreateLogger("RedisStartup");

try
{
    ConfigurationOptions redisOptions = ConfigurationOptions.Parse(redisConnectionString);
    redisOptions.ConnectTimeout = 500;
    redisOptions.ConnectRetry = 1;
    redisOptions.AbortOnConnectFail = true;

    IConnectionMultiplexer redisConnection = await ConnectionMultiplexer.ConnectAsync(redisOptions);
    builder.Services.AddSingleton(redisConnection);
    builder.Services.AddSingleton<IIdempotencyCache>(sp => new RedisIdempotencyCache(
        sp.GetRequiredService<IConnectionMultiplexer>(),
        idempotencyCacheTtl,
        sp.GetRequiredService<ILogger<RedisIdempotencyCache>>()));

    startupLogger.LogInformation("Connected to Redis at {ConnectionString} for idempotency caching.", redisConnectionString);
}
catch (RedisConnectionException ex)
{
    startupLogger.LogWarning(
        ex, "Could not connect to Redis at {ConnectionString}; idempotency lookups will go straight to SQLite.",
        redisConnectionString);
    builder.Services.AddSingleton<IIdempotencyCache, NullIdempotencyCache>();
}

var app = builder.Build();

app.MapPaymentEndpoints();

app.Run();

public partial class Program
{
}
