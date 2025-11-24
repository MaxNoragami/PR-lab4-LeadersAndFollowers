namespace LeadersAndFollowers.Core.Abstractions;

/// <summary>
/// Options for semi-synchronous replication on the leader.
/// </summary>
public sealed class ReplicationOptions
{
    /// <summary>
    /// Number of follower confirmations required (excluding the leader itself).
    /// </summary>
    public int WriteQuorum { get; init; }

    /// <summary>
    /// Optional per-follower timeout. If null or <= 0, no extra timeout is applied.
    /// </summary>
    public TimeSpan? PerFollowerTimeout { get; init; }

    public ReplicationOptions(int writeQuorum, TimeSpan? perFollowerTimeout = null)
    {
        if (writeQuorum < 0)
            throw new ArgumentOutOfRangeException(nameof(writeQuorum), "Write quorum must be >= 0.");

        WriteQuorum = writeQuorum;
        PerFollowerTimeout = perFollowerTimeout;
    }
}
