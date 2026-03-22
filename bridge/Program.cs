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

app.MapGet("/api/presets", async (DaemonClient client) =>
{
    var appInfo = await client.GetAppInfoAsync();
    var presetDir = appInfo["PresetDirectory"]?.ToString();
    if (string.IsNullOrEmpty(presetDir) || !Directory.Exists(presetDir))
        return Results.Ok(Array.Empty<object>());

    var presets = Directory.GetFiles(presetDir, "*.json")
        .Select(f =>
        {
            var name = Path.GetFileNameWithoutExtension(f);
            string? content = null;
            try { content = File.ReadAllText(f); } catch { }
            return new { name, path = f, content };
        })
        .Where(p => p.content != null)
        .ToList();

    return Results.Ok(presets);
});

app.MapPost("/api/presets/save", async (HttpContext context, DaemonClient client) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var body = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(await reader.ReadToEndAsync());
    var name = body?.GetValueOrDefault("name")?.Trim();

    if (string.IsNullOrEmpty(name))
        return Results.BadRequest(new { error = "Name is required" });

    // Sanitize filename
    foreach (var c in Path.GetInvalidFileNameChars())
        name = name.Replace(c, '_');

    var appInfo = await client.GetAppInfoAsync();
    var presetDir = appInfo["PresetDirectory"]?.ToString();
    if (string.IsNullOrEmpty(presetDir))
        return Results.BadRequest(new { error = "Could not determine preset directory" });

    Directory.CreateDirectory(presetDir);

    var settings = await client.GetSettingsAsync();
    var filePath = Path.Combine(presetDir, $"{name}.json");

    if (File.Exists(filePath))
        return Results.Conflict(new { error = "A snapshot with this name already exists" });

    await File.WriteAllTextAsync(filePath, settings.ToString(Newtonsoft.Json.Formatting.Indented));
    return Results.Ok(new { name, path = filePath });
});

app.MapPost("/api/presets/load", async (HttpContext context, DaemonClient client) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var body = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(await reader.ReadToEndAsync());
    var name = body?.GetValueOrDefault("name");

    if (string.IsNullOrEmpty(name))
        return Results.BadRequest(new { error = "Name is required" });

    var appInfo = await client.GetAppInfoAsync();
    var presetDir = appInfo["PresetDirectory"]?.ToString();
    if (string.IsNullOrEmpty(presetDir))
        return Results.BadRequest(new { error = "Could not determine preset directory" });

    var filePath = Path.Combine(presetDir, $"{name}.json");
    if (!File.Exists(filePath))
        return Results.NotFound(new { error = "Snapshot not found" });

    var content = await File.ReadAllTextAsync(filePath);
    var settings = Newtonsoft.Json.Linq.JToken.Parse(content);
    await client.SetSettingsAsync(settings);
    return Results.Ok(new { loaded = name });
});

app.MapPost("/api/presets/delete", async (HttpContext context, DaemonClient client) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var body = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(await reader.ReadToEndAsync());
    var name = body?.GetValueOrDefault("name");

    if (string.IsNullOrEmpty(name))
        return Results.BadRequest(new { error = "Name is required" });

    var appInfo = await client.GetAppInfoAsync();
    var presetDir = appInfo["PresetDirectory"]?.ToString();
    if (string.IsNullOrEmpty(presetDir))
        return Results.BadRequest(new { error = "Could not determine preset directory" });

    var filePath = Path.Combine(presetDir, $"{name}.json");
    if (!File.Exists(filePath))
        return Results.NotFound(new { error = "Snapshot not found" });

    File.Delete(filePath);
    return Results.Ok(new { deleted = name });
});

app.MapPost("/api/open-folder", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var body = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(await reader.ReadToEndAsync());
    var path = body?.GetValueOrDefault("path");

    if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        return Results.BadRequest(new { error = "Folder does not exist", path });

    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = path,
        UseShellExecute = true,
    });
    return Results.Ok(new { opened = path });
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
