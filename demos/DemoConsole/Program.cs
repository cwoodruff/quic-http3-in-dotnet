using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Text.Json;
using DemoConsole.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5000");
builder.Services.AddSingleton<DemoState>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DemoSupervisor>());
builder.Services.AddSingleton<DemoSupervisor>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", (DemoState state) => Results.Json(new
{
    http3 = state.Http3Ready,
    quic = state.QuicReady,
    http3Port = DemoSupervisor.Http3Port,
    quicPort = DemoSupervisor.QuicPort,
}));

app.MapGet("/demo/http2", async (HttpContext ctx) =>
    await RunHttpDemoAsync(ctx, HttpVersion.Version20, HttpVersionPolicy.RequestVersionExact));

app.MapGet("/demo/http3", async (HttpContext ctx) =>
    await RunHttpDemoAsync(ctx, HttpVersion.Version30, HttpVersionPolicy.RequestVersionOrLower));

app.MapGet("/demo/multiplex", async (HttpContext ctx, DemoState state, ILoggerFactory lf) =>
    await RunMultiplexDemoAsync(ctx, state, lf.CreateLogger("multiplex")));

app.Run();

static async Task RunHttpDemoAsync(
    HttpContext ctx, Version version, HttpVersionPolicy policy)
{
    PrepareSse(ctx);
    var url = $"https://localhost:{DemoSupervisor.Http3Port}/";

    await WriteSse(ctx, "status",
        $"GET {url} — requested {version} ({policy})");

    var handler = new SocketsHttpHandler
    {
        SslOptions = new SslClientAuthenticationOptions
        {
            // Dev cert from KestrelHttp3Demo; trust unconditionally for demo only.
            RemoteCertificateValidationCallback = (_, _, _, _) => true,
        },
    };
    using var client = new HttpClient(handler);
    var req = new HttpRequestMessage(HttpMethod.Get, url)
    {
        Version = version,
        VersionPolicy = policy,
    };

    var sw = Stopwatch.StartNew();
    try
    {
        using var resp = await client.SendAsync(req, ctx.RequestAborted);
        var body = await resp.Content.ReadAsStringAsync(ctx.RequestAborted);
        var altSvc = resp.Headers.TryGetValues("alt-svc", out var v) ? string.Join(", ", v) : null;

        var payload = JsonSerializer.Serialize(new
        {
            negotiated = $"HTTP/{resp.Version}",
            requested = $"HTTP/{version}",
            policy = policy.ToString(),
            status = $"{(int)resp.StatusCode} {resp.ReasonPhrase}",
            altSvc,
            body = body.TrimEnd(),
            elapsedMs = sw.ElapsedMilliseconds,
        });
        await WriteSse(ctx, "response", payload);
    }
    catch (Exception ex)
    {
        await WriteSse(ctx, "error", ex.Message);
    }
    await WriteSse(ctx, "done", "");
}

static async Task RunMultiplexDemoAsync(HttpContext ctx, DemoState state, ILogger log)
{
    PrepareSse(ctx);

    if (!await state.MultiplexLock.WaitAsync(0))
    {
        await WriteSse(ctx, "error", "a multiplex demo is already running");
        await WriteSse(ctx, "done", "");
        return;
    }

    try
    {
        if (!state.QuicReady)
        {
            await WriteSse(ctx, "error", "QUIC server not ready");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = state.QuicClientProjectPath,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(state.QuicClientProjectPath);
        psi.ArgumentList.Add("--no-launch-profile");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("--size");
        psi.ArgumentList.Add(GetQuery(ctx, "size", "200"));
        psi.ArgumentList.Add("--ticks");
        psi.ArgumentList.Add(GetQuery(ctx, "ticks", "30"));

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("failed to start client");

        await WriteSse(ctx, "status", $"spawned client PID {process.Id}");

        // Cancel the child if the SSE connection drops.
        using var reg = ctx.RequestAborted.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* best-effort */ }
        });

        var stdoutTask = ForwardAsync(process.StandardOutput, ctx, isError: false, log);
        var stderrTask = ForwardAsync(process.StandardError, ctx, isError: true, log);
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync(ctx.RequestAborted);

        await WriteSse(ctx, "exit", process.ExitCode.ToString());
    }
    catch (Exception ex)
    {
        await WriteSse(ctx, "error", ex.Message);
    }
    finally
    {
        state.MultiplexLock.Release();
        await WriteSse(ctx, "done", "");
    }
}

static async Task ForwardAsync(StreamReader reader, HttpContext ctx, bool isError, ILogger log)
{
    string? line;
    while ((line = await reader.ReadLineAsync(ctx.RequestAborted)) != null)
    {
        var evt = isError ? "stderr" :
            line.Contains("[heartbeat]") ? "heartbeat" :
            line.Contains("[file]") ? "file" :
            "stdout";
        log.LogDebug("client {Evt}: {Line}", evt, line);
        await WriteSse(ctx, evt, line);
    }
}

static string GetQuery(HttpContext ctx, string key, string fallback) =>
    ctx.Request.Query.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString() : fallback;

static void PrepareSse(HttpContext ctx)
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";
}

static async Task WriteSse(HttpContext ctx, string eventName, string data)
{
    var sb = new System.Text.StringBuilder();
    sb.Append("event: ").Append(eventName).Append('\n');
    foreach (var line in data.Split('\n'))
        sb.Append("data: ").Append(line).Append('\n');
    sb.Append('\n');
    await ctx.Response.WriteAsync(sb.ToString(), ctx.RequestAborted);
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
}
