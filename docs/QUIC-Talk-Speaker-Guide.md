# Speaker Guide — QUIC and HTTP/3 in .NET

*The Future of Fast, Secure Networking*

A 26-slide deck targeting a **45-minute slot** (≈35 min talk + 10 min Q&A). Notes assume the deck Chris built; slide numbers reference that ordering.

---

## Table of contents

- [The arc of the talk](#the-arc-of-the-talk)
- [Pre-talk checklist](#pre-talk-checklist)
- [Opening: the first 90 seconds](#opening-the-first-90-seconds)
- [Slide-by-slide notes](#slide-by-slide-notes)
- [Demo plan and backup](#demo-plan-and-backup)
- [Q&A preparation](#qa-preparation)
- [Things to remember on stage](#things-to-remember-on-stage)

---

## The arc of the talk

The deck has five movements. Land each one before moving on.

1. **The problem** (slides 1–2). TCP is old. Users feel it. We have a fix.
2. **What QUIC is** (slides 3–10). UDP foundation, four superpowers, why it beats TCP/TLS/HTTP2 on real workloads.
3. **QUIC in .NET** (slides 11–18). Kestrel one-liner, HttpClient, then the raw `System.Net.Quic` namespace.
4. **Does it actually win?** (slides 19–22). Decision matrix, benchmark code, illustrative results, real-world use cases.
5. **Production reality** (slides 23–26). Fallback, debugging, takeaways, Q&A.

If you fall behind, compress in movement 2 (the comparison table on slide 7 is a visual you can land in 30 seconds) or movement 3 (skip slide 17 and reference it as "and the client side mirrors the server — see the deck").

---

## Pre-talk checklist

**Equipment**

- HDMI adapter packed, plus a USB-C backup
- Presenter remote with fresh batteries
- Bottle of water at the lectern, not on the table where you'll knock it over
- Phone on Do Not Disturb (not silent — DND)

**Demo environment** (only if running live)

- Two terminal windows visible: one for the Kestrel server, one for an HTTP/3 client (`curl --http3`, or your HttpClient sample)
- Browser open to `chrome://flags/#enable-quic` confirmed on, and DevTools Network tab ready to show the `alt-svc` header
- A working cert. `dotnet dev-certs https --trust` run on the demo machine well before showtime
- On Linux: confirm `libmsquic` is installed
- A printed fallback (screenshots) for every demo, in case the network dies

**Slide deck**

- Open the file once in PowerPoint on the room's machine before the audience arrives; render once
- Confirm slide 21's chart renders correctly — that's the one piece of the deck most likely to fail to import
- Page-down once at the start to skip past the title slide, then page-up — this catches projector handshakes that hide the first transition

---

## Opening: the first 90 seconds

Don't open with "Hi, I'm Chris." Open with the problem. The intro is on slide 1; you can introduce yourself while it's visible.

**Suggested opener** (deliver while slide 1 is up):

> "Every one of you ran some code over TCP this morning. You loaded this conference's website, checked Slack, joined Wi-Fi. Every byte went through a transport protocol designed in 1981 — four years before the first Macintosh shipped. Today I want to show you what's replacing it, and how to use the replacement right now in .NET 8 and 9."

Then advance to slide 2 and land the hook.

---

## Slide-by-slide notes

Times are talk-time targets, not hard caps.

### Slide 1 — Title (≈30 s)

Don't read the title. Introduce yourself in one breath: *"I'm Chris Woodruff, I wrote the C# Networking book, and this is QUIC."* Move on.

### Slide 2 — TCP is starting to feel its age (≈1:30)

Each stat is a beat:

- **1980** — *"That's older than most of you, and definitely older than the iPhone. TCP was designed when the internet had four buildings on it. Everything was wired."*
- **2–3 RTT** — *"Before HTTP can move a byte, TCP shakes hands, then TLS shakes hands. Across the Atlantic that's a third of a second you spend waiting for nothing."*
- **1 packet** — *"And HTTP/2's fix for HTTP/1's connection-per-request? It put all the streams on one TCP connection. Now one lost packet stalls every download on the page."*

Land the bottom line out loud: *"Mobile users feel this most. QUIC was designed to fix all three."*

### Slide 3 — Agenda (≈45 s)

Don't read every bullet. Tell them the shape: *"We're going to spend ten minutes on what QUIC is, ten minutes on the code, ten minutes on what to expect in production. Demos throughout."*

### Slide 4 — What is QUIC? (≈1:30)

The stack diagram is the point. Walk up it bottom-to-top: *"IP, UDP — that part you know. Then QUIC sits where TCP would, and it has TLS 1.3 inside it. HTTP/3 rides on top."* The key insight is that **TLS is part of the transport, not on top of it**.

### Slide 5 — QUIC's four superpowers (≈1:30)

Four cards, one beat each. Don't dwell — every one of these gets a deeper slide later. This slide is the headline; the next six are the article.

### Slide 6 — HTTP timeline (≈1:00)

The whole point of this slide is the last dot. Linger on 2022 / HTTP/3: *"This is the first HTTP version in 26 years that doesn't ride on TCP. That's the whole story of this talk."*

### Slide 7 — Comparison table (≈1:00)

You don't need to read the rows. Tell the audience to scan it and call out the two rows that matter most: **Connection setup** (1 round trip vs 2–3) and **Head-of-line blocking** (eliminated vs not). Everything else is consequence.

### Slide 8 — The handshake diagram (≈2:00)

This is where you slow down. The left half is the TCP+TLS handshake. Walk down it: SYN, SYN/ACK, ACK with ClientHello, ServerHello with cert, two Finished messages, then finally data. The right half is QUIC: one round trip, with TLS folded into the transport. Then deliver the punchline:

> *"And on a reconnect, QUIC can do zero round trips. Your second request to the same server starts moving data in the very first packet."*

### Slide 9 — Head-of-line blocking (≈1:30)

Use the visual literally. Point to stream B in the TCP side: *"One packet drops. Watch what happens to streams A, C, and D — they're stuck. They have to wait for B to retransmit because they're all sharing the TCP byte stream."* Then point to the QUIC side: *"Same packet drops. A, C, and D keep flowing. Only B pauses."*

### Slide 10 — Connection migration (≈1:00)

Set the scene: *"You walk out of the office, your phone drops Wi-Fi and grabs LTE. On TCP, your connection is dead — different IP, different port, start over. On QUIC, the connection ID stays the same. Your video call doesn't blink."*

### Slide 11 — Part two divider (≈15 s)

Just say *"Okay, that's QUIC. Now let's write some code."* and move on.

### Slide 12 — Three pillars of System.Net.Quic (≈1:30)

Anchor each name to a job:
- `QuicListener` — accepts connections (think `TcpListener`)
- `QuicConnection` — one connection, both sides use it
- `QuicStream` — the data plane, where multiplexing actually happens

### Slide 13 — OS requirements (≈1:00)

Don't dwell, but say the macOS line out loud — it always gets a chuckle and it's the most likely thing to bite someone in the audience: *"If you develop on a Mac, your HTTP/3 server falls back to HTTP/2 silently. Test on Linux or Windows before you trust your benchmarks."*

### Slide 14 — Kestrel HTTP/3 in 4 lines (≈2:30)

The applause line for this slide is **"that's the entire change."** Land it:

> *"This is the whole diff to add HTTP/3 to an ASP.NET Core app. One property: Http1AndHttp2AndHttp3. Kestrel negotiates with each client and gives them the best they support."*

If you're running a demo, this is the moment: run the server in one terminal, `curl --http3 https://localhost:5001/` in another. Show the response coming back with `HTTP/3 200`.

### Slide 15 — HttpClient for HTTP/3 (≈1:30)

The two pill boxes at the bottom are the meat. Most code in the wild gets this wrong — `RequestVersionExact` fails closed, `RequestVersionOrLower` falls back gracefully. *"In production, always RequestVersionOrLower. Always."*

### Slide 16 — QuicListener (≈2:00)

Don't read the code line by line. The audience can read C#. Highlight the three things that matter:

1. The `IsSupported` check — *"Always do this; it fails open."*
2. The `ApplicationProtocols` field — *"This is how you say `h3`. It's the ALPN identifier."*
3. The `while(true)` accept loop — *"Same pattern as TcpListener. Spawn a task per connection."*

### Slide 17 — QuicConnection (client) (≈1:30)

Same approach: don't read it. Call out:

- `RemoteEndPoint` — *"This is where TCP would point you too."*
- `MaxInbound*Streams` — *"This is QUIC's flow control. You're telling the peer how many streams you'll accept."*

### Slide 18 — QuicStream multiplexing (≈2:30)

This is the second applause-line slide. Build it up: *"Look at the client code at the bottom. Three streams: telemetry, file transfer, control. All on the same connection. The file upload doesn't block the heartbeat. The heartbeat doesn't block the control channel. That's the head-of-line blocking fix in practice."*

If you're running a demo, this is where to show the stream behavior — a fake "big file" upload running while another stream still ticks every second.

### Slide 19 — HttpClient vs QuicConnection decision guide (≈1:30)

Don't agonize over this slide. Land one line: *"99% of you should use HttpClient. The other 1% — game devs, custom protocols — that's why QuicConnection exists."*

### Slide 20 — Benchmark code (≈1:30)

Quick scroll. The interesting bit is at the bottom: two calls to `Measure`, one with HTTP/2, one with HTTP/3. *"This is the kind of harness you want to run in your own environment. The numbers depend wildly on your network."*

### Slide 21 — Performance chart (≈1:30)

These are illustrative numbers. **Say that out loud.** Then walk the bars left-to-right:

- *"On a clean network, almost no difference. Don't switch to HTTP/3 to get faster localhost calls."*
- *"As packet loss climbs, HTTP/3 pulls ahead — that's the head-of-line blocking story."*
- *"And on a network change — Wi-Fi to LTE — HTTP/2 has to reconnect. QUIC just keeps going."*

### Slide 22 — Real-world use cases (≈1:00)

Four cards, one breath each. Don't dwell.

### Slide 23 — Fallback strategy (≈1:30)

The numbered list on the right is the slide's main content. Walk through it:

> *"Client connects, gets the page over HTTP/2. The server response has an `Alt-Svc` header that says 'hey, I also speak HTTP/3 on UDP port 443.' The client tries QUIC for the next request. If it works, great. If a firewall blocks UDP/443 — and many corporate networks do — the client stays on HTTP/2. Nobody notices."*

### Slide 24 — Debugging HTTP/3 (≈1:30)

Hit each tip briefly. The browser localhost gotcha is the most useful one to highlight: *"Chrome won't do HTTP/3 to localhost by default. Test against a real hostname, even a loopback alias."*

### Slide 25 — Key takeaways (≈1:30)

Slow down. Pause between numbers. The four takeaways are the message of the talk; if someone walks out remembering only this slide, that's a win.

### Slide 26 — Q&A (open)

Don't fill the silence. Wait for the first question. If it doesn't come in ten seconds: *"Common one I get is 'is HTTP/3 worth turning on today?' — short answer, yes, and here's why…"* and seed the discussion yourself.

---

## Demo plan and backup

**If you're doing one demo, do slide 14's Kestrel demo.** It's the highest-impact, lowest-risk: enable three protocols in one line, watch curl negotiate up to HTTP/3, point at the `alt-svc` header in DevTools.

**If you're doing two, add slide 18's multiplexing demo.** A pair of streams on one `QuicConnection` — one sending a 100 MB file, one sending a 1-line heartbeat every second. Show the heartbeat staying steady while the file climbs.

**If the demo fails:**

- Do not try to debug live. Audiences smell flop sweat.
- Have screenshots in the deck (consider adding a hidden slide 14b with the curl output)
- Say *"Looks like the demo gods aren't with us — here's what should have happened…"* and narrate over the screenshot. The audience forgives this gracefully.

**Tooling notes:**

- `curl --http3 -v https://your-server` is your friend for showing the protocol negotiation. The `-v` output shows the ALPN handshake landing on `h3`.
- In Chrome DevTools, the **Protocol** column in Network → right-click headers shows `h3` for HTTP/3 requests.
- Wireshark with the QUIC dissector is impressive but cuts hard against a live audience — save it for a workshop.

---

## Q&A preparation

These come up almost every time. Have an answer ready.

**"Why not just upgrade TCP?"**
TCP lives in operating system kernels. Upgrading it means upgrading every router, every load balancer, every middlebox between you and your users. QUIC lives in user space — you ship it with your app. That's how the IETF got something deployed in five years instead of fifteen.

**"Doesn't UDP get blocked by firewalls?"**
Some corporate firewalls do block UDP on non-DNS ports. That's exactly why fallback to HTTP/2 matters. In practice, Cloudflare reports HTTP/3 reaches around 75% of their traffic — the rest falls back cleanly.

**"What about server load? UDP and encryption per packet sound expensive."**
QUIC does cost more CPU than TCP — typically 2× for the same throughput today. But CPUs are cheaper than user-facing latency, and the gap is closing fast as kernel offloads and hardware acceleration arrive. For most apps, it's not the bottleneck.

**"Can I use QUIC for non-HTTP traffic?"**
Yes — that's exactly what `System.Net.Quic` is for. Pick your own ALPN identifier (anything except reserved ones like `h3`), and you have a multiplexed, encrypted, migrating transport for whatever protocol you want to invent. Multiplayer games are doing this already.

**"What about load balancers?"**
This is where production gets interesting. UDP load balancing is harder than TCP — you can't just look at five-tuples because connection IDs are designed to survive address changes. Most modern L7 balancers (Cloudflare, AWS, nginx 1.25+, HAProxy 2.6+) handle this; older infrastructure doesn't. Test before you trust.

**"Is .NET's QUIC implementation production-ready?"**
HTTP/3 in Kestrel went GA in .NET 7 and is stable. The raw `System.Net.Quic` namespace is also marked stable. Both are based on Microsoft's MsQuic library, which Microsoft ships in Windows Server. Yes, it's production-ready.

**"Does this work with gRPC?"**
Not yet officially. gRPC over HTTP/3 is in the gRPC spec but the .NET implementation isn't there as of .NET 9. For now, gRPC rides HTTP/2. Watch this space.

**"What about WebSocket vs QUIC streams?"**
Different layers. WebSocket is an upgrade from HTTP for bidirectional messaging at the application layer. QUIC streams are transport-layer. If you want full-duplex over HTTP/3, use WebTransport (still maturing) or open a `QuicConnection` directly. SignalR will eventually run on top of all of these.

---

## Things to remember on stage

- The deck has page numbers in the footer. If you lose your place, glance there and recover silently.
- Don't read code character-by-character. The audience reads faster than you talk.
- Pause after each big claim. *"One round trip — and zero on resume."* Then stop. Let it land.
- If you cite specific .NET version numbers, double-check on the day. .NET releases on a 12-month cadence and slides go stale fast.
- The chart on slide 21 says "illustrative" in small print. **Say it out loud.** If you don't, someone will quote your numbers as gospel in a Slack channel next week.
- Your job at the end isn't to demo every API in `System.Net.Quic`. It's to send the audience back to their desks knowing that HTTP/3 is one line in Kestrel and worth turning on.
