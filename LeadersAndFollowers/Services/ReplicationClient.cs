using LeadersAndFollowers.Models;

namespace LeadersAndFollowers.Services;

public class ReplicationClient
{
    private readonly HttpClient _httpClient;
    private readonly int _minDelayMs;
    private readonly int _maxDelayMs;
    private readonly Random _random = new();

    public ReplicationClient(HttpClient httpClient, int minDelayMs, int maxDelayMs)
    {
        _httpClient = httpClient;
        _minDelayMs = minDelayMs;
        _maxDelayMs = maxDelayMs;
    }

    public async Task<bool> SendReplicationAsync(string followerUrl, ReplicationCommand command)
    {
        try
        {
            // Simulate network lag
            if (_maxDelayMs > 0)
            {
                var delay = _random.Next(_minDelayMs, _maxDelayMs + 1);
                if (delay > 0)
                    await Task.Delay(delay);
            }

            var url = followerUrl.TrimEnd('/') + "/replicate";
            var response = await _httpClient.PostAsJsonAsync(url, command);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
