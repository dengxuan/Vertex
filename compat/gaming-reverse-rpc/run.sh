#!/usr/bin/env bash
# compat/gaming-reverse-rpc — orchestrator. .NET Vertex server waits for
# a Go client (gaming-go-sdk) to connect, then invokes OrderSubmit on
# that peer's handler. Validates the server→client request/response path
# — the trickiest direction in Gaming since it goes through the SDK's
# handler dispatch + Channel.Close drain.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

WORKSPACE="$(cd "../../.." && pwd)"
[[ -d "$WORKSPACE/vertex-dotnet" ]] \
  || { echo "error: clone dengxuan/vertex-dotnet at $WORKSPACE/vertex-dotnet" >&2; exit 1; }
[[ -d "$WORKSPACE/vertex-go" ]] \
  || { echo "error: clone dengxuan/vertex-go at $WORKSPACE/vertex-go" >&2; exit 1; }
[[ -d "$WORKSPACE/../L8CHAT/gaming-go-sdk" ]] \
  || { echo "error: clone L8CHAT/gaming-go-sdk at ${WORKSPACE%/*}/L8CHAT/gaming-go-sdk" >&2; exit 1; }

PORT="${GAMING_REVERSE_RPC_PORT:-50057}"
export GAMING_REVERSE_RPC_PORT="$PORT"
export GAMING_REVERSE_RPC_TIMEOUT_MS="${GAMING_REVERSE_RPC_TIMEOUT_MS:-15000}"

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
  echo "✗ compat/gaming-reverse-rpc FAIL — server never bound :$PORT" >&2
  exit 1
fi
exec 3<&-

echo "→ running Go client (gaming-go-sdk handler)"
(cd go-client && go run .) &
CLIENT_PID=$!

echo "→ waiting for server to finish (it exits after Invoke succeeds)"
set +e
wait "$SERVER_PID"
SERVER_RC=$?
set -e
SERVER_PID=""

if [[ "$SERVER_RC" -ne 0 ]]; then
  echo "✗ compat/gaming-reverse-rpc FAIL — server exited with $SERVER_RC" >&2
  wait "$CLIENT_PID" 2>/dev/null || true
  CLIENT_PID=""
  exit "$SERVER_RC"
fi

echo "→ waiting for Go client"
set +e
wait "$CLIENT_PID"
CLIENT_RC=$?
set -e
CLIENT_PID=""

if [[ "$CLIENT_RC" -eq 0 ]]; then
  echo "✓ compat/gaming-reverse-rpc PASS"
else
  echo "✗ compat/gaming-reverse-rpc FAIL — client exited with $CLIENT_RC"
  exit "$CLIENT_RC"
fi
