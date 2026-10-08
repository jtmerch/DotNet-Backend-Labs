namespace Starter;

public enum ItemState
{
    Pending,
    Claimed,
    Done
}

public sealed class Item
{
    public required string Id { get; init; }
    public ItemState State { get; set; } = ItemState.Pending;
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ClaimedAt { get; set; }
}

/// <summary>
/// Should claim the oldest Pending item, or a Claimed item that's been stuck longer
/// than staleThreshold - and nothing else.
/// </summary>
public sealed class ClaimStore
{
    private readonly List<Item> _items = new();
    private readonly object _lock = new();

    public void Add(Item item)
    {
        lock (_lock)
        {
            _items.Add(item);
        }
    }

    public Item? ClaimNext(TimeSpan staleThreshold, DateTimeOffset now)
    {
        lock (_lock)
        {
            // Bug: compares CreatedAt, which never changes, instead of ClaimedAt, which
            // is what actually tells you how long this item has been sitting claimed.
            Item? candidate = _items.FirstOrDefault(i =>
                i.State == ItemState.Pending ||
                (i.State == ItemState.Claimed && now - i.CreatedAt > staleThreshold));

            if (candidate is null)
            {
                return null;
            }

            candidate.State = ItemState.Claimed;
            candidate.ClaimedAt = now;
            return candidate;
        }
    }
}
