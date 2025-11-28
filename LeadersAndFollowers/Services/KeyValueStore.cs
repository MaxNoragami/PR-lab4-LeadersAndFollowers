using System.Collections.Concurrent;

namespace LeadersAndFollowers.Services;

public class KeyValueStore
{
    private readonly ConcurrentDictionary<string, (string Value, long Version)> _store = new();
    private long _versionCounter = 0;

    public void Set(string key, string value, long version)
    {
        _store.AddOrUpdate(key, 
            _ => (value, version),
            (_, current) => version > current.Version ? (value, version) : current);
    }

    public string? Get(string key)
    {
        return _store.TryGetValue(key, out var entry) ? entry.Value : null;
    }

    public Dictionary<string, string> GetAll()
    {
        return _store.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value);
    }

    public long IncrementVersion()
    {
        return Interlocked.Increment(ref _versionCounter);
    }
}
