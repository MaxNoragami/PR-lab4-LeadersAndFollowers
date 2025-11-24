using LeadersAndFollowers.API.Clients;
using LeadersAndFollowers.API.Configuration;
using LeadersAndFollowers.API.Models;
using LeadersAndFollowers.API.Services;
using LeadersAndFollowers.Core.Abstractions;
using LeadersAndFollowers.Core.Implementation;

var builder = WebApplication.CreateBuilder(args);

// ASP.NET already has environment variables in builder.Configuration by default

// --- Read basic configuration ---

var configuration = builder.Configuration;

// NODE_ROLE: "Leader" or "Follower" (default: Leader)
var nodeRoleString = configuration["NODE_ROLE"] ?? "Leader";
if (!Enum.TryParse<NodeRole>(nodeRoleString, ignoreCase: true, out var nodeRole))
{
    nodeRole = NodeRole.Leader;
}

// WRITE_QUORUM: int, default 1
var writeQuorumStr = configuration["WRITE_QUORUM"];
var writeQuorum = int.TryParse(writeQuorumStr, out var parsedQuorum) ? parsedQuorum : 1;

// FOLLOWER_TIMEOUT_MS: timeout per follower, default 2000 ms
var followerTimeoutStr = configuration["FOLLOWER_TIMEOUT_MS"];
var followerTimeoutMs = int.TryParse(followerTimeoutStr, out var parsedTimeout) ? parsedTimeout : 2000;

// MIN_DELAY_MS / MAX_DELAY_MS: network lag simulation on leader side
var minDelayStr = configuration["MIN_DELAY_MS"];
var maxDelayStr = configuration["MAX_DELAY_MS"];
var minDelayMs = int.TryParse(minDelayStr, out var parsedMinDelay) ? parsedMinDelay : 0;
var maxDelayMs = int.TryParse(maxDelayStr, out var parsedMaxDelay) ? parsedMaxDelay : 1000;

// FOLLOWERS: semicolon-separated list of follower base URLs
// e.g. "http://follower1:5000;http://follower2:5000"
var followersEnv = configuration["FOLLOWERS"] ?? string.Empty;

// --- Register shared services (leader & follower) ---

builder.Services.AddSingleton<IKeyValueStore, InMemoryKeyValueStore>();
builder.Services.AddSingleton<ISystemClock, SystemClock>();

// We can always register topology; it's only used on the leader.
builder.Services.AddSingleton<IFollowerTopology>(_ =>
{
    var followers = followersEnv
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(url => new FollowerDescriptor(url))
        .ToList();

    return new StaticFollowerTopology(followers);
});

// --- Leader-specific services ---

if (nodeRole == NodeRole.Leader)
{
    builder.Services.AddSingleton(new ReplicationOptions(
        writeQuorum: writeQuorum,
        perFollowerTimeout: TimeSpan.FromMilliseconds(followerTimeoutMs)));

    // HttpClient for talking to followers
    builder.Services.AddHttpClient<ReplicationHttpClient>();

    // IReplicationClient implemented over HTTP
    builder.Services.AddSingleton<IReplicationClient>(sp =>
    {
        var client = sp.GetRequiredService<ReplicationHttpClient>();
        return client;
    });

    builder.Services.AddSingleton<ILeaderWriteService, LeaderWriteService>();

    // Also pass delay bounds to the replication client configuration later
    builder.Services.AddSingleton(new NetworkDelayOptions(minDelayMs, maxDelayMs));
}

var app = builder.Build();

// Simple health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "ok", role = nodeRole.ToString() }));

// --- Client-facing write endpoint (leader only) ---

if (nodeRole == NodeRole.Leader)
{
    // POST /set?key=foo&value=bar
    app.MapPost("/set", async (
        string key,
        string value,
        ILeaderWriteService leaderWriteService,
        CancellationToken ct) =>
    {
        var result = await leaderWriteService.WriteAsync(key, value, ct);

        return Results.Json(new
        {
            success = result.IsSuccess,
            quorum = result.RequiredQuorum,
            acks = result.SuccessfulFollowers
        });
    });
}

// --- Reads (leader + follower) ---

// GET /get/{key}
app.MapGet("/get/{key}", async (string key, IKeyValueStore store, CancellationToken ct) =>
{
    var value = await store.GetAsync(key, ct);
    return value is null ? Results.NotFound() : Results.Ok(value);
});

// GET /dump
// Used for final consistency check and debugging
app.MapGet("/dump", async (IKeyValueStore store, CancellationToken ct) =>
{
    var snapshot = await store.DumpAsync(ct);
    return Results.Json(snapshot);
});

// --- Replication endpoint (leader acts as client, followers receive) ---

// POST /replicate
// Body: ReplicationCommand as JSON
app.MapPost("/replicate", async (
    ReplicationCommand command,
    IKeyValueStore store,
    CancellationToken ct) =>
{
    // Apply replicated write to local store
    await store.SetAsync(command.Key, command.Value, ct);
    return Results.Ok();
});

app.Run();

