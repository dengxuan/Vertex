# Getting started — Go

> 🚧 This guide is placeholder until `github.com/dengxuan/vertex/go` is published. Pointers below reflect planned API.

## 1. Install

```bash
go get github.com/dengxuan/vertex/go/messaging
go get github.com/dengxuan/vertex/go/transport/grpc
```

## 2. Define messages in `.proto`

```proto
// protos/gaming.proto
syntax = "proto3";
package gaming.v1;
option go_package = "myapp/gen/gaming/v1;gamingv1";

message CreateRoom { string room_name = 1; }
message RoomCreated { string room_id = 1; }
```

Generate:

```bash
protoc --go_out=gen --go_opt=paths=source_relative protos/gaming.proto
```

## 3. Wire up

```go
package main

import (
    "context"
    "github.com/dengxuan/vertex/go/messaging"
    "github.com/dengxuan/vertex/go/transport/grpc"
    gamingv1 "myapp/gen/gaming/v1"
)

func main() {
    ctx := context.Background()

    transport, err := grpc.Dial("https://api.example.com",
        grpc.WithBearerToken("my-api-key"),
    )
    if err != nil { panic(err) }
    defer transport.Close()

    channel := messaging.NewChannel(transport,
        messaging.RegisterEvent[*gamingv1.GameStateChanged](),
        messaging.RegisterRequest[*gamingv1.CreateRoom, *gamingv1.RoomCreated](),
    )

    resp, err := messaging.Invoke[*gamingv1.CreateRoom, *gamingv1.RoomCreated](
        ctx, channel, &gamingv1.CreateRoom{RoomName: "lobby"})
    if err != nil { panic(err) }
    println(resp.RoomId)
}
```

## Server-side RPC handler

```go
channel.RegisterHandler(
    messaging.Handler[*gamingv1.CreateRoom, *gamingv1.RoomCreated](
        func(ctx context.Context, req *gamingv1.CreateRoom) (*gamingv1.RoomCreated, error) {
            id := uuid.NewString()
            return &gamingv1.RoomCreated{RoomId: id}, nil
        }),
)
```

## Next steps

- [Wire format spec](../spec/wire-format.md) — same wire as .NET
- [Transport contract](../spec/transport-contract.md) — Go transport impls MUST satisfy all 4 invariants
- .NET companion: [`getting-started-dotnet.md`](./getting-started-dotnet.md)
