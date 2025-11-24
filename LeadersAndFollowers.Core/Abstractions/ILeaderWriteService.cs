namespace LeadersAndFollowers.Core.Abstractions;

/// <summary>
/// This is the main leader-side service:
/// - Writes to local store
/// - Replicates to followers
/// - Waits until quorum or exhaustion
/// </summary>
public interface ILeaderWriteService
{
    Task<WriteResult> WriteAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);
}
