namespace LeadersAndFollowers.Core.Abstractions;

/// <summary>
/// Snapshot of followers known to the leader.
/// </summary>
public interface IFollowerTopology
{
    IReadOnlyList<FollowerDescriptor> GetFollowers();
}
