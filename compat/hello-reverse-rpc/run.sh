#!/usr/bin/env bash
# compat/hello-reverse-rpc — orchestrator. Starts the .NET server (which is
# the CALLER in this scenario), then runs the Go client (which handles). Both
# must PASS. The server drives the Invoke once the client connects; the
# client exits after handling exactly one request.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

WORKSPACE="$(cd "../../.." && pwd)"
[[ -d "$WORKSPACE/vertex-dotnet" ]] \
  || { echo "error: clone dengxuan/vertex-dotnet at $WORKSPACE/vertex-dotnet" >&2; exit 1; }
[[ -d "$WORKSPACE/vertex-go" ]] \
  || { echo "error: clone dengxuan/vertex-go at $WORKSPACE/vertex-go" >&2; exit 1; }

PORT="${HELLO_REVERSE_RPC_PORT:-50054}"
export HELLO_REVERSE_RPC_PORT="$PORT"
export HELLO_REVERSE_RPC_TIMEOUT_MS="${HELLO_REVERSE_RPC_TIMEOUT_MS:-15000}"

SERVER_PID=""
CLIENT_PID=""
cleanup() {
  for pid in "$CLIENT_PID" "$SERVER_PID"; do
    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
      kill "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
    fi
  done
}
trap cleanup EXIT INT TERM

echo "→ building .NET server"
dotnet build dotnet-server --configuration Release --nologo -v q

echo "→ starting .NET server on :$PORT"
dotnet run --project dotnet-server --configuration Release --no-build --no-launch-profile &
SERVER_PID=$!

echo "→ waiting for server to bind :$PORT"
for _ in $(seq 1 150); do
  if (exec 3<>/dev/tcp/127.0.0.1/"$PORT") 2>/dev/null; then
    exec 3<&-
    echo "→ server ready"
    break
  fi
  sleep 0.1
done
if ! (exec 3<>/dev/tcp/127.0.0.1/"$PORT") 2>/dev/null; then
  echo "✗ compat/hello-reverse-rpc FAIL — server never bound :$PORT" >&2
  exit 1
fi
exec 3<&-

echo "→ running Go client"
(cd go-client && go run .) &
CLIENT_PID=$!

echo "→ waiting for server to finish (it exits after Invoke succeeds)"
set +e
wait "$SERVER_PID"
SERVER_RC=$?
set -e
SERVER_PID=""

if [[ "$SERVER_RC" -ne 0 ]]; then
  echo "✗ compat/hello-reverse-rpc FAIL — server exited with $SERVER_RC" >&2
  # Let client finish / timeout so we see its log, then fail.
  wait "$CLIENT_PID" 2>/dev/null || true
  CLIENT_PID=""
  exit "$SERVER_RC"
fi

echo "→ waiting for Go client (it exits after handling one request)"
set +e
wait "$CLIENT_PID"
CLIENT_RC=$?
set -e
CLIENT_PID=""

if [[ "$CLIENT_RC" -eq 0 ]]; then
  echo "✓ compat/hello-reverse-rpc PASS"
else
  echo "✗ compat/hello-reverse-rpc FAIL — client exited with $CLIENT_RC"
  exit "$CLIENT_RC"
fi
