# Cross-language compatibility tests

Vertex guarantees that a `.NET` sender talks correctly to a Go receiver and vice versa. This directory holds the end-to-end tests that verify this.

## Status: 🚧 planning

Scaffolding to be added alongside the first Go implementation milestone.

## Planned structure

```
compat/
├── README.md
├── docker-compose.yml           ← spins up a gRPC server + Vertex peers in both languages
├── scenarios/                   ← one subdir per scenario (hello-world, pub-sub, RPC, error, disconnect, large-message, ...)
│   ├── hello-world/
│   │   ├── protos/              ← shared test-only .proto (in addition to /protos)
│   │   ├── dotnet-peer/         ← .NET side of the scenario
│   │   └── go-peer/             ← Go side
│   └── ...
└── runner/                      ← CI harness: runs each scenario with all peer-language pairings
```

## Running locally (future)

```bash
cd compat
docker-compose up --build
./runner/run-all.sh
```

## CI

Each cross-language scenario runs on every PR that touches `/spec/`, `/protos/`, `/dotnet/`, or `/go/`. Failures here block the PR.

## Adding a scenario

1. Create `scenarios/<name>/protos/<name>.proto` for scenario-specific messages.
2. Implement the peer on both sides (`dotnet-peer/`, `go-peer/`).
3. Add the scenario to the runner config.
4. Verify locally, then open a PR.
