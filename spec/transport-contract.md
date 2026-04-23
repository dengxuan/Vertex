# Vertex transport contract

> Status: **normative**　|　Version: **1.0**　|　Last updated: 2026-04-23

This document defines the contract every Vertex transport implementation MUST satisfy. It is **language-neutral**: the rules apply identically to .NET, Go, PHP, or any future implementation.

Audience:

1. People implementing a new transport (gRPC, QUIC, WebSocket, nanomsg, raw TCP, ...) in any language.
2. Maintainers debugging unexpected transport behavior — cross-check against this contract first.

---

## The four invariants

These four rules are distilled from real production incidents. Every one of them has been violated at least once in practice; none of them may be re-violated. Call them **铁律** (iron laws) — they are non-negotiable.

### Invariant #1 — Read loop only does lightweight non-blocking routing

The **read loop** (the loop that pulls messages off the underlying socket / stream and hands them to the messaging layer) MUST NOT invoke any user-provided handler synchronously on its own thread / goroutine / task. Its job is limited to:

- Push inbound messages into a queue / channel for the messaging layer to consume asynchronously, OR
- At most inline-handle "ack" style messages that do not call user code and do not wait on I/O.

If the read loop awaits a user handler, a single slow handler creates **head-of-line blocking**: every subsequent inbound message — including ACKs needed to unblock the slow one — is stuck behind it.

> **Production incident**: an earlier `gaming-go-sdk` called `dispatchHandler(msg)` synchronously in its `readLoop`. A single 5-second RPC handler stalled all subsequent inbound messages, tripped a timeout, and caused a false-positive disconnect judgment.
>
> **Fix**: `go c.dispatchHandler(ctx, message)` in Go (a new goroutine per message), or `_ = DispatchAsync(message, ct)` in .NET.

### Invariant #2 — A single message send/handler failure is NOT a disconnect

A single reply failing to send (peer refused, serialization error, per-message timeout) MUST fail **only that one message**, and MUST NOT:

- Raise a disconnect / peer-connection-lost event
- Close the inbound queue / channel
- Cancel the read loop
- Mark the entire peer as unreachable

The only legitimate disconnect judgment comes from the underlying socket / stream throwing a transport-fatal error (I/O error, TLS handshake failure, HTTP/2 GOAWAY, etc.) — and even then, per invariant #4, only from a specific code location.

> **Production incident**: an earlier `gaming-go-sdk` version closed the entire bidi stream if a user's `build()` function returned an error while processing a reverse request. One merchant's business exception flipped **all** in-flight requests to that merchant into failure at once.
>
> **Fix**: `handleReverseOrder` now sends a per-message failure envelope and leaves the stream untouched.

### Invariant #3 — Cancellation is for pre-wire only; once writing has started, it MUST be ignored

`send(target, frames, cancellation)` MUST honor cancellation **only** in the time window between call entry and the first byte hitting the wire. Valid cancellation points include:

- Waiting for the write lock (`mutex.Lock(ctx)` / `SemaphoreSlim.WaitAsync(ct)`)
- Waiting for connection establishment
- Waiting for backpressure (a full outbound queue)

Once the implementation has written **any** byte of the envelope to the stream / socket, cancellation MUST be ignored until all frames of that envelope are flushed (or the write fails with a transport-level error).

Why: HTTP/2 treats a mid-stream cancellation as `RST_STREAM`, which terminates the ENTIRE bidi stream — tearing down every in-flight request multiplexed on it, not just the cancelled one.

> **Production incident**: an earlier `gaming-dotnet-sdk` passed the caller's cancellation token straight into `RequestStream.WriteAsync(message, ct)`. One caller's timeout → one RST_STREAM → all N concurrent requests on the same stream torn down. Observed: 10 concurrent requests, one timing out, **all 10** failing.
>
> **Fix**: `_writeLock.WaitAsync(ct)` is cancellable; the subsequent `RequestStream.WriteAsync(message)` explicitly drops `ct`.

Transports whose "send" is really "enqueue" (ZeroMQ: writing = `NetMQQueue.Enqueue`) satisfy this invariant for free — there is no mid-wire state to interrupt. Any **true streaming** transport (gRPC, QUIC, TCP, WebSocket) must enforce it explicitly.

### Invariant #4 — The read loop is the SOLE source of truth for peer-connection state

The judgment "this peer is disconnected" / "this connection is dead" has **exactly one legitimate origin**: an error from the read loop's `read()` / `MoveNext()` / `receive()` call.

Explicitly forbidden:
- `send()` failing → triggering disconnect (violates invariant #2)
- A user handler throwing → triggering disconnect (violates invariant #2)
- A separate heartbeat timeout having its own disconnect path (redesign: heartbeat should surface as a read-loop read timeout, not a parallel source of truth)
- The transport layer emitting `Disconnected` from the send path when the socket is cleanly closing (should be caught on the read side)

Why: multi-source disconnect judgment inevitably produces misjudgments and deadlocks. Path A declares dead, closes the stream; path B is mid-write, throws, and declares dead again → duplicated `Disconnected` events, double-release of resources, phantom state.

> **Production incident**: earlier `gaming-go-sdk` had `isTransientStreamError(nil)` returning `true`. A clean EOF from the server (signaled as `nil` error in Go) was classified as a transient error, triggering infinite reconnect loops.
>
> **Fix**: every "is this a disconnect" decision is centralized in the read loop, and `nil` / EOF is explicitly NOT transient.

---

## Implementation checklist

When writing a new transport, verify each item **before merging**:

- [ ] Transport exposes a stable name (constructor parameter, read-only after construction).
- [ ] `send` satisfies **invariant #3**: cancellation active only during resource acquisition; ignored once bytes start flowing.
- [ ] `send` failures surface only as exceptions / errors to the caller; they do NOT raise peer-connection events and do NOT close the inbound queue (**invariant #2**).
- [ ] `receive` yields a single-consumer async stream / channel / iterator of inbound messages.
- [ ] Read loop pushes each inbound message into the channel and **immediately** returns to read the next — no user-code invocation on the read loop thread (**invariant #1**).
- [ ] Peer-connection-state events are emitted **exclusively** from the read-loop's error path or from underlying connection-lifecycle callbacks. Never from the send path (**invariant #4**).
- [ ] Resource cleanup on dispose: close the inbound channel writer, stop the read loop, release the underlying socket / stream / poller.
- [ ] Unit tests cover:
  - Cancel mid-write → write completes, connection stays up.
  - Single `send` exception → `PeerConnectionChanged` NOT raised; subsequent `send` still works.
  - Server closes stream → `PeerConnectionChanged(Disconnected)` fires **exactly once**.
  - Multi-frame messages arrive in order, no interleaving across concurrent messages.

---

## Known reference implementations

| Transport | Status | Languages | Satisfies invariants via |
|---|---|---|---|
| ZeroMQ (Pub/Sub + Router/Dealer) | 🚧 porting from Skywalker | .NET | Native multi-frame; `NetMQQueue` enqueue is atomic; events from `NetMQMonitor` only |
| gRPC bidi | 🚧 porting from Skywalker | .NET | Explicit `_writeLock.WaitAsync(ct)` + non-cancellable `WriteAsync` body |
| gRPC bidi | ⏳ planned | Go | TBD — will mirror .NET strategy |

---

## History / provenance

These four invariants were originally written up in [Skywalker's `docs/modules/transport.md`](https://github.com/dengxuan/Skywalker/blob/main/docs/modules/transport.md) while incubating `Skywalker.Transport.Grpc`. When Vertex spun out, this document became the language-neutral canonical version; the Skywalker copy now cross-references this one.
