# Vertex protos

Shared Protobuf definitions used by Vertex itself. These are **NOT** business messages — they are Vertex's own internal protocol types.

Business messages (the payloads Vertex carries) are defined in YOUR repo, not here.

Every language implementation ([vertex-dotnet](https://github.com/dengxuan/vertex-dotnet), [vertex-go](https://github.com/dengxuan/vertex-go), …) pulls these protos via their language's protoc toolchain and regenerates.

## Layout

```
protos/
└── vertex/
    └── transport/
        └── grpc/
            └── v1/
                └── bidi.proto   (TransportFrame + Bidi service for Vertex.Transport.Grpc)
```

## Versioning

Each proto's version lives in **one place only** — both the directory segment (`v1/`) and the proto package (`vertex.transport.grpc.v1`). No double-versioning in the path. Within a version, changes must be backwards-compatible by Protobuf rules (additive only). Breaking changes require a new version directory (`v2/`) AND a coordinated rollout across all implementations. See [`/spec/wire-format.md`](../spec/wire-format.md) § 5.

## Codegen

Every language implementation runs its own protoc invocation against these files; generated files are checked into each language's source tree (not gitignored, because IDE / editor experience relies on them).

- `.NET`: `Grpc.Tools` MSBuild integration, `<Protobuf Include="...">` in csproj.
- Go: `protoc --go_out` + `--go-grpc_out`.
- PHP (when added): `protoc --php_out` + `grpc_php_plugin`.

## Buf (future)

Once we have multiple proto consumers, we plan to adopt [buf](https://buf.build) for lint + breaking-change detection in CI. Tracked as an open item.
