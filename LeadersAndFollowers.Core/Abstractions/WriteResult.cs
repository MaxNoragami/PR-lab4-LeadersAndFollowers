namespace LeadersAndFollowers.Core.Abstractions;

/// <summary>
/// Result of a leader write (including replication outcome).
/// </summary>
public sealed record WriteResult(
    bool IsSuccess,
    int RequiredQuorum,
    int SuccessfulFollowers,
    IReadOnlyList<ReplicationResponse> Responses,
    bool WasCancelled);
