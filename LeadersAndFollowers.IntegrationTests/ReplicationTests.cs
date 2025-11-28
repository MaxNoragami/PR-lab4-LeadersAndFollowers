using System.Net.Http.Json;
using LeadersAndFollowers.Models;

namespace LeadersAndFollowers.IntegrationTests;


public class ReplicationTests
{
    private const string LeaderUrl = "http://localhost:8080";
    private static readonly string[] FollowerUrls = 
    {
        "http://localhost:8081",
        "http://localhost:8082",
        "http://localhost:8083",
        "http://localhost:8084",
        "http://localhost:8085"
    };

    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };

    [Fact]
    public async Task GivenLeader_WhenWritingAndReading_ThenReturnsCorrectValue()
    {
        // Arrange
        var key = $"test-key-{Guid.NewGuid()}";
        var value = "hello-world";

        // Act - Write to & Read from leader
        var writeResponse = await _client.PostAsync(
            $"{LeaderUrl}/set?key={key}&value={value}", null);
        
        var readResponse = await _client.GetAsync($"{LeaderUrl}/get/{key}");

        // Assert - Write succeeded
        Assert.True(writeResponse.IsSuccessStatusCode);
        var writeResult = await writeResponse.Content.ReadFromJsonAsync<WriteResult>();
        Assert.True(writeResult?.Success);

        // Assert - Read returns correct value
        Assert.True(readResponse.IsSuccessStatusCode);
        var readValue = await readResponse.Content.ReadAsStringAsync();
        Assert.Contains(value, readValue);
    }

    [Fact]
    public async Task GivenLeader_WhenWritingData_ThenFollowersReceiveReplicatedData()
    {
        // Arrange
        var key = $"replicated-key-{Guid.NewGuid()}";
        var value = "replicated-value";

        // Act - Write to leader
        var writeResponse = await _client.PostAsync(
            $"{LeaderUrl}/set?key={key}&value={value}", null);
        Assert.True(writeResponse.IsSuccessStatusCode);

        // Wait for replication to complete
        await Task.Delay(2000);

        // Assert - All followers have the data
        foreach (var followerUrl in FollowerUrls)
        {
            var readResponse = await _client.GetAsync($"{followerUrl}/get/{key}");
            Assert.True(readResponse.IsSuccessStatusCode, 
                $"Follower {followerUrl} should have the key");
            
            var readValue = await readResponse.Content.ReadAsStringAsync();
            Assert.Contains(value, readValue);
        }
    }

    [Fact]
    public async Task GivenFollower_WhenAttemptingWrite_ThenRequestIsRejected()
    {
        // Arrange
        var key = $"follower-write-{Guid.NewGuid()}";
        var value = "should-fail";

        // Act & Assert - All followers should reject writes
        foreach (var followerUrl in FollowerUrls)
        {
            var response = await _client.PostAsync(
                $"{followerUrl}/set?key={key}&value={value}", null);
            
            // Followers don't have /set endpoint, should return 404
            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task GivenLeader_WhenReceivingConcurrentWrites_ThenAllWritesSucceed()
    {
        // Arrange
        var keys = Enumerable.Range(0, 10)
            .Select(i => $"concurrent-key-{Guid.NewGuid()}")
            .ToList();

        // Act - Write all keys concurrently
        var writeTasks = keys.Select(async key =>
        {
            var response = await _client.PostAsync(
                $"{LeaderUrl}/set?key={key}&value=value-{key}", null);
            return response.IsSuccessStatusCode;
        });

        var results = await Task.WhenAll(writeTasks);

        // Assert - All writes succeeded
        Assert.All(results, success => Assert.True(success));
    }

    [Fact]
    public async Task GivenLeader_WhenQuorumConfigIsChanged_ThenWritesRespectNewQuorum()
    {
        // Arrange - Set quorum to 3
        var configResponse = await _client.PostAsJsonAsync(
            $"{LeaderUrl}/config",
            new { WriteQuorum = 3 });
        Assert.True(configResponse.IsSuccessStatusCode);

        // Act - Write should still succeed
        var key = $"quorum-test-{Guid.NewGuid()}";
        var writeResponse = await _client.PostAsync(
            $"{LeaderUrl}/set?key={key}&value=test", null);

        // Assert
        Assert.True(writeResponse.IsSuccessStatusCode);
        var result = await writeResponse.Content.ReadFromJsonAsync<WriteResult>();
        Assert.True(result?.Success);
        Assert.Equal(3, result?.Quorum);
    }

    [Fact]
    public async Task GivenCluster_WhenCheckingHealth_ThenAllNodesAreHealthy()
    {
        var leaderHealth = await _client.GetAsync($"{LeaderUrl}/health");
        Assert.True(leaderHealth.IsSuccessStatusCode);

        foreach (var followerUrl in FollowerUrls)
        {
            var followerHealth = await _client.GetAsync($"{followerUrl}/health");
            Assert.True(followerHealth.IsSuccessStatusCode, 
                $"Follower {followerUrl} should be healthy");
        }
    }

    [Fact]
    public async Task GivenSameKey_WhenWrittenMultipleTimes_ThenFinalValueIsConsistentAcrossCluster()
    {
        // Arrange
        var key = $"overwrite-key-{Guid.NewGuid()}";
        var finalValue = "final-value";

        // Act - Write multiple times to same key
        for (int i = 0; i < 5; i++)
        {
            await _client.PostAsync(
                $"{LeaderUrl}/set?key={key}&value=value-{i}", null);
            await Task.Delay(100); // Small delay between writes
        }

        // Write final value
        await _client.PostAsync(
            $"{LeaderUrl}/set?key={key}&value={finalValue}", null);

        // Wait for replication
        await Task.Delay(2000);

        // Assert - Leader and all followers have the final value
        var leaderValue = await _client.GetStringAsync($"{LeaderUrl}/get/{key}");
        Assert.Contains(finalValue, leaderValue);

        foreach (var followerUrl in FollowerUrls)
        {
            var followerValue = await _client.GetStringAsync($"{followerUrl}/get/{key}");
            Assert.Contains(finalValue, followerValue);
        }
    }
}
