using LeadersAndFollowers.Models;

namespace LeadersAndFollowers.Services;

public class LeaderWriteService(
    KeyValueStore store,
    ReplicationClient replicationClient,
    List<string> followers,
    int writeQuorum)
{
    private readonly KeyValueStore _store = store;
    private readonly ReplicationClient _replicationClient = replicationClient;
    private readonly List<string> _followers = followers;

    public int WriteQuorum { 
        get; 
        set => 
            field = (value < 1) ?
                throw new ArgumentException("write_quorum must be >= 1") :
            (value > _followers.Count) ?
                throw new ArgumentException($"write_quorum must be <= {_followers.Count}") :
            value;
    } = writeQuorum;


    public async Task<WriteResult> WriteAsync(string key, string value)
    {
        var version = _store.IncrementVersion();
        _store.Set(key, value, version);

        if (WriteQuorum == 0 || _followers.Count == 0)
            return new WriteResult(Success: true, Quorum: WriteQuorum, Acks: 0);

        var command = new ReplicationCommand(key, value, version);

        var replicationTasks = _followers
            .Select(f => _replicationClient.SendReplicationAsync(f, command))
            .ToList();

        var successCount = 0;

        while (replicationTasks.Count > 0 && successCount < WriteQuorum)
        {
            var completed = await Task.WhenAny(replicationTasks);
            replicationTasks.Remove(completed);

            var success = await completed;
            if (success)
                successCount++;
        }

        var isSuccess = successCount >= WriteQuorum;
        return new WriteResult(Success: isSuccess, Quorum: WriteQuorum, Acks: successCount);
    }
}
