using LeadersAndFollowers.Core.Abstractions;

namespace LeadersAndFollowers.Core.Implementation;

/// <summary>
/// Leader write service using pure async/await.
/// No manual threads, no Task.Run.
/// </summary>
public sealed class LeaderWriteService : ILeaderWriteService
{
    private readonly IKeyValueStore _store;
    private readonly IFollowerTopology _topology;
    private readonly IReplicationClient _replicationClient;
    private readonly ReplicationOptions _options;
    private readonly ISystemClock _clock;

    public LeaderWriteService(
        IKeyValueStore store,
        IFollowerTopology topology,
        IReplicationClient replicationClient,
        ReplicationOptions options,
        ISystemClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _replicationClient = replicationClient ?? throw new ArgumentNullException(nameof(replicationClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<WriteResult> WriteAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (value is null) throw new ArgumentNullException(nameof(value));

        cancellationToken.ThrowIfCancellationRequested();

        // 1. Write locally on leader
        await _store.SetAsync(key, value, cancellationToken).ConfigureAwait(false);

        // 2. Get followers
        var followers = _topology.GetFollowers() ?? Array.Empty<FollowerDescriptor>();

        if (_options.WriteQuorum > followers.Count)
        {
            throw new InvalidOperationException(
                $"Write quorum {_options.WriteQuorum} is greater than number of followers {followers.Count}.");
        }

        // Edge case: no quorum or no followers → succeed immediately
        if (_options.WriteQuorum == 0 || followers.Count == 0)
        {
            return new WriteResult(
                IsSuccess: true,
                RequiredQuorum: _options.WriteQuorum,
                SuccessfulFollowers: 0,
                Responses: Array.Empty<ReplicationResponse>(),
                WasCancelled: false);
        }

        var command = new ReplicationCommand(
            Key: key,
            Value: value,
            TimestampUtc: _clock.UtcNow);

        // 3. Start replication to all followers concurrently
        var pendingTasks = followers
            .Select(f => ReplicateToFollowerAsync(f, command, cancellationToken))
            .ToList();

        var responses = new List<ReplicationResponse>(pendingTasks.Count);
        var successCount = 0;

        // 4. Wait until we hit quorum or run out of tasks
        while (pendingTasks.Count > 0 && successCount < _options.WriteQuorum)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var completed = await Task.WhenAny(pendingTasks).ConfigureAwait(false);
            pendingTasks.Remove(completed);

            var response = await completed.ConfigureAwait(false);
            responses.Add(response);

            if (response.Status == ReplicationStatus.Success)
            {
                successCount++;
            }
        }

        // At this point:
        // - pendingTasks are still running; we just don't wait for them.
        // - They will complete in the background as normal async tasks.
        //   (No Task.Run, no extra threads.)

        var isSuccess = successCount >= _options.WriteQuorum;

        return new WriteResult(
            IsSuccess: isSuccess,
            RequiredQuorum: _options.WriteQuorum,
            SuccessfulFollowers: successCount,
            Responses: responses,
            WasCancelled: cancellationToken.IsCancellationRequested);
    }

    private async Task<ReplicationResponse> ReplicateToFollowerAsync(
        FollowerDescriptor follower,
        ReplicationCommand command,
        CancellationToken outerCancellationToken)
    {
        try
        {
            CancellationToken ct = outerCancellationToken;

            if (_options.PerFollowerTimeout is { } timeout && timeout > TimeSpan.Zero)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerCancellationToken);
                cts.CancelAfter(timeout);
                ct = cts.Token;
            }

            return await _replicationClient
                .SendReplicationAsync(follower, command, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!outerCancellationToken.IsCancellationRequested)
        {
            // Cancelled due to per-follower timeout (not global cancellation)
            return ReplicationResponse.Timeout(follower);
        }
        catch (Exception ex)
        {
            return ReplicationResponse.Failure(follower, ex.Message);
        }
    }
}
