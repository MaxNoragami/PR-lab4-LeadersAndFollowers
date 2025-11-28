namespace LeadersAndFollowers.Models;

public record ReplicationCommand(string Key, string Value, long Version);
