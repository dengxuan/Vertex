# Vertex

> Lightweight, cross-language bidi messaging kernel. **Zero broker, language-neutral wire format, 4 invariants that protocol implementations MUST satisfy.**

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## What is Vertex?

Vertex is a small cross-language library for **bidirectional message passing** between services. It solves the "every team rewrites their own gRPC bidi stream dispatcher" problem by shipping a **language-neutral wire spec** plus reference implementations in multiple languages.

Pitch:

- **Language-neutral by design** — the wire format is documented in [`/spec/wire-format.md`](./spec/wire-format.md), not hidden inside a .NET library.
- **Pluggable transport** — gRPC (for cross-language, over the internet) and ZeroMQ (for intra-cluster, mDNS discovery) bundled; add your own by implementing the transport contract.
- **Pluggable serializer** — Protobuf (forced for gRPC), MessagePack (default for ZeroMQ), or bring your own `IMessageSerializer`.
- **4 transport invariants** — see [`/spec/transport-contract.md`](./spec/transport-contract.md). These are non-negotiable rules every transport implementation must enforce. They are distilled from production incidents where bidi streams had head-of-line blocking, over-broad cancellation, and false-positive disconnect detection.
- **Small surface** — `IMessageBus` (publish/subscribe), `IRpcClient` (request/response), `IRpcHandler<TReq, TRes>` (server-side RPC). That's it.

### Why not just use gRPC directly?

Raw gRPC gives you a bidi stream and a `.proto` schema, but leaves you to write — **and re-write, and fix** — the dispatch logic: how to route replies, how to handle concurrent in-flight requests on one stream, when to tear down vs recover, how to avoid the cancel-token-kills-the-stream bug, etc. Vertex encapsulates that dispatch layer once and ships it in every supported language.

### Why not NATS / ZeroMQ / Kafka?

- **NATS** is a broker — requires running a server, has its own operational footprint. Vertex is broker-less.
- **ZeroMQ** gives you sockets, not messaging semantics. Vertex provides `IMessageBus` / `IRpcClient` abstractions on top.
- **Kafka** is a log, not an RPC system.

Vertex's closest analogue is **"NATS-style API over gRPC/ZMQ sockets, language-neutral"**.

## Status

| Language | Status | Package |
|---|---|---|
| **.NET** (8.0+) | 🚧 bootstrapping (migrating from Skywalker) | `Vertex.Dotnet.*` |
| **Go** (1.22+) | 🚧 planning | `github.com/dengxuan/vertex/go` |
| **PHP** | ⏸ future | — |
| Rust / Python / ... | ⏸ future, contributions welcome | — |

## Repository layout

```
vertex/
├── spec/                    ← Language-neutral specs (authoritative)
│   ├── wire-format.md       ← 4-frame envelope on the wire
│   └── transport-contract.md ← The 4 transport invariants
├── protos/                  ← Shared .proto files (for Protobuf payloads, future envelope proto)
│   └── vertex/v1/
├── dotnet/                  ← Vertex.Dotnet.* implementation
│   ├── src/
│   └── tests/
├── go/                      ← Vertex Go implementation
├── compat/                  ← Cross-language end-to-end tests (.NET ↔ Go ↔ ...)
├── docs/                    ← Tutorials, guides (not spec)
└── .github/workflows/       ← Per-language + compat CI
```

## Quickstart

> Code is landing in the coming weeks. Spec, roadmap, and contribution guide are ready today.

- **.NET**: see [`dotnet/README.md`](./dotnet/README.md) — migration from `Skywalker.Messaging.*` in progress
- **Go**: see [`go/README.md`](./go/README.md) — implementation in progress, follows the wire spec 1:1

## Origin

Vertex is spun out of [Skywalker](https://github.com/dengxuan/Skywalker) (a .NET DDD framework), where `Skywalker.Messaging.*` and `Skywalker.Transport.*` incubated. After shipping `Skywalker.Transport.Grpc` with .NET client+server, it became clear that cross-language messaging doesn't belong inside a single-language DDD framework. Vertex is that messaging layer pulled out, ready to grow peer language implementations.

See [Skywalker's spin-out design doc](https://github.com/dengxuan/Skywalker/blob/main/docs/architecture/messaging-spin-out.md).

## Contributing

See [`CONTRIBUTING.md`](./CONTRIBUTING.md). Wire-spec changes need cross-language review; per-language changes can be merged independently if they don't break the wire contract.

## License

MIT — see [`LICENSE`](./LICENSE).
