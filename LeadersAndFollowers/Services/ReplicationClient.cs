using LeadersAndFollowers.Models;

namespace LeadersAndFollowers.Services;

public class ReplicationClient(
    HttpClient httpClient, 
    int minDelayMs, 
    int maxDelayMs)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly Random _random = new();

    public int MinDelayMs { 
        get; 
        set => 
            field = (value < 0) ?
                throw new ArgumentException("min_delay_ms must be >= 0") :
            value;
    } = minDelayMs;
    
    public int MaxDelayMs { 
        get; 
        set => 
            field = (value < 0) ?
                throw new ArgumentException("max_delay_ms must be >= 0") :
            value;
    } = maxDelayMs;


    public async Task<bool> SendReplicationAsync(string followerUrl, ReplicationCommand command)
    {
        try
        {
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
