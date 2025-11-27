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
var store = new KeyValueStore { UseVersioning = useVersioning };
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

    app.MapPost("/config", (ConfigUpdate update, LeaderWriteService leaderService, ReplicationClient replicationClient) =>
    {
        if (update.WriteQuorum.HasValue)
        {
            if (update.WriteQuorum < 1)
                return Results.BadRequest(new { error = "write_quorum must be >= 1" });
            if (update.WriteQuorum > followers.Count)
                return Results.BadRequest(new { error = $"write_quorum must be <= {followers.Count} (available followers)" });
            leaderService.WriteQuorum = update.WriteQuorum.Value;
        }

        if (update.MinDelayMs.HasValue)
        {
            if (update.MinDelayMs < 0)
                 return Results.BadRequest(new { error = "min_delay_ms must be >= 0" });
            replicationClient.MinDelayMs = update.MinDelayMs.Value;
        }

        if (update.MaxDelayMs.HasValue)
        {
            if (update.MaxDelayMs < 0)
                return Results.BadRequest(new { error = "max_delay_ms must be >= 0" });
            replicationClient.MaxDelayMs = update.MaxDelayMs.Value;
        }

        return Results.Ok(new
        {
            status = "ok",
            config = new
            {
                write_quorum = leaderService.WriteQuorum,
                min_delay_ms = replicationClient.MinDelayMs,
                max_delay_ms = replicationClient.MaxDelayMs
            }
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

// Dump all versions (for consistency analysis)
app.MapGet("/dump-versions", (KeyValueStore store) =>
{
    return Results.Json(store.GetAllVersions());
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

record ConfigUpdate(int? WriteQuorum, int? MinDelayMs, int? MaxDelayMs);


