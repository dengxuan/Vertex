# Vertex peer authentication

> Status: **normative**　|　Version: **1.0**　|　Last updated: 2026-04-26

This document defines how Vertex transports MUST surface connection-level
authentication metadata to the messaging layer, and how language
implementations MUST expose the authenticator hook + per-peer state to
application code.

The motivation is concrete: pre-1.x Vertex consumers often reimplemented
"the first business message MUST be a Login RPC" inside their messaging
layer, with brittle "are-you-logged-in" checks scattered across every
RPC handler. That pattern shadows what every transport already supports
natively (gRPC initial metadata, AMQP connection properties, MQTT CONNECT
username/password, etc.). This spec promotes it from a per-app
convention to a Vertex contract.

---

## 1. Goals

1. **Authenticate at connection establishment**, before the first
   business `kind=REQUEST` / `kind=EVENT` envelope is dispatched. A
   peer that fails authentication never sees an application handler.
2. **Reject unauthenticated peers at the transport layer**, not via an
   error response on a successfully-routed message. The transport-level
   error code (gRPC `UNAUTHENTICATED`, AMQP `403 ACCESS_REFUSED`, etc.)
   makes the failure machine-readable and short-circuits framework
   middleware that would otherwise log a fake 'session-bound' peer.
3. **Carry an opaque per-peer state** (e.g. `directorId`, tenant scope)
   from the authenticator down to every RPC handler that the peer
   triggers, **without** a per-request dictionary lookup keyed by
   `PeerId`.
4. **Stay transport-neutral**. The authenticator API talks in
   `string → string` metadata and an opaque `peer_state`; transports
   are responsible for getting the metadata onto / off the wire in
   their native idiom.

---

## 2. Wire mapping

### 2.1 gRPC

The authenticator metadata MUST be carried in **HTTP/2 request headers**
sent on the `Bidi.Connect` initial frame:

- Client: `ClientContext::AddMetadata("key", "value")` /
  `Metadata.Add("key", "value")` before invoking `Bidi.Connect`.
- Server: read via `ServerContext::client_metadata()` /
  `ServerCallContext.RequestHeaders` **before** calling `stream->Read`
  for the first time.

Recommended header keys (downstream implementations MAY restrict to a
subset but MUST NOT redefine these keys with conflicting semantics):

| Header | Purpose |
|---|---|
| `authorization` | Standard HTTP auth header (`Bearer <token>` /  `Basic <b64>`). Preferred when fronting a token-issuing IdP. |
| `x-vertex-peer-token` | Vertex-issued opaque token (when the IdP is Vertex itself, e.g. a single-monolith deployment). |
| `x-vertex-peer-username` / `x-vertex-peer-password` | Plain credentials. Only over TLS. Discouraged in production; use `authorization`. |

Custom headers are allowed; see §4.

### 2.2 ZeroMQ

ZMQ has [ZAP](https://rfc.zeromq.org/spec/27/) for authentication, with
PLAIN / CURVE / GSSAPI mechanisms. The Vertex layer MUST surface ZAP
credentials (`User-Id` / properties) via the same authenticator
interface. Implementations MAY map ZAP properties → metadata 1:1 by
property name.

### 2.3 Other transports

Future transport implementations MUST document how their native
authentication surface (e.g. AMQP `AMQPLAIN` SASL, MQTT 3 `username` /
`password`, WebSocket `Sec-WebSocket-Protocol`) maps to the
authenticator metadata dictionary.

---

## 3. Authenticator contract

Every server-side Vertex transport MUST accept a single optional
`PeerAuthenticator` at construction. When present, the transport MUST
invoke it exactly once per accepted peer connection, **before** any
inbound message from that peer is enqueued.

```
PeerAuthenticator(PeerAuthenticationContext) → PeerAuthenticationResult

PeerAuthenticationContext:
  peer:     PeerId          // transport-assigned
  metadata: Map<String,String>  // case-insensitive key, see §2

PeerAuthenticationResult:
  // exactly one of:
  Accept(peer_state: Object|null)
  Reject(reason: String)
```

Behavior:

- `Accept(peer_state)` — peer is admitted. `peer_state` is opaque to
  Vertex; the messaging layer MUST attach it to every `RpcContext` /
  `EventContext` produced from this peer's traffic, retrievable via
  language-idiomatic accessors (e.g. `RpcContext.PeerState` /
  `EventContext.PeerState`). When the peer disconnects, the
  implementation SHOULD release any references it holds to
  `peer_state`.
- `Reject(reason)` — peer is denied. The transport MUST close the
  connection with the transport-native "unauthenticated" code (gRPC
  `UNAUTHENTICATED`, AMQP `403 ACCESS_REFUSED`, etc.) and SHOULD
  surface `reason` as the trailing status message when the transport
  supports it. The transport MUST NOT enqueue any inbound message
  from the rejected peer to the messaging layer.
- **No authenticator configured** — peers are admitted unconditionally
  with `peer_state = null`. This is the v1.0 / unauthenticated default
  for all transports, preserving the original behavior of pre-spec
  consumers.

Threading: the transport MAY invoke the authenticator on its own read
loop or on a dedicated worker. The authenticator implementation MUST be
re-entrant safe; multiple authenticator invocations may happen in
parallel for different peers.

Cancellation: the authenticator MAY be passed a transport-lifetime
cancellation token. It MUST NOT be passed a per-peer cancellation token
that fires on the very peer being authenticated (would be a chicken-and-egg).

---

## 4. Metadata key conventions

Keys are **case-insensitive ASCII**. Implementations MUST normalize to
lowercase before delivering to the authenticator.

The following key prefixes are reserved for Vertex itself:

- `x-vertex-*` — Vertex framework metadata (token, version, language).
  Application code SHOULD NOT redefine these.
- `authorization` — standard HTTP auth, allowed wherever the transport
  speaks HTTP semantics (gRPC, future REST/SSE).

All other keys are application-defined.

Values are UTF-8 strings. Binary data MUST be base64-encoded by the
application; transports MUST NOT silently base64-encode on its behalf
(would defeat case-insensitive routing).

---

## 5. Peer state lifecycle

The `peer_state` returned by `Accept(...)` follows the **peer-connection
lifetime**, NOT individual message lifetimes:

- Set: at the authenticator's `Accept` return.
- Read: by every RPC handler / event handler invocation triggered by
  this peer's messages. Reads are race-free because no message is
  dispatched before `Accept` completes.
- Disposed: when the peer connection ends (graceful close, transport-
  level error, or server shutdown). Implementations MAY hold weak
  references; user code MUST NOT rely on `peer_state` outliving the
  peer connection.

Implementations MAY surface a per-peer "on disconnect" hook that
receives the same `peer_state`, for users who need to release external
resources (e.g. close a DB session, decrement a per-tenant connection
count).

---

## 6. Conformance notes

A conforming server-side transport implementation:

- MUST accept a `PeerAuthenticator` at construction (nullable).
- MUST invoke it exactly once per peer, before any inbound message is
  enqueued.
- MUST close the connection with the transport-native "unauthenticated"
  status code when the authenticator returns `Reject(...)`, including
  the reason string in the status message when supported.
- MUST attach `peer_state` to every `RpcContext` / `EventContext` for
  messages from the authenticated peer.
- MUST normalize metadata keys to lowercase before delivering to the
  authenticator.
- SHOULD release `peer_state` references on peer disconnect.

A conforming client-side transport implementation:

- MUST accept an optional `metadata` map at construction (or at
  per-call connect, for transports that support reconnection with
  fresh credentials).
- MUST send all entries verbatim (after lowercase-normalizing keys)
  on the connection-establishment frame in the transport-native idiom
  (gRPC: HTTP/2 request headers; ZMQ: ZAP frame; etc.).

---

## Appendix A: example flow (gRPC)

```
1. Client construct:
     transport = GrpcTransport(name="director", options={
       server_address: "https://api.example.com",
       metadata: { "authorization": "Bearer eyJhbGc..." },
     })

2. Server construct:
     transport = GrpcServerTransport(name="director", options={
       bind_address: "0.0.0.0:50051",
       authenticator: ctx => {
         var token = ctx.metadata["authorization"]?.RemoveBearerPrefix();
         var session = await jwtValidator.Validate(token);
         return session != null
           ? Accept(peer_state: session)
           : Reject("invalid or expired token");
       },
     })

3. On the wire:
     Client -> Server (HTTP/2 :path /vertex.transport.grpc.v1.Bidi/Connect
                       headers: { authorization: "Bearer eyJhbGc..." })
     Server reads ServerContext.client_metadata()
     Server invokes authenticator → Accept(peer_state=Session{director_id="d1"})
     Server admits the peer; subsequent messages dispatch with
       RpcContext.PeerState == Session{director_id="d1"}.

4. RPC handler:
     async ValueTask<X> StartGameHandler.HandleAsync(RpcContext<StartGameRequest> ctx, _) {
         var session = (Session)ctx.PeerState;
         var providerId = session.ProviderId;
         // No "is logged in?" check needed — peer never reached here without authentication.
         return await lotteryClient.StartGameAsync(providerId);
     }
```

---

## Appendix B: relationship to existing invariants

The four [transport-contract.md](./transport-contract.md) invariants
(`#1` read-loop-only routing, `#2` send failures don't disconnect,
`#3` cancellation is pre-wire only, `#4` read loop is the sole
disconnect source) are unchanged. Specifically:

- The authenticator runs on the **same thread / task that opens the
  read loop**, before the loop starts processing inbound messages. It
  is NOT subject to invariant `#1` because it does not run "in" the
  read loop — it runs "before" it.
- An authenticator that throws is treated as `Reject(ex.Message)` —
  the resulting connection close is a normal lifecycle event, not a
  disconnect-during-send (so invariant `#2` is not violated).
- The peer connection close caused by `Reject` is a transport-level
  signal that the read loop never started; it does not produce a
  `PeerConnectionChanged(Disconnected)` event because there was no
  preceding `Connected` event (consistent with invariant `#4`'s rule
  that disconnect comes from the read loop).
