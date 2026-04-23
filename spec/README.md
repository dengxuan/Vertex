# Specs

This directory contains **normative** documents. Every Vertex implementation — in any language — MUST conform to the specs here. Implementation-specific details (how a language binding wires up DI, how it names configuration classes) live next to the implementation, not here.

## Documents

| File | Scope | Audience |
|---|---|---|
| [`wire-format.md`](./wire-format.md) | Byte-level envelope + transport framing | Anyone implementing a new transport in any language |
| [`transport-contract.md`](./transport-contract.md) | Four transport invariants (ironclad rules) | Everyone implementing or debugging a transport |

## Compatibility promise

- A given `wire-format.md` version (e.g. v1) is forward-compatible with itself: a v1 sender works with any v1 receiver.
- Breaking changes produce a new major version of the spec and require coordinated rollout across all language packages.
- Non-breaking additions (e.g. new `MessageKind` values, optional metadata) are allowed within a major version with feature detection.

## Change process

See [`../CONTRIBUTING.md`](../CONTRIBUTING.md) § B for how to propose spec changes. Short version: open an issue, implement in all languages in one PR, run `/compat/` tests.
