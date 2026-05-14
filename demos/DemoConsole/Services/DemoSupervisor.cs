using System.Diagnostics;

namespace DemoConsole.Services;

/// <summary>
/// Auto-launches the HTTP/3 server and QUIC server child processes at startup,
/// tracks their liveness, and tears them down on shutdown.
/// </summary>
public sealed class DemoSupervisor : IHostedService, IAsyncDisposable
{
    public const int Http3Port = 5051;
    public const int QuicPort = 5002;

    private readonly ILogger<DemoSupervisor> _log;
    private readonly DemoState _state;
    private Child? _http3;
    private Child? _quic;

    public DemoSupervisor(ILogger<DemoSupervisor> log, DemoState state)
    {
        _log = log;
        _state = state;
    }

    public Task StartAsync(CancellationToken ct)
    {
        var http3ProjectPath = FindDemoProject("KestrelHttp3Demo");
        var quicProjectPath = FindDemoProject("QuicMultiplexingServer");
        _state.QuicClientProjectPath = FindDemoProject("QuicMultiplexingClient");

        _http3 = Spawn(
            "http3",
            http3ProjectPath,
            env: new() { ["KESTREL_HTTP3_PORT"] = Http3Port.ToString() });

        _quic = Spawn("quic", quicProjectPath, env: new());

        // Mark services ready after a brief warm-up. We don't probe — if the
        // child crashes early we'll see it via the Exited event below.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            if (_http3?.Process.HasExited == false) _state.Http3Ready = true;
            if (_quic?.Process.HasExited == false) _state.QuicReady = true;
        }, ct);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var child in new[] { _http3, _quic })
        {
            if (child is null) continue;
            try
            {
                if (!child.Process.HasExited)
                    child.Process.Kill(entireProcessTree: true);
                await child.Process.WaitForExitAsync(
                    new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "while stopping {Name}", child.Name);
            }
            finally
            {
                child.Process.Dispose();
            }
        }
        _http3 = null;
        _quic = null;
    }

    private Child Spawn(string name, string projectPath, Dictionary<string, string> env)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = projectPath,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(projectPath);
        psi.ArgumentList.Add("--no-launch-profile");
        foreach (var (k, v) in env) psi.Environment[k] = v;

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"failed to start {name}");

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) _log.LogInformation("[{Name}] {Line}", name, e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) _log.LogWarning("[{Name}!] {Line}", name, e.Data);
        };
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            _log.LogWarning("[{Name}] exited with code {Code}", name, process.ExitCode);
            if (name == "http3") _state.Http3Ready = false;
            if (name == "quic") _state.QuicReady = false;
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _log.LogInformation("[{Name}] spawned PID {Pid} from {Path}", name, process.Id, projectPath);
        return new Child(name, process);
    }

    private static string FindDemoProject(string projectName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var inDemos = Path.Combine(dir.FullName, "demos", projectName);
            if (Directory.Exists(inDemos)) return inDemos;
            var asSibling = Path.Combine(dir.FullName, projectName);
            if (Directory.Exists(asSibling) &&
                File.Exists(Path.Combine(asSibling, $"{projectName}.csproj")))
                return asSibling;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"could not locate sibling project '{projectName}' from {AppContext.BaseDirectory}");
    }

    private sealed record Child(string Name, Process Process);
}
