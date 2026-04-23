#!/usr/bin/env bash
# compat/gaming-event-push — orchestrator. Starts a minimal Vertex-based
# gaming server that pushes one IssueOpening event to the first connecting
# peer, and runs the (Vertex-based) gaming-go-sdk against it. Validates the
# server→client fire-and-forget event path before the real Gaming service
# gets migrated.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

WORKSPACE="$(cd "../../.." && pwd)"
[[ -d "$WORKSPACE/vertex-dotnet" ]] \
  || { echo "error: clone dengxuan/vertex-dotnet at $WORKSPACE/vertex-dotnet" >&2; exit 1; }
[[ -d "$WORKSPACE/vertex-go" ]] \
  || { echo "error: clone dengxuan/vertex-go at $WORKSPACE/vertex-go" >&2; exit 1; }
[[ -d "$WORKSPACE/../L8CHAT/gaming-go-sdk" ]] \
  || { echo "error: clone L8CHAT/gaming-go-sdk at ${WORKSPACE%/*}/L8CHAT/gaming-go-sdk" >&2; exit 1; }

PORT="${GAMING_EVENT_PUSH_PORT:-50056}"
export GAMING_EVENT_PUSH_PORT="$PORT"
export GAMING_EVENT_PUSH_TIMEOUT_MS="${GAMING_EVENT_PUSH_TIMEOUT_MS:-15000}"

SERVER_PID=""
cleanup() {
  if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
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
  echo "✗ compat/gaming-event-push FAIL — server never bound :$PORT" >&2
  exit 1
fi
exec 3<&-

echo "→ running Go client (gaming-go-sdk subscriber)"
(cd go-client && go run .)

echo "→ waiting for server to finish"
set +e
wait "$SERVER_PID"
SERVER_RC=$?
set -e
SERVER_PID=""

if [[ "$SERVER_RC" -eq 0 ]]; then
  echo "✓ compat/gaming-event-push PASS"
else
  echo "✗ compat/gaming-event-push FAIL — server exited with $SERVER_RC"
  exit "$SERVER_RC"
fi
