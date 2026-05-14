namespace DemoConsole.Services;

/// <summary>Shared readiness/state between the supervisor and the endpoints.</summary>
public sealed class DemoState
{
    public bool Http3Ready { get; set; }
    public bool QuicReady { get; set; }
    public string QuicClientProjectPath { get; set; } = "";

    /// <summary>Set when a multiplex run is in progress so we reject concurrent clicks.</summary>
    public readonly SemaphoreSlim MultiplexLock = new(1, 1);
}
