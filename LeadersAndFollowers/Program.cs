using LeadersAndFollowers.Models;
using LeadersAndFollowers.Services;

var builder = WebApplication.CreateBuilder(args);

// Read configuration from environment variables
var nodeRoleString = builder.Configuration["NODE_ROLE"] ?? "Leader";
var nodeRole = Enum.TryParse<NodeRole>(nodeRoleString, true, out var role) ? role : NodeRole.Leader;

var writeQuorum = int.TryParse(builder.Configuration["WRITE_QUORUM"], out var wq) ? wq : 1;
var minDelayMs = int.TryParse(builder.Configuration["MIN_DELAY_MS"], out var minDelay) ? minDelay : 0;
var maxDelayMs = int.TryParse(builder.Configuration["MAX_DELAY_MS"], out var maxDelay) ? maxDelay : 1000;

var useVersioning = builder.Configuration["USE_VERSIONING"]?.ToLower() != "false";

var followersEnv = builder.Configuration["FOLLOWERS"] ?? string.Empty;
var followers = followersEnv
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .ToList();

// Register services
var store = new KeyValueStore(useVersioning);
builder.Services.AddSingleton(store);

if (nodeRole == NodeRole.Leader)
{
    var httpClient = new HttpClient();
    var replicationClient = new ReplicationClient(httpClient, minDelayMs, maxDelayMs);
    var leaderService = new LeaderWriteService(store, replicationClient, followers, writeQuorum);
    builder.Services.AddSingleton(replicationClient);
    builder.Services.AddSingleton(leaderService);
}

var app = builder.Build();

app.MapGet("/health", () 
    => Results.Ok(new { status = "ok", role = nodeRole.ToString() }));

app.MapGet("/get/{key}", (string key, KeyValueStore store) =>
{
    var value = store.Get(key);
    return value is null ? Results.NotFound() : Results.Ok(value);
});

app.MapGet("/dump", (KeyValueStore store) =>
    Results.Json(store.GetAll()));

app.MapGet("/dump-versions", (KeyValueStore store) =>
    Results.Json(store.GetAllVersions()));

if (nodeRole == NodeRole.Leader)
{
    app.MapPost("/set", async (string key, string value, LeaderWriteService leaderService) =>
    {
        var result = await leaderService.WriteAsync(key, value);
        return Results.Json(result);
    });

    app.MapPost("/config", (ConfigUpdate update, LeaderWriteService leaderService, ReplicationClient replicationClient) =>
    {
        try
        {
            if (update.WriteQuorum.HasValue)
                leaderService.WriteQuorum = update.WriteQuorum.Value;

            if (update.MinDelayMs.HasValue)
                replicationClient.MinDelayMs = update.MinDelayMs.Value;

            if (update.MaxDelayMs.HasValue)
                replicationClient.MaxDelayMs = update.MaxDelayMs.Value;

            return Results.Ok(new ConfigResult(
                leaderService.WriteQuorum,
                replicationClient.MinDelayMs,
                replicationClient.MaxDelayMs
            ));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    });
}

if (nodeRole == NodeRole.Follower)
    app.MapPost("/replicate", (ReplicationCommand command, KeyValueStore store) =>
    {
        store.Set(command.Key, command.Value, command.Version);
        return Results.Ok();
    });

app.Run();

