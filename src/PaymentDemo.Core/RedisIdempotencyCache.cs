using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace PaymentDemo.Core;

public sealed class RedisIdempotencyCache : IIdempotencyCache
{
    private readonly IConnectionMultiplexer _connection;
    private readonly TimeSpan _ttl;
    private readonly ILogger<RedisIdempotencyCache> _logger;

    public RedisIdempotencyCache(IConnectionMultiplexer connection, TimeSpan ttl, ILogger<RedisIdempotencyCache> logger)
    {
        _connection = connection;
        _ttl = ttl;
        _logger = logger;
    }

    public async Task<string?> TryGetTransactionIdAsync(string demoAccountId, string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            IDatabase database = _connection.GetDatabase();
            RedisValue value = await database.StringGetAsync(BuildKey(demoAccountId, idempotencyKey));
            return value.IsNullOrEmpty ? null : value.ToString();
        }
        catch (RedisException ex)
        {
            // A cache miss is harmless - the caller falls through to SQLite - so a
            // Redis outage degrades performance, not correctness.
            _logger.LogWarning(ex, "Idempotency cache read failed; falling through to the database.");
            return null;
        }
    }

    public async Task SetAsync(string demoAccountId, string idempotencyKey, string transactionId, CancellationToken cancellationToken)
    {
        try
        {
            IDatabase database = _connection.GetDatabase();
            await database.StringSetAsync(BuildKey(demoAccountId, idempotencyKey), transactionId, _ttl);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Idempotency cache write failed; the transaction is still safely recorded in SQLite.");
        }
    }

    private static string BuildKey(string demoAccountId, string idempotencyKey) =>
        $"idempotency:{demoAccountId}:{idempotencyKey}";
}
