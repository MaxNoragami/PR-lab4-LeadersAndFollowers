namespace LeadersAndFollowers.Models;

public enum NodeRole
{
    Leader,
    Follower
}

public record ReplicationCommand(string Key, string Value, long Version);

public record WriteResult(bool Success, int Quorum, int Acks);
