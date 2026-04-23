# Cross-language compatibility tests

Vertex guarantees that a .NET sender talks correctly to a Go receiver and vice versa. This directory holds the orchestration that verifies this.

## Status: 🚧 planning

Concrete test scenarios land alongside the first Go implementation milestone.

## Why it lives in the spec repo

Because compat tests **orchestrate multiple implementation repos**. They need to check out [vertex-dotnet](https://github.com/dengxuan/vertex-dotnet) and [vertex-go](https://github.com/dengxuan/vertex-go) and run peers against each other. Putting this orchestration in either impl repo would create a circular ownership problem (who owns the matrix?). The spec repo is the natural home.

## Planned structure

```
compat/
├── README.md
├── docker-compose.yml       ← spins up one peer per language
├── Makefile                 ← targets: fetch-impls, build, run-all, run-scenario
├── scenarios/               ← one subdir per scenario
│   ├── hello-world/
│   │   ├── protos/          ← scenario-specific .proto (if any)
│   │   └── expectations.md  ← pre/postconditions independent of language
│   └── ...
└── runner/                  ← shell scripts + CI harness
```

## Running locally (future)

```bash
# clones vertex-dotnet + vertex-go next to this repo, at pinned commits
make fetch-impls

# builds each impl
make build

# runs a single scenario, all language pairs
make run-scenario SCENARIO=hello-world

# runs everything
make run-all
```

## CI

Each compat scenario runs on every PR that touches `/spec/`, `/protos/`, or `/compat/`. Compat CI also runs nightly against `main` of each impl repo to catch regressions.

Impl-repo PRs can optionally trigger compat CI against their own branch to pre-validate spec-level changes.

## Adding a scenario

1. Create `scenarios/<name>/`.
2. Write `expectations.md` describing the protocol-level invariants this scenario verifies.
3. Add peer implementations as tiny apps in each language impl repo under `samples/compat/<name>/`.
4. Wire the scenario into `runner/`.
5. Verify locally, then open PRs to the spec repo (this directory) and each impl repo.
