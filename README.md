# Vertex

> Lightweight, cross-language bidi messaging kernel. **Zero broker, language-neutral wire format, 4 invariants that every transport implementation MUST satisfy.**

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## What is Vertex?

Vertex is a small cross-language library for **bidirectional message passing** between services. It solves the "every team rewrites their own gRPC bidi stream dispatcher" problem by shipping a **language-neutral wire spec** plus reference implementations in multiple languages.

Pitch:

- **Language-neutral by design** — the wire format is documented in [`/spec/wire-format.md`](./spec/wire-format.md), not hidden inside any single-language library.
- **Pluggable transport** — gRPC (for cross-language, over the internet) and ZeroMQ (for intra-cluster, mDNS discovery) bundled; add your own by implementing the transport contract.
- **Pluggable serializer** — Protobuf (forced for gRPC), MessagePack (default for ZeroMQ), or bring your own `IMessageSerializer`.
- **4 transport invariants** — see [`/spec/transport-contract.md`](./spec/transport-contract.md). These are non-negotiable rules every transport implementation must enforce. Distilled from production incidents where bidi streams had head-of-line blocking, over-broad cancellation, and false-positive disconnect detection.
- **Small surface** — `IMessageBus` (publish/subscribe), `IRpcClient` (request/response), `IRpcHandler<TReq, TRes>` (server-side RPC). That's it.

### Why not just use gRPC directly?

Raw gRPC gives you a bidi stream and a `.proto` schema, but leaves you to write — **and re-write, and fix** — the dispatch logic: how to route replies, how to handle concurrent in-flight requests on one stream, when to tear down vs recover, how to avoid the cancel-token-kills-the-stream bug, etc. Vertex encapsulates that dispatch layer once and ships it in every supported language.

### Why not NATS / ZeroMQ / Kafka?

- **NATS** is a broker — requires running a server, has its own operational footprint. Vertex is broker-less.
- **ZeroMQ** gives you sockets, not messaging semantics. Vertex provides `IMessageBus` / `IRpcClient` abstractions on top.
- **Kafka** is a log, not an RPC system.

Vertex's closest analogue is **"NATS-style API over gRPC/ZMQ sockets, language-neutral"**.

## This repository

**This repo is the canonical spec.** Every language implementation follows the documents under [`/spec/`](./spec). Language implementations live in separate repositories (one per language), similar to how gRPC is organized (`grpc/grpc` spec vs `grpc/grpc-go`, `grpc/grpc-dotnet`, etc.).

## Implementations

| Language | Repository | Package / module | Status |
|---|---|---|---|
| **.NET** (8.0+) | [dengxuan/vertex-dotnet](https://github.com/dengxuan/vertex-dotnet) | `Vertex.Messaging`, `Vertex.Transport.Grpc`, … | 🚧 bootstrapping |
| **Go** (1.22+) | [dengxuan/vertex-go](https://github.com/dengxuan/vertex-go) | `github.com/dengxuan/vertex-go/messaging`, … | 🚧 planning |
| **PHP** | — | — | ⏸ future |
| Rust / Python / ... | — | — | ⏸ future, contributions welcome |

## Repository layout

```
Vertex/                      ← you are here (spec repository)
├── spec/                    ← normative language-neutral documents
│   ├── wire-format.md
│   └── transport-contract.md
├── protos/                  ← shared .proto files (referenced by every impl)
│   └── vertex/v1/
├── compat/                  ← cross-language end-to-end tests (clones impl repos)
└── docs/                    ← project-level docs (governance, release policy, etc.)
```

## Quickstart

You don't use this repo directly from application code — go to the language repo:

- **.NET**: [dengxuan/vertex-dotnet#getting-started](https://github.com/dengxuan/vertex-dotnet#getting-started)
- **Go**: [dengxuan/vertex-go#getting-started](https://github.com/dengxuan/vertex-go#getting-started)

Come back **here** if you are:

- Implementing a new language binding (read `/spec/` first)
- Contributing a wire-spec change (see [`CONTRIBUTING.md`](./CONTRIBUTING.md))
- Running / extending cross-language compatibility tests (see [`/compat/`](./compat))

## Origin

Vertex is spun out of [Skywalker](https://github.com/dengxuan/Skywalker) (a .NET DDD framework), where `Skywalker.Messaging.*` and `Skywalker.Transport.*` incubated. After shipping `Skywalker.Transport.Grpc` with .NET client+server, it became clear that cross-language messaging doesn't belong inside a single-language DDD framework. Vertex is that messaging layer pulled out — as a polyglot polyrepo so every language can be a first-class citizen.

See [Skywalker's spin-out design doc](https://github.com/dengxuan/Skywalker/blob/main/docs/architecture/messaging-spin-out.md).

## Governance

Wire-spec changes in this repo require review from at least one maintainer per shipped language implementation. See [`CONTRIBUTING.md`](./CONTRIBUTING.md).

## License

MIT — see [`LICENSE`](./LICENSE).
