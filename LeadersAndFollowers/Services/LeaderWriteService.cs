using LeadersAndFollowers.Models;

namespace LeadersAndFollowers.Services;

public class LeaderWriteService
{
    private readonly KeyValueStore _store;
    private readonly ReplicationClient _replicationClient;
    private readonly List<string> _followers;
    private readonly int _writeQuorum;

    public LeaderWriteService(
        KeyValueStore store,
        ReplicationClient replicationClient,
        List<string> followers,
        int writeQuorum)
    {
        _store = store;
        _replicationClient = replicationClient;
        _followers = followers;
        _writeQuorum = writeQuorum;
    }

    public async Task<WriteResult> WriteAsync(string key, string value)
    {
        // Generate version and write locally first
        var version = _store.IncrementVersion();
        _store.Set(key, value, version);

        // If no quorum needed or no followers, succeed immediately
        if (_writeQuorum == 0 || _followers.Count == 0)
        {
            return new WriteResult(true, _writeQuorum, 0);
        }

        var command = new ReplicationCommand(key, value, version);

        // Start replication to all followers concurrently
        var replicationTasks = _followers
            .Select(f => _replicationClient.SendReplicationAsync(f, command))
            .ToList();

        var successCount = 0;

        // Wait until we hit quorum or run out of tasks
        while (replicationTasks.Count > 0 && successCount < _writeQuorum)
        {
            var completed = await Task.WhenAny(replicationTasks);
            replicationTasks.Remove(completed);

            var success = await completed;
            if (success)
            {
                successCount++;
            }
        }

        var isSuccess = successCount >= _writeQuorum;
        return new WriteResult(isSuccess, _writeQuorum, successCount);
    }
}
