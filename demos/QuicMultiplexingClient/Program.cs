using System.Diagnostics;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Text;

const string Alpn = "quic-demo/1";
const int Port = 5002;

// CLI: --size <MB>   total upload size (default 200)
//      --ticks <N>   max heartbeat ticks (default 30)
long sizeBytes = ParseLongArg(args, "--size", 200L) * 1024 * 1024;
int maxTicks = (int)ParseLongArg(args, "--ticks", 30);

if (!QuicConnection.IsSupported)
{
    Console.Error.WriteLine(
        "QUIC is not supported on this runtime/OS. Install libmsquic on Linux, " +
        "or run on Windows. macOS support is limited.");
    return 1;
}

var clientOptions = new QuicClientConnectionOptions
{
    RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, Port),
    DefaultStreamErrorCode = 0,
    DefaultCloseErrorCode = 0,
    ClientAuthenticationOptions = new SslClientAuthenticationOptions
    {
        ApplicationProtocols = new List<SslApplicationProtocol> { new(Alpn) },
        TargetHost = "localhost",
        // Demo only: trust the self-signed cert the demo server generates.
        RemoteCertificateValidationCallback = (_, _, _, _) => true,
    },
};

await using var connection = await QuicConnection.ConnectAsync(clientOptions);
Console.WriteLine($"[client] connected to {connection.RemoteEndPoint}");

using var stopHeartbeat = new CancellationTokenSource();

var fileTask = Task.Run(async () =>
{
    try
    {
        await UploadFileAsync(connection, sizeBytes);
    }
    finally
    {
        // Let the heartbeat run for one more tick after the file finishes so
        // the audience sees that the heartbeat outlives the file stream.
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        stopHeartbeat.Cancel();
    }
});

var heartbeatTask = SendHeartbeatAsync(connection, maxTicks, stopHeartbeat.Token);

await Task.WhenAll(fileTask, heartbeatTask);

await connection.CloseAsync(0);
Console.WriteLine("[client] done.");
return 0;

static async Task SendHeartbeatAsync(QuicConnection conn, int maxTicks, CancellationToken ct)
{
    await using var stream = await conn.OpenOutboundStreamAsync(QuicStreamType.Unidirectional);
    await stream.WriteAsync(new byte[] { (byte)'H' });

    var sw = Stopwatch.StartNew();
    for (int tick = 1; tick <= maxTicks; tick++)
    {
        if (ct.IsCancellationRequested) break;

        var line = $"tick {tick:D2}  t={sw.Elapsed.TotalSeconds,6:F2}s\n";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(line));
        await stream.FlushAsync();
        Console.WriteLine($"[client][heartbeat] tick {tick:D2} sent  (file uploading in parallel)");

        try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
        catch (TaskCanceledException) { break; }
    }

    stream.CompleteWrites();
    await stream.WritesClosed;
}

static async Task UploadFileAsync(QuicConnection conn, long totalBytes)
{
    await using var stream = await conn.OpenOutboundStreamAsync(QuicStreamType.Unidirectional);
    await stream.WriteAsync(new byte[] { (byte)'F' });

    var chunk = new byte[64 * 1024];
    Random.Shared.NextBytes(chunk);

    long sent = 0;
    long lastReportedMb = 0;
    var sw = Stopwatch.StartNew();

    while (sent < totalBytes)
    {
        var n = (int)Math.Min(chunk.Length, totalBytes - sent);
        await stream.WriteAsync(chunk.AsMemory(0, n));
        sent += n;

        var mb = sent / (1024 * 1024);
        if (mb - lastReportedMb >= 25)
        {
            Console.WriteLine($"[client][file] uploaded {mb} / {totalBytes / (1024 * 1024)} MB");
            lastReportedMb = mb;
        }
    }

    stream.CompleteWrites();
    await stream.WritesClosed;

    var mbDone = sent / (1024.0 * 1024);
    Console.WriteLine(
        $"[client][file] complete: {mbDone:F1} MB in {sw.Elapsed.TotalSeconds:F2}s " +
        $"({mbDone / sw.Elapsed.TotalSeconds:F1} MB/s)");
}

static long ParseLongArg(string[] args, string name, long fallback)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == name && long.TryParse(args[i + 1], out var v))
            return v;
    return fallback;
}
