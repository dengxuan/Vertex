# Vertex wire format v1

> Status: **normative** (every language implementation MUST conform to this document)　|　Version: **1.0**　|　Last updated: 2026-04-23

This document defines the on-wire bytes Vertex sends and receives. Any conforming implementation — regardless of language — that obeys this spec can interoperate with any other conforming implementation. Everything above this layer (how messages are dispatched, how types are registered) is an implementation detail of each language library.

---

## 1. Layering

```
┌─────────────────────────────────────────────────────────┐
│ Application code: send events, invoke RPCs, handle RPCs │
├─────────────────────────────────────────────────────────┤
│ Messaging layer: topic routing, request/response pairing │  ← implementation detail per language
├─────────────────────────────────────────────────────────┤
│ Envelope (this document, §2)                            │  ← normative
├─────────────────────────────────────────────────────────┤
│ Serializer: Protobuf | MessagePack | ...                │  ← §3
├─────────────────────────────────────────────────────────┤
│ Transport: gRPC bidi stream | ZeroMQ | ...              │  ← §4, contract in transport-contract.md
└─────────────────────────────────────────────────────────┘
```

The wire format defines the **envelope** and its **framing** onto each supported transport. The payload encoding is layered on top (§3).

---

## 2. Envelope: 4 frames

Every Vertex message on the wire is exactly **4 frames** in this strict order:

| Index | Field | Encoding | Notes |
|---|---|---|---|
| **0** | `topic` | UTF-8 string, no NUL terminator | See §2.1 |
| **1** | `kind` | single byte | See §2.2 |
| **2** | `request_id` | UTF-8 string, no NUL terminator | See §2.3 |
| **3** | `payload` | opaque bytes | Serializer-defined, see §3 |

Frames are **opaque byte buffers** at this layer; the wrapping transport (§4) is responsible for preserving frame boundaries.

### 2.1 `topic` (frame 0)

A UTF-8 string that identifies the message type.

**Canonical form** (recommended for cross-language interop):
- If the payload type is generated from a `.proto` file, use the full name of the message as reported by the generated descriptor: e.g. `feivoo.gaming.v1.CreateRoom`.
- If the payload type is **not** proto-generated (e.g., a MessagePack POCO in .NET), use the language-native full type name; cross-language interop then requires out-of-band coordination to align topic strings.

**Error marking**: a response whose topic starts with the ASCII character `!` (0x21) indicates an error response. The receiver MUST strip the leading `!` before looking up the original topic. See §2.4.

**Constraints**:
- MUST NOT be empty (except for reserved frame 2; frame 0 is always non-empty)
- Recommended ≤ 255 bytes; implementations MAY impose their own limit
- No whitespace, no control characters (U+0000 – U+001F), no leading `!` unless it is an error-response marker

### 2.2 `kind` (frame 1)

A single byte enumerating the message kind:

| Value | Name | Semantics |
|---|---|---|
| `0x00` | `EVENT` | Fire-and-forget publish. `request_id` MUST be empty. No response expected. |
| `0x01` | `REQUEST` | RPC request. `request_id` MUST be present and non-empty. Receiver MUST reply with a `RESPONSE` of matching `request_id`. |
| `0x02` | `RESPONSE` | RPC reply. `request_id` MUST match the corresponding `REQUEST`. Topic MAY be prefixed with `!` to indicate error. |

Values `0x03` and above are reserved; receivers MUST drop any message with an unknown kind and MAY log a warning.

### 2.3 `request_id` (frame 2)

A UTF-8 string correlating a `REQUEST` with its `RESPONSE`.

- `EVENT`: MUST be the empty string (frame 2 is zero-length).
- `REQUEST`: MUST be a non-empty unique identifier. Recommended: 128-bit random (a UUID4 in hex, 32 chars, no hyphens) or similar. Uniqueness scope is per sender-per-peer; two peers independently sending request IDs that collide is NOT a violation.
- `RESPONSE`: MUST match exactly the `request_id` of the request being replied to.

Implementations MUST treat `request_id` as an opaque byte sequence (not interpret as numeric, etc.).

### 2.4 `payload` (frame 3) and error responses

For `EVENT` and `REQUEST`: `payload` is the serialized business message. See §3 for serializer rules.

For `RESPONSE`:
- **Success**: `payload` is the serialized response message (same serializer as the request).
- **Error**: `topic` (frame 0) starts with `!`, and `payload` is a UTF-8-encoded human-readable error message. (This is a deliberately simple scheme; structured error types can be added in wire format v2 if needed.)

### 2.5 Summary table

| Kind | Topic frame 0 | Kind frame 1 | RequestId frame 2 | Payload frame 3 |
|---|---|---|---|---|
| Event | `"feivoo.gaming.v1.GameStateChanged"` | `0x00` | `""` (empty) | serialized `GameStateChanged` |
| Request | `"feivoo.gaming.v1.CreateRoom"` | `0x01` | `"7f3c...a21"` | serialized `CreateRoom` |
| Success Response | `"feivoo.gaming.v1.CreateRoom"` | `0x02` | `"7f3c...a21"` | serialized `RoomCreated` |
| Error Response | `"!feivoo.gaming.v1.CreateRoom"` | `0x02` | `"7f3c...a21"` | UTF-8 error message |

---

## 3. Payload serialization

The payload (frame 3) is opaque bytes. The envelope does **not** encode which serializer produced these bytes — sender and receiver MUST agree out-of-band, typically via the transport binding:

| Transport | Serializer | Rationale |
|---|---|---|
| gRPC (Vertex.Transport.Grpc) | **Protobuf** (enforced) | Cross-language ecosystem; gRPC ships with Protobuf tooling |
| ZeroMQ (Vertex.Transport.NetMq) | **MessagePack** by default; Protobuf opt-in; any `IMessageSerializer` impl supported | Intra-cluster scenarios prefer MessagePack ergonomics; cross-language users opt into Protobuf |

A single Vertex cluster (= all peers sharing a messaging channel) MUST use one serializer consistently. Mixing serializers on the same channel is undefined behavior.

---

## 4. Transport framing

The envelope is 4 logical frames. Each transport MUST preserve these 4 frames atomically: either all 4 arrive at the receiver in order, or none do.

### 4.1 ZeroMQ (native multi-frame)

ZeroMQ natively supports multi-frame messages. The 4 envelope frames map **directly** to 4 ZMQ frames. No additional framing is needed.

### 4.2 gRPC bidi stream

gRPC streams one message at a time, so Vertex emulates multi-frame semantics by wrapping each envelope frame into a single Protobuf message with an end-of-message marker:

```proto
// Internal to the transport; business payloads are inside TransportFrame.payload
syntax = "proto3";
package vertex.transport.grpc.v1;

service Bidi {
  rpc Connect(stream TransportFrame) returns (stream TransportFrame);
}

message TransportFrame {
  bytes payload          = 1;
  bool  end_of_message   = 2;  // true on the 4th (last) frame of each Vertex envelope
}
```

Sender emits 4 `TransportFrame` messages per Vertex message; `end_of_message = true` is set only on the 4th. Receiver accumulates frames until `end_of_message`, then delivers them as one 4-frame envelope to the Vertex messaging layer.

### 4.3 Other transports (future)

Any transport that can atomically deliver a byte sequence can carry Vertex by either:

1. **Preserving native frames** (ZeroMQ-style), or
2. **Simulating** frame boundaries with an internal delimiter protocol (gRPC-style; RabbitMQ, HTTP, QUIC, etc. would pick one).

Transport implementations MUST document which strategy they chose and ensure atomic delivery of all 4 frames.

---

## 5. Versioning

This document describes **wire format v1**. Breaking changes (new mandatory frames, changed byte encodings) produce v2 and require coordinated rollout across all language implementations.

Non-breaking additions (new `kind` values, new topic naming conventions) MAY be made within v1 with appropriate feature-detection.

---

## 6. Conformance notes

A conforming implementation:

- MUST produce the exact byte sequences described above on the wire
- MUST correctly parse any byte sequence conforming to this document
- MUST implement the [transport contract](./transport-contract.md) for each transport it claims to support
- SHOULD provide interop tests against the reference `.NET` and `Go` implementations in [`/compat`](../compat/)

---

## Appendix A: example byte layout (ZeroMQ)

A single `CreateRoom` RPC request sent over ZeroMQ:

```
Frame 0 (topic):      "feivoo.gaming.v1.CreateRoom"                  (UTF-8, 27 bytes)
Frame 1 (kind):       [0x01]                                         (1 byte)
Frame 2 (request_id): "7f3c3a9e-1c04-4a2f-b8e2-2e8ea1b0a21c"         (UTF-8, 36 bytes)
Frame 3 (payload):    <Protobuf-serialized CreateRoom message>       (variable)
```

## Appendix B: example byte layout (gRPC)

Same request over gRPC bidi stream (4 `TransportFrame` messages):

```
TransportFrame #1:  payload = "feivoo.gaming.v1.CreateRoom" (UTF-8),  end_of_message = false
TransportFrame #2:  payload = [0x01],                                 end_of_message = false
TransportFrame #3:  payload = "7f3c3a9e-1c04-4a2f-...",               end_of_message = false
TransportFrame #4:  payload = <serialized CreateRoom>,                end_of_message = true
```
