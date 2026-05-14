using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

var port = int.TryParse(Environment.GetEnvironmentVariable("KESTREL_HTTP3_PORT"), out var p) ? p : 5001;

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(port, listen =>
    {
        listen.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
        listen.UseHttps();
    });
});

var app = builder.Build();

// Advertise HTTP/3 on the same authority so HTTP/2 clients know to upgrade.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.AltSvc = $"h3=\":{port}\"; ma=86400";
    await next();
});

app.MapGet("/", (HttpContext ctx) =>
    $"Hello from {ctx.Request.Protocol} over {(ctx.Request.IsHttps ? "TLS" : "plaintext")}\n");

app.Run();
