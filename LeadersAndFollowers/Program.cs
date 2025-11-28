using LeadersAndFollowers.Models;
using LeadersAndFollowers.Services;

var builder = WebApplication.CreateBuilder(args);


var config = AppConfig.FromConfiguration(builder.Configuration);

var store = new KeyValueStore(config.UseVersioning);
builder.Services.AddSingleton(store);

if (config.NodeRole == NodeRole.Leader)
{
    var httpClient = new HttpClient();
    var replicationClient = new ReplicationClient(httpClient, config.MinDelayMs, config.MaxDelayMs);
    var leaderService = new LeaderWriteService(store, replicationClient, config.Followers, config.WriteQuorum);
    builder.Services.AddSingleton(replicationClient);
    builder.Services.AddSingleton(leaderService);
}


var app = builder.Build();


app.MapGet("/health", () 
    => Results.Ok(new { status = "ok", role = config.NodeRole.ToString() }));

app.MapGet("/get/{key}", (string key, KeyValueStore store) =>
{
    var value = store.Get(key);
    return value is null ? Results.NotFound() : Results.Ok(value);
});

app.MapGet("/dump", (KeyValueStore store) =>
    Results.Json(store.GetAll()));

app.MapGet("/dump-versions", (KeyValueStore store) =>
    Results.Json(store.GetAllVersions()));

if (config.NodeRole == NodeRole.Leader)
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

if (config.NodeRole == NodeRole.Follower)
    app.MapPost("/replicate", (ReplicationCommand command, KeyValueStore store) =>
    {
        store.Set(command.Key, command.Value, command.Version);
        return Results.Ok();
    });


app.Run();
