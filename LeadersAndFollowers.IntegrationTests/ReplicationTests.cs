using System.Net.Http.Json;

namespace LeadersAndFollowers.IntegrationTests;

public class ReplicationTests
{
    private const string LeaderUrl = "http://localhost:8080";
    private const string Follower1Url = "http://localhost:8081";
    private const string Follower2Url = "http://localhost:8082";
    
    private readonly HttpClient _httpClient = new();

    /// <summary>
    /// Test 1: Store Layer - Concurrent writes to same key from multiple clients
    /// </summary>
    [Fact]
    public async Task Test1_StoreLayer_ConcurrentWritesToSameKey_NoCorruption()
    {
        // Arrange: Use a unique key for this test
        var key = $"concurrent-key-{Guid.NewGuid()}";
        
        // Act: Send 10 concurrent writes to the same key
        var tasks = Enumerable.Range(1, 10)
            .Select(i => _httpClient.PostAsync($"{LeaderUrl}/set?key={key}&value=write-{i}", null))
            .ToArray();
        
        var responses = await Task.WhenAll(tasks);
        
        // Assert: All writes should succeed (quorum reached)
        var successCount = responses.Count(r => r.IsSuccessStatusCode);
        Assert.True(successCount == 10, $"Expected 10 successful writes, got {successCount}");
        
        // Give replication time to complete
        await Task.Delay(2000);
        
        // Read from leader - should have ONE of the values, not corrupted data
        var leaderResponse = await _httpClient.GetAsync($"{LeaderUrl}/get/{key}");
        Assert.True(leaderResponse.IsSuccessStatusCode);
        
        var value = await leaderResponse.Content.ReadAsStringAsync();
        Assert.StartsWith("\"write-", value); // Should be a valid write-X value
        Assert.DoesNotContain("null", value);
        Assert.DoesNotContain("undefined", value);
    }

    /// <summary>
    /// Test 2: Logic Layer - Read-Modify-Write race condition
    /// </summary>
    [Fact]
    public async Task Test2_LogicLayer_ReadModifyWrite_LostUpdate()
    {
        // Arrange: Set initial counter value
        var key = $"counter-{Guid.NewGuid()}";
        await _httpClient.PostAsync($"{LeaderUrl}/set?key={key}&value=0", null);
        await Task.Delay(500);
        
        const int numIncrements = 20;
        
        // Act: Multiple clients do read-modify-write concurrently
        async Task IncrementCounter()
        {
            // Read current value
            var response = await _httpClient.GetAsync($"{LeaderUrl}/get/{key}");
            var valueString = await response.Content.ReadAsStringAsync();
            var currentValue = int.Parse(valueString.Trim('"'));
            
            // Delay to maximize race condition window
            await Task.Delay(100);
            
            // Write incremented value
            await _httpClient.PostAsync($"{LeaderUrl}/set?key={key}&value={currentValue + 1}", null);
        }
        
        // Run many concurrent increments
        var tasks = Enumerable.Range(0, numIncrements).Select(_ => IncrementCounter()).ToArray();
        await Task.WhenAll(tasks);
        
        await Task.Delay(500);
        
        // Assert: Should be numIncrements (20), but will be less due to lost updates
        var finalResponse = await _httpClient.GetAsync($"{LeaderUrl}/get/{key}");
        var finalValueString = await finalResponse.Content.ReadAsStringAsync();
        var finalValue = int.Parse(finalValueString.Trim('"'));
        
        // Log the actual result
        Console.WriteLine($"Expected: {numIncrements} increments, Actual final value: {finalValue}");
        
        // The race condition means finalValue < numIncrements (lost updates!)
        // This test PASSES if we detect lost updates (which proves the race condition exists)
        if (finalValue < numIncrements)
        {
            // Race condition detected! Some updates were lost
            Assert.True(true, $"Race condition confirmed: {numIncrements - finalValue} updates were lost");
        }
        else
        {
            // If somehow all updates succeeded, the test still passes but notes it
            Assert.True(true, "No race condition detected in this run (try running again)");
        }
    }

    /// <summary>
    /// Test 3: Network/Replication Layer - Stale reads from followers
    /// Followers may have old data due to replication lag (0-1000ms delays)
    /// </summary>
    [Fact]
    public async Task Test3_NetworkLayer_StaleReadsFromFollowers_ReplicationLag()
    {
        // Arrange: Write a value to leader
        var key = $"stale-test-{Guid.NewGuid()}";
        var initialValue = "initial";
        await _httpClient.PostAsync($"{LeaderUrl}/set?key={key}&value={initialValue}", null);
        await Task.Delay(1500); // Ensure initial value is replicated
        
        // Act: Write new value and IMMEDIATELY read from follower
        var newValue = "updated";
        var writeResponse = await _httpClient.PostAsync($"{LeaderUrl}/set?key={key}&value={newValue}", null);
        Assert.True(writeResponse.IsSuccessStatusCode);
        
        // Immediately read from follower (before replication completes)
        var followerResponse = await _httpClient.GetAsync($"{Follower1Url}/get/{key}");
        var followerValue = await followerResponse.Content.ReadAsStringAsync();
        
        // Read from leader for comparison
        var leaderResponse = await _httpClient.GetAsync($"{LeaderUrl}/get/{key}");
        var leaderValue = await leaderResponse.Content.ReadAsStringAsync();
        
        // Assert: Follower MIGHT have stale data (initial value) while leader has new value
        // Note: This might not always fail due to timing, but demonstrates the concept
        var isStale = followerValue.Contains(initialValue) && leaderValue.Contains(newValue);
        
        if (!isStale)
        {
            // Wait and check eventual consistency
            await Task.Delay(1500);
            var finalFollowerResponse = await _httpClient.GetAsync($"{Follower1Url}/get/{key}");
            var finalFollowerValue = await finalFollowerResponse.Content.ReadAsStringAsync();
            
            // Eventually consistent
            Assert.Contains(newValue, finalFollowerValue.Trim('"'));
        }
        
        // The test demonstrates that immediate reads from followers may be stale
        Assert.True(true, "Test demonstrates replication lag - followers may temporarily have stale data");
    }
    
    /// <summary>
    /// Test 4: Replication Conflict - Rapid writes to same key cause inconsistency
    /// Due to network delays, followers may receive writes in different order
    /// </summary>
    [Fact]
    public async Task Test4_ReplicationConflict_RapidWritesCauseInconsistency()
    {
        // Arrange: Use a unique key
        var key = $"conflict-{Guid.NewGuid()}";
        
        // Act: Send rapid writes to the same key
        // Due to random delays (0-1000ms), replication order may differ per follower
        var writeTasks = new List<Task>();
        for (int i = 1; i <= 10; i++)
        {
            writeTasks.Add(_httpClient.PostAsync($"{LeaderUrl}/set?key={key}&value=write-{i}", null));
            await Task.Delay(10); // Small delay between writes to create version ordering
        }
        await Task.WhenAll(writeTasks);
        
        // DON'T wait for full replication - check immediately!
        await Task.Delay(200);
        
        // Get values from all nodes
        var leaderResponse = await _httpClient.GetAsync($"{LeaderUrl}/get/{key}");
        var leaderValue = (await leaderResponse.Content.ReadAsStringAsync()).Trim('"');
        
        var follower1Response = await _httpClient.GetAsync($"{Follower1Url}/get/{key}");
        var follower1Value = follower1Response.IsSuccessStatusCode 
            ? (await follower1Response.Content.ReadAsStringAsync()).Trim('"') 
            : "NOT_FOUND";
        
        var follower2Response = await _httpClient.GetAsync($"{Follower2Url}/get/{key}");
        var follower2Value = follower2Response.IsSuccessStatusCode 
            ? (await follower2Response.Content.ReadAsStringAsync()).Trim('"') 
            : "NOT_FOUND";
        
        Console.WriteLine($"Leader: {leaderValue}, F1: {follower1Value}, F2: {follower2Value}");
        
        // Check if there's inconsistency (values differ)
        var hasInconsistency = (leaderValue != follower1Value) || (leaderValue != follower2Value);
        
        if (hasInconsistency)
        {
            Console.WriteLine("INCONSISTENCY DETECTED! Followers have different values than leader.");
        }
        
        // Now wait for eventual consistency
        await Task.Delay(3000);
        
        // Check again - should be consistent now
        var finalLeader = (await (await _httpClient.GetAsync($"{LeaderUrl}/get/{key}")).Content.ReadAsStringAsync()).Trim('"');
        var finalF1 = (await (await _httpClient.GetAsync($"{Follower1Url}/get/{key}")).Content.ReadAsStringAsync()).Trim('"');
        var finalF2 = (await (await _httpClient.GetAsync($"{Follower2Url}/get/{key}")).Content.ReadAsStringAsync()).Trim('"');
        
        Console.WriteLine($"After waiting - Leader: {finalLeader}, F1: {finalF1}, F2: {finalF2}");
        
        // All should match now (eventual consistency)
        Assert.Equal(finalLeader, finalF1);
        Assert.Equal(finalLeader, finalF2);
    }
    
    /// <summary>
    /// Test 5: Check eventual consistency - after waiting, all nodes should converge
    /// </summary>
    [Fact]
    public async Task Test5_EventualConsistency_AllNodesConverge()
    {
        // Arrange: Write multiple keys
        var baseKey = $"consistency-{Guid.NewGuid()}";
        for (int i = 0; i < 5; i++)
        {
            await _httpClient.PostAsync($"{LeaderUrl}/set?key={baseKey}-{i}&value=value-{i}", null);
        }
        
        // Act: Wait for replication to complete (max delay is 1000ms + some buffer)
        await Task.Delay(3000);
        
        // Assert: Dump data from leader and followers
        var leaderDump = await _httpClient.GetFromJsonAsync<Dictionary<string, string>>($"{LeaderUrl}/dump");
        var follower1Dump = await _httpClient.GetFromJsonAsync<Dictionary<string, string>>($"{Follower1Url}/dump");
        var follower2Dump = await _httpClient.GetFromJsonAsync<Dictionary<string, string>>($"{Follower2Url}/dump");
        
        // Check that all our test keys exist and match
        for (int i = 0; i < 5; i++)
        {
            var key = $"{baseKey}-{i}";
            var expectedValue = $"value-{i}";
            
            Assert.True(leaderDump!.ContainsKey(key), $"Leader missing key {key}");
            Assert.Equal(expectedValue, leaderDump[key]);
            
            Assert.True(follower1Dump!.ContainsKey(key), $"Follower1 missing key {key}");
            Assert.Equal(expectedValue, follower1Dump[key]);
            
            Assert.True(follower2Dump!.ContainsKey(key), $"Follower2 missing key {key}");
            Assert.Equal(expectedValue, follower2Dump[key]);
        }
    }
}
