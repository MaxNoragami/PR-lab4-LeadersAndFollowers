using System.Collections.Concurrent;
using LeadersAndFollowers.Core.Abstractions;

namespace LeadersAndFollowers.Core.Implementation;

/// <summary>
/// Thread-safe in-memory key–value store.
/// Used by both leader and followers.
/// </summary>
public sealed class InMemoryKeyValueStore : IKeyValueStore
{
    private readonly ConcurrentDictionary<string, string> _store = new();

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (value is null) throw new ArgumentNullException(nameof(value));

        cancellationToken.ThrowIfCancellationRequested();

        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        cancellationToken.ThrowIfCancellationRequested();

        _store.TryGetValue(key, out var value);
        return Task.FromResult<string?>(value);
    }

    public Task<IReadOnlyDictionary<string, string>> DumpAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Snapshot
        var copy = new Dictionary<string, string>(_store);
        return Task.FromResult((IReadOnlyDictionary<string, string>)copy);
    }
}
