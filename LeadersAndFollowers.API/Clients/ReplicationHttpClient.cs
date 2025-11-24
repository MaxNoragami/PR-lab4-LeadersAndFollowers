using LeadersAndFollowers.API.Configuration;
using LeadersAndFollowers.Core.Abstractions;

namespace LeadersAndFollowers.API.Clients;

/// <summary>
/// HTTP-based implementation of IReplicationClient.
/// It:
///  - waits a random delay in [MinDelayMs, MaxDelayMs]
///  - sends POST /replicate with JSON body to each follower
/// </summary>
public sealed class ReplicationHttpClient : IReplicationClient
{
    private readonly HttpClient _httpClient;
    private readonly NetworkDelayOptions _delayOptions;
    private readonly ISystemClock _clock;
    private readonly Random _random = new();

    public ReplicationHttpClient(
        HttpClient httpClient,
        NetworkDelayOptions delayOptions,
        ISystemClock clock)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _delayOptions = delayOptions ?? throw new ArgumentNullException(nameof(delayOptions));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ReplicationResponse> SendReplicationAsync(
        FollowerDescriptor follower,
        ReplicationCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Simulate network lag on leader side
            var min = Math.Max(0, _delayOptions.MinDelayMs);
            var max = Math.Max(min, _delayOptions.MaxDelayMs);
            if (max > 0)
            {
                var delay = _random.Next(min, max + 1);
                if (delay > 0)
                    await Task.Delay(delay, cancellationToken);
            }

            var url = follower.Id.TrimEnd('/') + "/replicate";

            var enrichedCommand = command with
            {
                // ensure timestamp is set if caller passed default
                TimestampUtc = command.TimestampUtc == default
                    ? _clock.UtcNow
                    : command.TimestampUtc
            };

            using var response = await _httpClient.PostAsJsonAsync(
                url,
                enrichedCommand,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ReplicationResponse.Success(follower);
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            return ReplicationResponse.Failure(follower, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // per-call timeout, if you ever wrap with a timeout token
            return ReplicationResponse.Timeout(follower);
        }
        catch (Exception ex)
        {
            return ReplicationResponse.Failure(follower, ex.Message);
        }
    }
}
