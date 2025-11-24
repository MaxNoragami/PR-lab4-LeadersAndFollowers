namespace LeadersAndFollowers.Core.Abstractions;

/// <summary>
/// Logical replication command from leader to follower.
/// Transport-independent.
/// </summary>
public sealed record ReplicationCommand(
    string Key,
    string Value,
    DateTimeOffset TimestampUtc);
