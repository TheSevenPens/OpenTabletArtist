using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json.Linq;
using Bridge.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:5188")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddSingleton<DaemonClient>();
builder.Services.AddSingleton<VMultiDetector>();

var app = builder.Build();
app.UseCors();
app.UseWebSockets();

// REST endpoints — JToken serializes directly to JSON via Newtonsoft
app.MapGet("/api/tablets", async (DaemonClient client) =>
{
    var tablets = await client.GetTabletsAsync();
    return Results.Content(tablets.ToString(), "application/json");
});

app.MapGet("/api/settings", async (DaemonClient client) =>
{
    var settings = await client.GetSettingsAsync();
    return Results.Content(settings.ToString(), "application/json");
});

app.MapPost("/api/settings", async (HttpContext context, DaemonClient client) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var body = JToken.Parse(await reader.ReadToEndAsync());
    await client.SetSettingsAsync(body);
    return Results.Ok();
});

app.MapGet("/api/app-info", async (DaemonClient client) =>
{
    var info = await client.GetAppInfoAsync();
    return Results.Content(info.ToString(), "application/json");
});

app.MapGet("/api/vmulti", (VMultiDetector detector) =>
{
    var status = detector.Detect();
    return Results.Ok(status);
});

app.MapGet("/api/debug/hid-devices", (VMultiDetector detector) =>
{
    var devices = detector.ListAllHidDevices();
    return Results.Ok(new { count = devices.Count, devices = devices.Take(50) });
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
