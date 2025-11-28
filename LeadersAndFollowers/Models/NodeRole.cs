namespace LeadersAndFollowers.Models;

public enum NodeRole
{
    Leader,
    Follower
}

public record ReplicationCommand(string Key, string Value, long Version);

public record WriteResult(bool IsSuccess, int RequiredQuorum, int SuccessfulFollowers);
