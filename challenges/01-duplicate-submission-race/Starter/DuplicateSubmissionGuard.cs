using System.Collections.Concurrent;

namespace Starter;

/// <summary>
/// Should guarantee that createNew runs at most once per key, and every caller with
/// that key gets the same result back - even racing concurrent callers.
/// </summary>
public sealed class DuplicateSubmissionGuard
{
    private readonly ConcurrentDictionary<string, string> _seen = new();

    public string Submit(string key, Func<string> createNew)
    {
        if (_seen.TryGetValue(key, out string? existing))
        {
            return existing;
        }

        // Bug: nothing stops another caller from also reaching this line, for the same
        // key, before either of us has stored anything below.
        string created = createNew();
        _seen[key] = created;
        return created;
    }
}
