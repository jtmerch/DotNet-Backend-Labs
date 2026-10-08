using Starter;

namespace Starter.Tests;

public sealed class ClaimStoreTests
{
    [Fact]
    public void ClaimNext_ItemClaimedMomentsAgo_IsNotReclaimedEvenIfCreatedLongAgo()
    {
        var store = new ClaimStore();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Created an hour ago (older than the staleness threshold) but still Pending -
        // claiming it for the first time right now should succeed.
        store.Add(new Item { Id = "item-1", CreatedAt = now.AddHours(-1) });

        Item? firstClaim = store.ClaimNext(TimeSpan.FromSeconds(30), now);
        Assert.NotNull(firstClaim);
        Assert.Equal("item-1", firstClaim!.Id);

        // Immediately try to claim again, at the same instant. It was *just* claimed,
        // so nothing should be eligible - regardless of how old CreatedAt is.
        Item? secondClaim = store.ClaimNext(TimeSpan.FromSeconds(30), now);
        Assert.Null(secondClaim);
    }

    [Fact]
    public void ClaimNext_ItemClaimedLongerThanThreshold_IsReclaimed()
    {
        var store = new ClaimStore();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        store.Add(new Item { Id = "item-1", CreatedAt = now });
        store.ClaimNext(TimeSpan.FromSeconds(30), now);

        Item? reclaimed = store.ClaimNext(TimeSpan.FromSeconds(30), now.AddSeconds(31));

        Assert.NotNull(reclaimed);
        Assert.Equal("item-1", reclaimed!.Id);
    }

    [Fact]
    public void ClaimNext_NoItems_ReturnsNull()
    {
        var store = new ClaimStore();

        Assert.Null(store.ClaimNext(TimeSpan.FromSeconds(30), DateTimeOffset.UtcNow));
    }
}
