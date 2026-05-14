# QUIC multiplexing demo — client (Slide 18)

Opens one `QuicConnection` to the demo server and runs two streams in
parallel over it:

1. **Heartbeat** — one line every second so the audience can hear the
   metronome.
2. **File** — a bulk upload (default 200 MB) that runs as fast as MsQuic
   will let it.

The point of the slide: the heartbeat ticks stay on cadence while the file
saturates the connection. One lost packet on the file stream would not stall
the heartbeat — that's the head-of-line-blocking fix in practice.

## Run

```bash
# Terminal 1: start the server first.
dotnet run --project demos/QuicMultiplexingServer

# Terminal 2: run the client.
dotnet run --project demos/QuicMultiplexingClient
```

### CLI

```bash
# Bigger payload, fewer ticks
dotnet run --project demos/QuicMultiplexingClient -- --size 500 --ticks 20

# size  = total upload size in MB (default 200)
# ticks = max heartbeat ticks before giving up (default 30)
```

## What the audience should see

In the client window, heartbeat ticks (`tick 01 sent`, `tick 02 sent`, …)
arrive every second. Interleaved with them are `uploaded N / TOTAL MB`
lines as the file flies through. When the file finishes, the heartbeat
gets one more tick and then the client closes the connection cleanly.

## Platform support

Same story as the server. `QuicConnection.IsSupported` guards the connect
call; if it returns false the client prints a hint and exits.
