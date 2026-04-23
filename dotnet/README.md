# Vertex .NET

The .NET implementation of the Vertex messaging kernel.

## Status: 🚧 migrating from Skywalker

The code is being ported from [`Skywalker.Messaging.*`](https://github.com/dengxuan/Skywalker/tree/main/src/Skywalker.Messaging) and [`Skywalker.Transport.*`](https://github.com/dengxuan/Skywalker/tree/main/src/Skywalker.Transport.Grpc). Tracking: Skywalker [Spin-out design doc](https://github.com/dengxuan/Skywalker/blob/main/docs/architecture/messaging-spin-out.md).

## Planned package layout

| NuGet package | Role |
|---|---|
| `Vertex.Dotnet.Serialization.Abstractions` | `IMessageSerializer` interface |
| `Vertex.Dotnet.Serialization.Protobuf` | `ProtobufMessageSerializer` (required for gRPC) |
| `Vertex.Dotnet.Serialization.MessagePack` | `MessagePackMessageSerializer` (default for ZeroMQ) |
| `Vertex.Dotnet.Transport.Abstractions` | `ITransport`, peer/frame primitives, 4-invariant contract |
| `Vertex.Dotnet.Transport.NetMq` | ZeroMQ transport; user-supplied serializer |
| `Vertex.Dotnet.Transport.Grpc` | gRPC transport (client + server); Protobuf enforced |
| `Vertex.Dotnet.Messaging.Abstractions` | `IMessageBus`, `IRpcClient`, `IRpcHandler<,>` |
| `Vertex.Dotnet.Messaging` | `MessagingChannel` |

Target framework: **net8.0** initially (may add net10 later when Skywalker does).

## Quickstart (planned, not functional yet)

```csharp
// cross-language client (gRPC + Protobuf enforced)
services.AddVertexGrpcTransport("feivoo", o =>
{
    o.ServerAddress = new Uri("https://api.feivoo.com");
    o.Metadata.Add(new("authorization", $"Bearer {apiKey}"));
});

services.AddVertexMessaging("feivoo", reg =>
{
    reg.RegisterEvent<GameStateChanged>();
    reg.RegisterRequest<CreateRoom, RoomCreated>();
});
```

```csharp
// intra-cluster, .NET-only (ZeroMQ + MessagePack default)
services.AddVertexNetMqTransport("internal", o =>
{
    o.BindEndpoint = "tcp://*:5555";
    // o.Serializer defaults to MessagePackMessageSerializer
});
```

## Building (once code lands)

```bash
dotnet restore Vertex.Dotnet.sln
dotnet test
dotnet pack
```

## Contributing

See the monorepo [`../CONTRIBUTING.md`](../CONTRIBUTING.md).
