using Microsoft.Extensions.Logging.Abstractions;
using PaymentDemo.Core;
using StackExchange.Redis;

namespace PaymentDemo.Core.Tests;

/// <summary>
/// Exercises the real Redis client against a real local Redis - requires
/// `docker compose up -d redis` from the repo root first. These tests detect whether
/// Redis is reachable and no-op (rather than fail) when it isn't, so the default
/// `dotnet test` run never depends on Docker being up. Run them deliberately with
/// Redis started when you want this specific coverage.
/// </summary>
public sealed class RedisIdempotencyCacheTests : IDisposable
{
    private readonly IConnectionMultiplexer? _connection;

    public RedisIdempotencyCacheTests()
    {
        try
        {
            var options = ConfigurationOptions.Parse("localhost:6379");
            options.ConnectTimeout = 500;
            options.AbortOnConnectFail = true;
            _connection = ConnectionMultiplexer.Connect(options);
        }
        catch (RedisConnectionException)
        {
            _connection = null;
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }

    [Fact]
    public async Task SetAsync_ThenTryGetTransactionIdAsync_ReturnsWhatWasStored()
    {
        if (_connection is null)
        {
            return; // Redis not running locally - see class remarks.
        }

        var cache = new RedisIdempotencyCache(_connection, TimeSpan.FromMinutes(1), NullLogger<RedisIdempotencyCache>.Instance);
        string demoAccountId = $"acct_test_{Guid.NewGuid():N}";

        await cache.SetAsync(demoAccountId, "key_1", "txn_abc", CancellationToken.None);
        string? found = await cache.TryGetTransactionIdAsync(demoAccountId, "key_1", CancellationToken.None);

        Assert.Equal("txn_abc", found);
    }

    [Fact]
    public async Task TryGetTransactionIdAsync_UnknownKey_ReturnsNull()
    {
        if (_connection is null)
        {
            return;
        }

        var cache = new RedisIdempotencyCache(_connection, TimeSpan.FromMinutes(1), NullLogger<RedisIdempotencyCache>.Instance);

        string? found = await cache.TryGetTransactionIdAsync($"acct_test_{Guid.NewGuid():N}", "unknown_key", CancellationToken.None);

        Assert.Null(found);
    }
}
