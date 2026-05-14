# QUIC multiplexing demo — server (Slide 18)

Raw `System.Net.Quic` server. Listens on `127.0.0.1:5002/udp` with ALPN
`quic-demo/1`, accepts one inbound stream at a time, and drains it based on a
single-byte type marker:

- `'H'` — heartbeat stream, printed line-by-line as messages arrive.
- `'F'` — file stream, drained and reported every 25 MB.

The certificate is generated in-process and self-signed. The matching client
trusts it explicitly. **Demo wiring only.**

## Run

```bash
dotnet run --project demos/QuicMultiplexingServer
```

Leave it running and start the client in another terminal — see
`demos/QuicMultiplexingClient/README.md`.

## Platform support

`QuicListener.IsSupported` is the guard. If it returns false the server prints
a hint and exits. Linux needs `libmsquic` from the Microsoft package feed;
Windows ships MsQuic natively; macOS support is limited.
