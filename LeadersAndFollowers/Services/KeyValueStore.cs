using System.Collections.Concurrent;

namespace LeadersAndFollowers.Services;

public class KeyValueStore
{
    private readonly ConcurrentDictionary<string, (string Value, long Version)> _store = new();
    
    public bool UseVersioning { get; set; } = true;

    public void Set(string key, string value, long version)
    {
        if (UseVersioning)
        {
            _store.AddOrUpdate(key, 
                _ => (value, version),
                (_, current) => version > current.Version ? (value, version) : current);
        }
        else
        {
            _store[key] = (value, version);
        }
    }

    public string? Get(string key)
    {
        return _store.TryGetValue(key, out var entry) ? entry.Value : null;
    }

    public Dictionary<string, string> GetAll()
    {
        return _store.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value);
    }

    public Dictionary<string, long> GetAllVersions()
    {
        return _store.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Version);
    }

    public long IncrementVersion()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
