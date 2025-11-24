namespace LeadersAndFollowers.Core.Abstractions;

public enum ReplicationStatus
{
    Success,
    Failed,
    Timeout
}

/// <summary>
/// Result of sending a command to a single follower.
/// </summary>
public sealed record ReplicationResponse(
    FollowerDescriptor Follower,
    ReplicationStatus Status,
    string? Error = null)
{
    public static ReplicationResponse Success(FollowerDescriptor follower) =>
        new(follower, ReplicationStatus.Success);

    public static ReplicationResponse Timeout(FollowerDescriptor follower) =>
        new(follower, ReplicationStatus.Timeout, "Operation timed out.");

    public static ReplicationResponse Failure(FollowerDescriptor follower, string? error) =>
        new(follower, ReplicationStatus.Failed, error);
}
