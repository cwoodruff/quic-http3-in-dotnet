# DemoConsole — stage driver

Single ASP.NET Core app that runs the QUIC/HTTP/3 demos behind a web UI.
Spawns `KestrelHttp3Demo` (on port 5051) and `QuicMultiplexingServer` (on
UDP 5002) as child processes at startup, and exposes three buttons:

1. **HTTP/2 request** — fires `HttpClient` with `Version20` + exact policy at
   `https://localhost:5051/`. Renders the negotiated protocol, status, and
   the `alt-svc` header that advertises HTTP/3.
2. **HTTP/3 request** — same URL, `Version30` + `RequestVersionOrLower`.
   Negotiates QUIC on Linux/Windows; cleanly falls back to HTTP/2 on macOS
   so the slide-13 punch line is visible.
3. **Multiplexed transfer** — spawns `QuicMultiplexingClient` and streams
   its stdout into side-by-side panels: heartbeat ticks on the left, file
   progress (with a progress bar) on the right.

The page connects to a Server-Sent Events endpoint per demo. The
multiplex run is single-flight — a second click while one is running gets
a "demo already running" error rather than racing.

## Run

```bash
dotnet dev-certs https --trust          # once per machine
dotnet run --project demos/DemoConsole
```

Open <http://localhost:5000>. Use the buttons or the keyboard shortcuts
`1`, `2`, `3`, `c`.

The two readiness pills at the top right go green once each child process
has had a few seconds to warm up. If a child crashes the corresponding
pill flips back to red — useful to glance at before clicking.

## Stage tips

- Tested for projection at 28 px output / 26 px caption. Adjust by changing
  the `font-size` rules in `wwwroot/style.css` if your room needs bigger.
- The `c` key clears the output panel between demos — handy if you want
  to re-fire button [1] after [2] so the audience can compare them.
- On macOS, button [2] will report `HTTP/2` instead of `HTTP/3`. That's
  the silent-fallback story from slide 13 — own it, don't apologize.
- The child processes are torn down with `Process.Kill(entireProcessTree:
  true)` on shutdown. If you Ctrl-C the console and see stragglers,
  `lsof -i :5002 -i :5051` will show you who's still around.
- macOS gotcha: AirPlay Receiver also listens on port 5000. If startup
  fails with "address already in use", disable AirPlay Receiver in
  System Settings → General → AirDrop & Handoff, or change the port in
  `Program.cs` (`UseUrls("http://localhost:5000")`).

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│ DemoConsole  (http://localhost:5000)                    │
│   ├─ wwwroot/index.html, style.css, app.js              │
│   ├─ /health         readiness JSON                     │
│   ├─ /demo/http2     SSE — HttpClient → :5051           │
│   ├─ /demo/http3     SSE — HttpClient → :5051           │
│   └─ /demo/multiplex SSE — spawns QuicMultiplexingClient│
│        ↓ supervises                                     │
│  ┌────────────────────────┐ ┌────────────────────────┐ │
│  │ KestrelHttp3Demo       │ │ QuicMultiplexingServer │ │
│  │ https://localhost:5051 │ │ udp://localhost:5002    │ │
│  └────────────────────────┘ └────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```
