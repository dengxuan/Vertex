# Contributing to Vertex (spec repository)

Thanks for your interest!

**This repository is the spec.** Code implementations live in per-language repos ([vertex-dotnet](https://github.com/dengxuan/vertex-dotnet), [vertex-go](https://github.com/dengxuan/vertex-go), …). Rules here are tuned for **protocol-level changes**, not day-to-day code changes — those go in the respective impl repo.

---

## What lives here

```
Vertex/
├── spec/            ← authoritative documents (normative)
│   ├── wire-format.md
│   └── transport-contract.md
├── protos/          ← shared .proto files, referenced by every impl
├── compat/          ← cross-language end-to-end tests (multi-repo orchestration)
└── docs/            ← project-level docs (governance, release policy, …)
```

## Types of changes

### A. Wire-spec change (`/spec/*`)

These are **normative** and affect every language implementation. Process:

1. **Open an issue first** describing the intended change and rationale. Tag `spec`.
2. In the PR under this repo:
   - Update `/spec/*.md` to describe the new behavior.
   - Bump the spec version if the change is breaking (`wire-format.md § 5`).
3. **Open companion PRs** in each affected impl repo to bring it in line. These PRs cross-reference the spec PR.
4. **Merge order**: spec PR merges **last**, after every impl PR is approved & green. This keeps `main` of the spec repo always describing reality.
5. Run `/compat/` tests end-to-end to verify all implementations interoperate on the new spec.

**Approval rule**: at least one maintainer per currently-shipped language must approve before the spec PR merges.

### B. Shared `.proto` change (`/protos/*`)

Semver-compatible evolution: additive only within the same version directory (`/protos/vertex/v1/`); breaking changes require a new version directory (`v1` → `v2`). `buf breaking` will be wired up in CI.

Each impl repo re-runs its protoc toolchain when the shared protos change.

### C. `/compat/` test / fixture change

`/compat/` orchestrates running vertex-dotnet and vertex-go against each other. Changes here don't need multi-impl sign-off but should pass locally against current `main` of all impl repos.

### D. `/docs/` or `README.md` change

Single-reviewer approval, no spec / compat impact.

---

## Branch strategy

- `main` — continuously releasable. Spec versions are frozen by tags (`v1.0.0-spec`, etc.).
- Feature branches: `feature/<issue>-<slug>`, `spec/<slug>`, `fix/<issue>-<slug>`.

---

## Commit / PR conventions

- [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `chore:`, `spec:` (Vertex-specific).
- PR title = commit title.
- PR body must include:
  - Summary of the change.
  - Which impl repos need companion PRs (if any). Link them.
  - Test plan: for spec changes, reference the `/compat/` run.

---

## Versioning

The **spec** is versioned separately from any impl package. See [`spec/wire-format.md § 5`](./spec/wire-format.md). Breaking changes produce a new major (wire v1 → v2). Implementations declare which wire version they support.

---

## Governance

Vertex is maintained by a small team. Spec PRs require multi-language approval. Impl repos are governed by their own maintainers.

---

## Where else to go

- **Code bug or language-specific feature request?** Open the issue in the corresponding impl repo ([vertex-dotnet](https://github.com/dengxuan/vertex-dotnet) / [vertex-go](https://github.com/dengxuan/vertex-go)).
- **Question about the spec, interop, or multi-language design?** Issue or Discussion here.
