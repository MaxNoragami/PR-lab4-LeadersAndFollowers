using LeadersAndFollowers.Core.Abstractions;

namespace LeadersAndFollowers.API.Services;

/// <summary>
/// Simple implementation of IFollowerTopology that just returns a fixed list
/// of followers based on env config.
/// The FollowerDescriptor.Id is the follower's base URL (e.g. "http://follower1:5000").
/// </summary>
public sealed class StaticFollowerTopology : IFollowerTopology
{
    private readonly IReadOnlyList<FollowerDescriptor> _followers;

    public StaticFollowerTopology(IReadOnlyList<FollowerDescriptor> followers)
    {
        _followers = followers ?? Array.Empty<FollowerDescriptor>();
    }

    public IReadOnlyList<FollowerDescriptor> GetFollowers() => _followers;
}
