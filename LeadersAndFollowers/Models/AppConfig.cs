namespace LeadersAndFollowers.Models;

public class AppConfig
{
    public NodeRole NodeRole { get; init; }
    public int WriteQuorum { get; init; }
    public int MinDelayMs { get; init; }
    public int MaxDelayMs { get; init; }
    public bool UseVersioning { get; init; }
    public List<string> Followers { get; init; } = [];

    public static AppConfig FromConfiguration(IConfiguration config)
    {
        var nodeRoleString = config["NODE_ROLE"] ?? "Leader";
        var nodeRole = Enum.TryParse<NodeRole>(nodeRoleString, true, out var role) 
            ? role : NodeRole.Leader;

        var followersEnv = config["FOLLOWERS"] ?? string.Empty;
        var followers = followersEnv
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new AppConfig
        {
            NodeRole = nodeRole,
            WriteQuorum = int.TryParse(config["WRITE_QUORUM"], out var wq) ? wq : 1,
            MinDelayMs = int.TryParse(config["MIN_DELAY_MS"], out var min) ? min : 0,
            MaxDelayMs = int.TryParse(config["MAX_DELAY_MS"], out var max) ? max : 1000,
            UseVersioning = config["USE_VERSIONING"]?.ToLower() != "false",
            Followers = followers
        };
    }
}
