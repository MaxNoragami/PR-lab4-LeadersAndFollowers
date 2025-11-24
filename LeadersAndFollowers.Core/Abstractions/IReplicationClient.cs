namespace LeadersAndFollowers.Core.Abstractions;

/// <summary>
/// Abstraction over "send this replication command to that follower".
/// API layer implements this using HTTP, etc.
/// </summary>
public interface IReplicationClient
{
    Task<ReplicationResponse> SendReplicationAsync(
        FollowerDescriptor follower,
        ReplicationCommand command,
        CancellationToken cancellationToken = default);
}
