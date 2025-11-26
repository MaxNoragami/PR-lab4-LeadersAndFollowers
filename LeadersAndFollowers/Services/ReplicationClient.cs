using LeadersAndFollowers.Models;

namespace LeadersAndFollowers.Services;

public class ReplicationClient
{
    private readonly HttpClient _httpClient;
    private readonly Random _random = new();

    public int MinDelayMs { get; set; }
    public int MaxDelayMs { get; set; }

    public ReplicationClient(HttpClient httpClient, int minDelayMs, int maxDelayMs)
    {
        _httpClient = httpClient;
        MinDelayMs = minDelayMs;
        MaxDelayMs = maxDelayMs;
    }

    public async Task<bool> SendReplicationAsync(string followerUrl, ReplicationCommand command)
    {
        try
        {
            // Simulate network lag
            if (MaxDelayMs > 0)
            {
                var delay = _random.Next(MinDelayMs, MaxDelayMs + 1);
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
