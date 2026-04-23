# Vertex Go

The Go implementation of the Vertex messaging kernel.

## Status: 🚧 planning

Module path: `github.com/dengxuan/vertex/go` (to be created once initial skeleton lands).

Go 1.22+.

## Planned package layout

```
go/
├── go.mod
├── messaging/                       ← IMessageBus, IRpcClient equivalents
├── serialization/
│   ├── protobuf/                    ← required for gRPC transport
│   └── msgpack/                     ← optional for ZeroMQ transport
├── transport/
│   ├── contract.go                  ← ITransport interface + 4 invariants enforcement
│   ├── grpc/                        ← gRPC bidi transport; Protobuf enforced
│   └── netmq/                       ← ZeroMQ transport (via gomq or similar)
└── protocol/
    └── v1/                          ← protoc-generated code from /protos/vertex/v1/
```

## Quickstart (planned, not functional yet)

```go
import (
    "github.com/dengxuan/vertex/go/messaging"
    "github.com/dengxuan/vertex/go/transport/grpc"
)

transport, err := grpc.Dial("https://api.feivoo.com",
    grpc.WithBearerToken(apiKey),
)
if err != nil { /* handle */ }

channel := messaging.NewChannel(transport,
    messaging.RegisterEvent(&pb.GameStateChanged{}),
    messaging.RegisterRequest(&pb.CreateRoom{}, &pb.RoomCreated{}),
)

resp, err := messaging.Invoke[*pb.CreateRoom, *pb.RoomCreated](ctx, channel, &pb.CreateRoom{RoomName: "lobby"})
```

## Development

```bash
cd go
go mod download
go build ./...
go test ./...
```

## Interop with .NET

The Go implementation is the second reference implementation (alongside .NET) for wire spec v1. Every test in [`/compat/`](../compat) runs .NET ↔ Go end-to-end to catch interop drift early.

## Contributing

See the monorepo [`../CONTRIBUTING.md`](../CONTRIBUTING.md).
