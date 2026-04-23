# Getting started — .NET

> 🚧 This guide is placeholder until `Vertex.Dotnet.*` NuGet packages are published. Pointers below reflect planned API.

## 1. Install

```bash
dotnet add package Vertex.Dotnet.Messaging
dotnet add package Vertex.Dotnet.Transport.Grpc       # cross-language scenarios
# or
dotnet add package Vertex.Dotnet.Transport.NetMq      # intra-cluster scenarios
```

## 2. Define your business messages

For cross-language (Vertex.Transport.Grpc), define in `.proto`:

```proto
// protos/gaming.proto
syntax = "proto3";
package gaming.v1;

message CreateRoom { string room_name = 1; }
message RoomCreated { string room_id = 1; }
```

`Grpc.Tools` picks up the `.proto` via `<Protobuf Include="protos/gaming.proto" />` in your csproj.

For .NET-only (Vertex.Transport.NetMq + MessagePack), decorate POCOs:

```csharp
[MessagePackObject]
public class CreateRoom { [Key(0)] public string RoomName { get; set; } = ""; }
```

## 3. Wire up

```csharp
var builder = WebApplication.CreateBuilder(args);

// Cross-language path
builder.Services.AddVertexGrpcTransport("main", o =>
{
    o.ServerAddress = new Uri("https://api.example.com");
});

// Register message types
builder.Services.AddVertexMessaging("main", reg =>
{
    reg.RegisterEvent<gaming.v1.GameStateChanged>();
    reg.RegisterRequest<gaming.v1.CreateRoom, gaming.v1.RoomCreated>();
});

var app = builder.Build();
app.Run();
```

## 4. Use

```csharp
public class RoomService
{
    private readonly IRpcClient _rpc;
    public RoomService(IRpcClient rpc) => _rpc = rpc;

    public Task<gaming.v1.RoomCreated> CreateAsync(string name, CancellationToken ct) =>
        _rpc.InvokeAsync<gaming.v1.CreateRoom, gaming.v1.RoomCreated>(
            new() { RoomName = name }, cancellationToken: ct).AsTask();
}
```

## Server-side RPC handler

```csharp
public class CreateRoomHandler : IRpcHandler<gaming.v1.CreateRoom, gaming.v1.RoomCreated>
{
    public ValueTask<gaming.v1.RoomCreated> HandleAsync(gaming.v1.CreateRoom req, RpcContext ctx, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        return ValueTask.FromResult(new gaming.v1.RoomCreated { RoomId = id });
    }
}

// in Startup:
builder.Services.AddScoped<IRpcHandler<gaming.v1.CreateRoom, gaming.v1.RoomCreated>, CreateRoomHandler>();
```

## Next steps

- [Wire format spec](../spec/wire-format.md) — learn what goes on the wire
- [Transport contract](../spec/transport-contract.md) — if you plan to write a custom transport
- Go companion: [`getting-started-go.md`](./getting-started-go.md)
