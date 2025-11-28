using LeadersAndFollowers.Models;
using LeadersAndFollowers.Services;

var builder = WebApplication.CreateBuilder(args);

// Read configuration from environment variables
var nodeRoleString = builder.Configuration["NODE_ROLE"] ?? "Leader";
var nodeRole = Enum.TryParse<NodeRole>(nodeRoleString, true, out var role) ? role : NodeRole.Leader;

var writeQuorum = int.TryParse(builder.Configuration["WRITE_QUORUM"], out var wq) ? wq : 1;
var minDelayMs = int.TryParse(builder.Configuration["MIN_DELAY_MS"], out var minDelay) ? minDelay : 0;
var maxDelayMs = int.TryParse(builder.Configuration["MAX_DELAY_MS"], out var maxDelay) ? maxDelay : 1000;

var followersEnv = builder.Configuration["FOLLOWERS"] ?? string.Empty;
var followers = followersEnv
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .ToList();

// Register services
var store = new KeyValueStore();
builder.Services.AddSingleton(store);

if (nodeRole == NodeRole.Leader)
{
    var httpClient = new HttpClient();
    var replicationClient = new ReplicationClient(httpClient, minDelayMs, maxDelayMs);
    var leaderService = new LeaderWriteService(store, replicationClient, followers, writeQuorum);
    builder.Services.AddSingleton(leaderService);
}

var app = builder.Build();

// Health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "ok", role = nodeRole.ToString() }));

// Write endpoint (leader only)
if (nodeRole == NodeRole.Leader)
{
    app.MapPost("/set", async (string key, string value, LeaderWriteService leaderService) =>
    {
        var result = await leaderService.WriteAsync(key, value);
        return Results.Json(new
        {
            success = result.IsSuccess,
            quorum = result.RequiredQuorum,
            acks = result.SuccessfulFollowers
        });
    });
}

// Read endpoint (both leader and follower)
app.MapGet("/get/{key}", (string key, KeyValueStore store) =>
{
    var value = store.Get(key);
    return value is null ? Results.NotFound() : Results.Ok(value);
});

// Dump all data (for consistency checks)
app.MapGet("/dump", (KeyValueStore store) =>
{
    return Results.Json(store.GetAll());
});

// Replication endpoint (follower only)
if (nodeRole == NodeRole.Follower)
{
    app.MapPost("/replicate", (ReplicationCommand command, KeyValueStore store) =>
    {
        store.Set(command.Key, command.Value, command.Version);
        return Results.Ok();
    });
}

app.Run();


