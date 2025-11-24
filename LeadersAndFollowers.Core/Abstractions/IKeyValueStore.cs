namespace LeadersAndFollowers.Core.Abstractions;

/// <summary>
/// Simple async key–value store abstraction.
/// Implemented in-memory for this lab, but easily swappable.
/// </summary>
public interface IKeyValueStore
{
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> DumpAsync(CancellationToken cancellationToken = default);
}
