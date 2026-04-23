# Contributing to Vertex

Thanks for your interest!

Vertex is a **multi-language monorepo**. This changes contribution mechanics compared to a single-language project — please read carefully.

---

## Structure at a glance

```
vertex/
├── spec/          ← authoritative wire & contract specs (language-neutral)
├── protos/        ← shared .proto files
├── dotnet/        ← .NET implementation (Vertex.Dotnet.* NuGet packages)
├── go/            ← Go implementation (github.com/dengxuan/vertex/go)
├── compat/        ← cross-language end-to-end tests
└── docs/          ← user-facing tutorials & guides (not spec)
```

---

## Types of changes

### A. Bug fix or feature inside a single language

If your change only touches `/dotnet/` OR only touches `/go/` AND does not change wire bytes, submit a single PR scoped to that language. No cross-language coordination needed.

### B. Wire-spec change

Any change under `/spec/` is **normative** and affects every language implementation. Process:

1. Open an issue first describing the intended change and rationale.
2. In your PR:
   - Update `/spec/*` files.
   - Update ALL language implementations to conform.
   - Update `/compat/` tests so cross-language interop is verified.
3. A wire-spec change requires approval from at least one maintainer per affected language.

If you want to propose a wire change but cannot implement all languages yourself, open the issue and tag `help wanted` / `protocol` — we will pair you with contributors from the missing languages.

### C. Proto change under `/protos/`

Use `buf` for lint/breaking-change detection (CI enforces this once added). Any proto file under `/protos/vertex/*/` follows semver-compatible evolution: additive only within the same version directory; breaking changes require a new version directory (`v1` → `v2`).

---

## Branch strategy

- `main` — stable, continuously releasable (or `-rc`-tagged releases)
- `release/*` — long-lived branches for major-version rewrites (if any)
- Feature branches: `feature/<issue>-<slug>`, `fix/<issue>-<slug>`, `spec/<slug>`, etc.

Default target branch for PRs is `main`.

---

## Commit / PR conventions

- [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `chore:`, `refactor:`, `test:`, `spec:` (Vertex-specific)
- PR title = commit title (we squash-merge single-commit PRs, preserving that title)
- PR description should include:
  - What changed
  - Why (link to issue for non-trivial changes)
  - Test plan (especially relevant for wire-spec / protocol changes: must run `/compat/` tests)

---

## Local development

### .NET

```bash
cd dotnet
dotnet restore
dotnet test
```

### Go

```bash
cd go
go mod download
go test ./...
```

### Cross-language compat

```bash
cd compat
# (TBD — docker-compose + runner scripts)
```

---

## Versioning and release

Vertex follows SemVer per language package:

- **.NET**: `Vertex.Dotnet.*` NuGet packages, versioned by MinVer from `v<major>.<minor>.<patch>` git tags on `main`.
- **Go**: module `github.com/dengxuan/vertex/go`, versioned by Go module tags `go/v<major>.<minor>.<patch>`.

**Wire-format version** is separate from package versions. Wire v1 can span many v1.x.y package releases. A wire v2 would coincide with major bumps in every language package.

---

## Governance

Vertex is currently maintained by a small team. All PRs need approval from at least one maintainer of each affected language. Wire-spec PRs need at least one maintainer of each shipped language (today: .NET + Go, once Go is online).

New maintainers are added based on sustained contributions in a given language.

---

## Questions / help

- [GitHub Discussions](https://github.com/dengxuan/Vertex/discussions) for design questions, language support requests, interop issues
- [GitHub Issues](https://github.com/dengxuan/Vertex/issues) for bugs and concrete tasks
