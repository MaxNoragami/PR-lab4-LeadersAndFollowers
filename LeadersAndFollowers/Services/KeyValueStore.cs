using System.Collections.Concurrent;

namespace LeadersAndFollowers.Services;

public class KeyValueStore(bool useVersioning = true)
{
    private readonly ConcurrentDictionary<string, (string Value, long Version)> _store = new();
    
    public bool UseVersioning => useVersioning;


    public void Set(string key, string value, long version)
    {
        if (UseVersioning)
            _store.AddOrUpdate(key, 
                _ => (value, version),
                (_, current) => version > current.Version ? (value, version) : current);
        else
            _store[key] = (value, version);
    }

    public string? Get(string key)
        => _store.TryGetValue(key, out var entry) ? entry.Value : null;

    public Dictionary<string, string> GetAll()
        => _store.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value);

    public Dictionary<string, long> GetAllVersions()
        => _store.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Version);

    public long IncrementVersion()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
