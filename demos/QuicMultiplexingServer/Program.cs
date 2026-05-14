using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

const string Alpn = "quic-demo/1";
const int Port = 5002;

if (!QuicListener.IsSupported)
{
    Console.Error.WriteLine(
        "QUIC is not supported on this runtime/OS. Install libmsquic on Linux, " +
        "or run on Windows. macOS support is limited.");
    return 1;
}

using var cert = CreateSelfSignedCert();

var options = new QuicListenerOptions
{
    ListenEndPoint = new IPEndPoint(IPAddress.Loopback, Port),
    ApplicationProtocols = new List<SslApplicationProtocol> { new(Alpn) },
    ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions
    {
        DefaultStreamErrorCode = 0,
        DefaultCloseErrorCode = 0,
        ServerAuthenticationOptions = new SslServerAuthenticationOptions
        {
            ApplicationProtocols = new List<SslApplicationProtocol> { new(Alpn) },
            ServerCertificate = cert,
        },
    }),
};

await using var listener = await QuicListener.ListenAsync(options);
Console.WriteLine($"[server] listening on quic://{listener.LocalEndPoint}  (alpn={Alpn})");

while (true)
{
    var connection = await listener.AcceptConnectionAsync();
    _ = HandleConnectionAsync(connection);
}

static async Task HandleConnectionAsync(QuicConnection connection)
{
    Console.WriteLine($"[server] connection from {connection.RemoteEndPoint}");
    try
    {
        while (true)
        {
            var stream = await connection.AcceptInboundStreamAsync();
            _ = HandleStreamAsync(stream);
        }
    }
    catch (QuicException) { /* peer closed */ }
    finally
    {
        await connection.DisposeAsync();
        Console.WriteLine("[server] connection closed");
    }
}

static async Task HandleStreamAsync(QuicStream stream)
{
    await using (stream)
    {
        var marker = new byte[1];
        if (await stream.ReadAsync(marker.AsMemory(0, 1)) == 0) return;

        switch ((char)marker[0])
        {
            case 'H': await DrainHeartbeatAsync(stream); break;
            case 'F': await DrainFileAsync(stream); break;
            default:
                Console.WriteLine($"[server] unknown stream type '{(char)marker[0]}'");
                break;
        }
    }
}

static async Task DrainHeartbeatAsync(QuicStream stream)
{
    using var reader = new StreamReader(stream, leaveOpen: true);
    string? line;
    while ((line = await reader.ReadLineAsync()) != null)
    {
        Console.WriteLine($"[server][heartbeat] {line}");
    }
}

static async Task DrainFileAsync(QuicStream stream)
{
    var buffer = new byte[64 * 1024];
    long total = 0;
    long lastReportedMb = 0;
    var sw = System.Diagnostics.Stopwatch.StartNew();

    while (true)
    {
        var n = await stream.ReadAsync(buffer);
        if (n == 0) break;
        total += n;

        var mb = total / (1024 * 1024);
        if (mb - lastReportedMb >= 25)
        {
            Console.WriteLine($"[server][file] received {mb} MB");
            lastReportedMb = mb;
        }
    }

    var mbDone = total / (1024.0 * 1024);
    Console.WriteLine(
        $"[server][file] complete: {mbDone:F1} MB in {sw.Elapsed.TotalSeconds:F2}s " +
        $"({mbDone / sw.Elapsed.TotalSeconds:F1} MB/s)");
}

static X509Certificate2 CreateSelfSignedCert()
{
    using var rsa = RSA.Create(2048);
    var req = new CertificateRequest(
        "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    var san = new SubjectAlternativeNameBuilder();
    san.AddDnsName("localhost");
    san.AddIpAddress(IPAddress.Loopback);
    req.CertificateExtensions.Add(san.Build());

    req.CertificateExtensions.Add(new X509KeyUsageExtension(
        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));
    req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
        new OidCollection { new("1.3.6.1.5.5.7.3.1") }, critical: false)); // serverAuth

    using var ephemeral = req.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
    var pfx = ephemeral.Export(X509ContentType.Pfx);
    return X509CertificateLoader.LoadPkcs12(pfx, password: null);
}
