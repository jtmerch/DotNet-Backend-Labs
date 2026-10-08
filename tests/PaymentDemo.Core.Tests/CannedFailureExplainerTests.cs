using PaymentDemo.Core;

namespace PaymentDemo.Core.Tests;

public sealed class CannedFailureExplainerTests
{
    [Fact]
    public async Task ExplainAsync_ReturnsDeterministicNonEmptyExplanation()
    {
        var explainer = new CannedFailureExplainer();
        var transaction = new Transaction
        {
            TransactionId = "txn_1",
            IdempotencyKey = "key_1",
            DemoAccountId = "acct_1",
            AmountMinorUnits = 500,
            Currency = "USD",
            RequestedOutcome = "timeout",
            Attempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        string first = await explainer.ExplainAsync(transaction, CancellationToken.None);
        string second = await explainer.ExplainAsync(transaction, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.Contains("txn_1", first);
        Assert.Contains("3 attempt", first);
        Assert.Equal(first, second);
    }
}
