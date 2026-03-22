using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Bridge.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddSingleton<DaemonClient>();

var app = builder.Build();
app.UseCors();
app.UseWebSockets();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
};

// REST endpoints
app.MapGet("/api/tablets", async (DaemonClient client) =>
{
    var tablets = await client.GetTabletsAsync();
    return Results.Ok(tablets);
});

app.MapGet("/api/settings", async (DaemonClient client) =>
{
    var settings = await client.GetSettingsAsync();
    return Results.Ok(settings);
});

app.MapPost("/api/settings", async (DaemonClient client, JsonElement body) =>
{
    await client.SetSettingsAsync(body);
    return Results.Ok();
});

app.MapGet("/api/app-info", async (DaemonClient client) =>
{
    var info = await client.GetAppInfoAsync();
    return Results.Ok(info);
});

// WebSocket endpoint for real-time events
app.Map("/ws", async (HttpContext context, DaemonClient client) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var ws = await context.WebSockets.AcceptWebSocketAsync();
    var cts = new CancellationTokenSource();

    void OnEvent(string json)
    {
        if (ws.State == WebSocketState.Open)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            _ = ws.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
        }
    }

    client.OnDaemonEvent += OnEvent;

    try
    {
        var buffer = new byte[1024];
        while (ws.State == WebSocketState.Open)
        {
            await ws.ReceiveAsync(buffer, cts.Token);
        }
    }
    catch (WebSocketException) { }
    finally
    {
        client.OnDaemonEvent -= OnEvent;
        cts.Cancel();
    }
});

// Start daemon connection in background
var daemonClient = app.Services.GetRequiredService<DaemonClient>();
_ = Task.Run(() => daemonClient.ConnectAsync());

app.Run("http://localhost:5000");
