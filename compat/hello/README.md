# compat/hello — first cross-language scenario

**Goal**: prove that clients in any supported language speaking the [Vertex wire format](../../spec/wire-format.md) correctly interoperate with a .NET server built on [vertex-dotnet](https://github.com/dengxuan/vertex-dotnet).

**Current matrix**:

| Client → Server | Status |
|---|---|
| `go-client` → `dotnet-server` | ✅ via `./run.sh` |
| `cpp-client` → `dotnet-server` | ✅ via `./run-cpp.sh` |
| `php-client` → `dotnet-server` | ✅ via `./run-php.sh` (needs PHP + ext-swoole; see `.devcontainer/`) |

**Shape**: one-way Publish (`KindEvent`). Simpler than RPC; exercises the full envelope encode → gRPC frame chunking → gRPC frame reassembly → envelope decode → MessagingChannel dispatch pipeline end to end.

## What it proves

- **Proto package alignment** — every client side generates code from `hello.proto` resolving to topic `vertex.compat.hello.v1.HelloEvent` (Go via `go_package`, C# via `csharp_namespace`, C++ via the proto package itself). The .NET server's `MessageTopic` on the same type resolves to the identical topic, so routing works without per-language adapters.
- **4-frame envelope** — every client constructs `[topic, kind, request_id, payload]` per `/spec/wire-format.md § 2`; .NET `WireFormat.Decode` unpacks it.
- **gRPC TransportFrame framing** — every client splits one envelope into 4 `TransportFrame` messages with `end_of_message=true` on the last (`/spec/wire-format.md § 4.2`); .NET's `GrpcServerTransport` reassembles.
- **Protobuf payload** — every side uses its native protobuf marshalling through its `IMessageSerializer` on the same `HelloEvent`.

## Prerequisites

Clone all four Vertex repos as siblings (only the ones for the variant you want
to run are required):

```
your-workspace/
├── Vertex/          ← you are here
├── vertex-dotnet/   ← required for any variant (dotnet-server)
├── vertex-go/       ← required for ./run.sh (go-client)
└── vertex-cpp/      ← required for ./run-cpp.sh (cpp-client)
```

Tooling:

- .NET SDK 8.0+ (always)
- Go 1.22+ (for go variant)
- CMake 3.20+ + a C++20 compiler + vcpkg (for cpp variant; vcpkg auto-installs
  grpc + protobuf — first install ~10–15 min, then cached)

## Run

### Go variant

```bash
./run.sh
```

```
→ running Go client
client: published HelloEvent{greeting="hello from go"}
server: PASS — received HelloEvent{greeting="hello from go"}
✓ compat/hello PASS
```

### C++ variant

```bash
./run-cpp.sh
```

```
→ running cpp client
client: dialing 127.0.0.1:50051
client: published HelloEvent{greeting="hello from cpp"}
server: PASS — received HelloEvent{greeting="hello from cpp"}
✓ compat/hello (cpp) PASS
```

Both scripts exit `0` on success, non-zero otherwise. CI calls them verbatim.

## Layout

```
compat/hello/
├── hello.proto                      ← business message shared by all sides
├── dotnet-server/
│   ├── HelloServer.csproj           ← ProjectReferences sibling vertex-dotnet
│   └── Program.cs                   ← minimal hosted gRPC + Vertex.Messaging subscriber
├── go-client/
│   ├── go.mod                       ← `replace` directive to sibling vertex-go
│   ├── main.go                      ← Dial + Publish
│   └── gen/hello.pb.go              ← protoc-gen-go output (checked in)
├── cpp-client/
│   ├── CMakeLists.txt               ← add_subdirectory the sibling vertex-cpp
│   ├── vcpkg.json                   ← grpc + protobuf for codegen
│   └── main.cpp                     ← GrpcTransport + MessagingChannel + Publish
├── php-client/
│   ├── bootstrap.php                ← autoload sibling vertex-php SDK (owns deps) + ./gen
│   ├── main.php                     ← GrpcClientTransport + MessagingChannel + publish
│   └── gen/                         ← protoc --php_out output (checked in)
├── run.sh                           ← orchestrator: dotnet-server + go-client
├── run-cpp.sh                       ← orchestrator: dotnet-server + cpp-client
├── run-php.sh                       ← orchestrator: dotnet-server + php-client
└── README.md                        ← this file
```

## Regenerate Go code from `hello.proto`

```bash
cd compat/hello
protoc --proto_path=. \
  --go_out=. --go_opt=module=vertex-hello-compat \
  hello.proto
```

## Regenerate C++ code from `hello.proto`

C++ doesn't check generated code in — `cpp-client/CMakeLists.txt` runs `protoc`
on the fly into the build dir. No manual step.
