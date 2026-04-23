# Vertex protos

Shared Protobuf definitions used by Vertex itself. These are **NOT** business messages — they are Vertex's own internal protocol types.

Business messages (the payloads Vertex carries) are defined in YOUR repo, not here.

Every language implementation ([vertex-dotnet](https://github.com/dengxuan/vertex-dotnet), [vertex-go](https://github.com/dengxuan/vertex-go), …) pulls these protos via their language's protoc toolchain and regenerates.

## Layout

```
protos/
└── vertex/
    └── v1/              ← wire format v1; additive-only evolution
        ├── envelope.proto           (reserved for future Envelope-as-proto migration; unused by wire v1)
        └── transport/
            └── grpc/
                └── v1/
                    └── bidi.proto   (the TransportFrame used by Vertex.Transport.Grpc)
```

## Versioning

The directory (`vertex/v1/`, `vertex/v2/`, …) is the wire format version. Within a version, changes must be backwards-compatible by Protobuf rules (only add fields, never repurpose tags, never change types, never make required fields optional).

Breaking changes require a new version directory AND coordinated rollout across all languages. See [`/spec/wire-format.md`](../spec/wire-format.md) § 5.

## Codegen

Every language implementation runs its own protoc invocation against these files; generated files are checked into each language's source tree (not gitignored, because IDE / editor experience relies on them).

- `.NET`: `Grpc.Tools` MSBuild integration, `<Protobuf Include="...">` in csproj.
- Go: `protoc --go_out` + `--go-grpc_out`.
- PHP (when added): `protoc --php_out` + `grpc_php_plugin`.

## Buf (future)

Once we have multiple proto consumers, we plan to adopt [buf](https://buf.build) for lint + breaking-change detection in CI. Tracked as an open item.
