# KestrelHttp3Demo (Slide 14)

Smallest possible diff to add HTTP/3 to an ASP.NET Core app: one Kestrel listen
option, `Http1AndHttp2AndHttp3`. Kestrel negotiates with each client and gives
them the best they support.

## Run

```bash
dotnet dev-certs https --trust          # once per machine
dotnet run --project demos/KestrelHttp3Demo
```

Server listens on `https://localhost:5001`.

## Show it on stage

```bash
# HTTP/2 (default for HttpClient / older curl):
curl -k -v https://localhost:5001/

# HTTP/3 (requires curl built with HTTP/3 support, e.g. brew install curl):
curl -k --http3 -v https://localhost:5001/
```

The response body echoes `ctx.Request.Protocol` so the audience can see
`HTTP/3` come back. The `alt-svc: h3=":5001"` response header is the
mechanism browsers use to discover HTTP/3 on the second request.

## Platform notes

- macOS: Kestrel HTTP/3 silently falls back to HTTP/2 — develop here, but
  validate the protocol negotiation on Linux or Windows.
- Linux: install `libmsquic` from the Microsoft package feed.
- Chrome: it won't speak HTTP/3 to `localhost` by default. Test against a
  loopback alias hostname or a real domain.
