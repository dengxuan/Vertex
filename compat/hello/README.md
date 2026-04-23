# compat/hello — first cross-language scenario

**Goal**: prove that a Go client speaking the [Vertex wire format](../../spec/wire-format.md) correctly interoperates with a .NET server built on [vertex-dotnet](https://github.com/dengxuan/vertex-dotnet).

**Shape**: one-way Publish (`KindEvent`). Simpler than RPC; exercises the full envelope encode → gRPC frame chunking → gRPC frame reassembly → envelope decode → MessagingChannel dispatch pipeline end to end.

## What it proves

- **Proto package alignment** — Go side generates code from `hello.proto` with `go_package` producing topic `vertex.compat.hello.v1.HelloEvent`; .NET side generates the C# type whose descriptor FullName is the same string. `MessageTopic` on both sides resolves to the identical topic, routing works.
- **4-frame envelope** — Go constructs `[topic, kind, request_id, payload]` per `/spec/wire-format.md § 2`; .NET `WireFormat.Decode` unpacks it; no custom per-language adapters needed.
- **gRPC TransportFrame framing** — Go splits one envelope into 4 `TransportFrame` messages with `end_of_message=true` on the last (`/spec/wire-format.md § 4.2`); .NET's `GrpcServerTransport` reassembles.
- **Protobuf payload** — both sides use `proto.Marshal` / `proto.Unmarshal` through their respective `IMessageSerializer` / serializer on the same `HelloEvent`.

## Prerequisites

Clone all three Vertex repos as siblings:

```
your-workspace/
├── Vertex/         ← you are here
├── vertex-dotnet/
└── vertex-go/
```

Tooling:

- .NET SDK 8.0+
- Go 1.22+

## Run

```bash
./run.sh
```

Expected output:

```
→ building .NET server
→ starting .NET server on :50051
→ server ready
→ running Go client
client: published HelloEvent{greeting="hello from go"}
server: PASS — received HelloEvent{greeting="hello from go"}
✓ compat/hello PASS
```

Exit code `0` on success, non-zero otherwise. CI calls `run.sh` verbatim.

## Layout

```
compat/hello/
├── hello.proto                      ← business message shared by both sides
├── dotnet-server/
│   ├── HelloServer.csproj           ← ProjectReferences the sibling vertex-dotnet
│   └── Program.cs                   ← minimal hosted gRPC + Vertex.Messaging subscriber
├── go-client/
│   ├── go.mod                       ← `replace` directive to sibling vertex-go
│   ├── main.go                      ← Dial + Publish
│   └── gen/hello.pb.go              ← protoc-gen-go output (checked in)
├── run.sh                           ← orchestrator: start server, run client, check
└── README.md                        ← this file
```

## Regenerate Go code from `hello.proto`

```bash
cd compat/hello
protoc --proto_path=. \
  --go_out=. --go_opt=module=vertex-hello-compat \
  hello.proto
```
