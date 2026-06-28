# compat/bidi-echo — long-lived bidirectional smoke

**Goal**: prove the gRPC client transport stays healthy under *sustained*
bidirectional traffic — many request/response round-trips, including concurrent
bursts, over one long-lived connection. Complements the single-shot scenarios
(`hello` one-way event, `hello-rpc` one RPC) which prove correctness but not
endurance.

**Current matrix**:

| Client → Server | Status |
|---|---|
| `php-client` → `dotnet-server` | ✅ via `./run.sh` |

**Shape**: a **bounded-duration** loop (default 30s, set `BIDI_ECHO_DURATION_S`)
where the PHP client continuously invokes an `EchoRequest` and asserts the
`EchoResponse` mirrors it, periodically firing a concurrent burst of invokes.
A smoke test can't run forever, so the window is bounded; the client exits 0
(PASS) once it closes with zero failures.

## What it proves

- **Long-lived send/receive** — the client transport's send-loop + recv-loop
  keep one HTTP/2 bidi stream healthy across tens of thousands of round-trips,
  not just a single exchange. (~145k round-trips in 30s locally.)
- **No interleave under concurrency** — periodic bursts of N simultaneous
  invokes each get back *their own* response. If the send-loop serialization or
  `request_id` correlation were wrong, bursts would cross responses; the client
  asserts an exact seq+payload match on every reply.
- **No shutdown race** — unlike `hello-rpc`'s single-shot server (which exits
  the instant it handles one request), `dotnet-server` here is **long-lived**
  (`WaitForShutdownAsync`), so the client drives real sustained traffic.

## Why a dedicated echo server

The `hello-rpc` server is single-shot: it `return`s as soon as its handler runs,
so its response write races a host shutdown. That's fine for a one-shot
correctness check but useless for endurance. `dotnet-server` here registers an
echo RPC handler and stays up until signalled, so the client controls the
window.

## Run

```bash
./run.sh                          # 30s window
BIDI_ECHO_DURATION_S=5 ./run.sh   # quick 5s window (CI)
```

```
→ running PHP client (30s bounded bidi smoke)
client: PASS — 145311 echo round-trips over 30s, 0 failures
✓ compat/bidi-echo PASS
```

Exits `0` on success, non-zero otherwise.

## Prerequisites

Clone the impl repos as siblings of `Vertex/` (see `../README.md`):

```
your-workspace/
├── Vertex/          ← you are here
├── vertex-dotnet/   ← dotnet-server
└── vertex-php/      ← php-client uses the sibling SDK directly (owns its deps)
```

Tooling: .NET SDK 8.0+, PHP 8.2+ with `ext-swoole`, `protoc`. The PHP client
installs nothing of its own — it autoloads the sibling `vertex-php` SDK (which
provides `google/protobuf`), exactly like `go-client` uses a `replace` directive.

## Layout

```
compat/bidi-echo/
├── echo.proto                      ← EchoRequest / EchoResponse, shared
├── dotnet-server/
│   ├── EchoServer.csproj           ← ProjectReferences sibling vertex-dotnet
│   └── Program.cs                  ← long-lived echo RPC handler
├── php-client/
│   ├── bootstrap.php               ← autoload sibling vertex-php SDK + ./gen
│   ├── main.php                    ← bounded bidi smoke (invoke loop + bursts)
│   └── gen/                        ← protoc --php_out output (checked in)
├── run.sh                          ← orchestrator: echo server + php client
└── README.md                       ← this file
```
