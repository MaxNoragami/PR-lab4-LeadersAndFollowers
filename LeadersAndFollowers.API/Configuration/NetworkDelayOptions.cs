namespace LeadersAndFollowers.API.Configuration;

/// <summary>
/// Options for simulated network delay on leader side.
/// </summary>
public sealed record NetworkDelayOptions(int MinDelayMs, int MaxDelayMs);
