namespace LeadersAndFollowers.Core.Abstractions;

/// <summary>
/// Lightweight description of a follower.
/// The API layer decides how this maps to host/port/URL.
/// </summary>
public sealed record FollowerDescriptor(string Id);
