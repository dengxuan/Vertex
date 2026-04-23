#!/usr/bin/env bash
# compat/hello-disconnect — orchestrator. Runs the same .NET server process
# twice (each handles one request then exits). The Go client dials once and
# expects its transport to auto-reconnect between the two servers.
#
# PASS requires:
#   - Invoke #1 succeeds (server A handles, exits 0)
#   - Invoke #2 succeeds AGAINST A NEW PROCESS (distinct server_boot)
#   - Both server instances exit 0
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

WORKSPACE="$(cd "../../.." && pwd)"
[[ -d "$WORKSPACE/vertex-dotnet" ]] \
  || { echo "error: clone dengxuan/vertex-dotnet at $WORKSPACE/vertex-dotnet" >&2; exit 1; }
[[ -d "$WORKSPACE/vertex-go" ]] \
  || { echo "error: clone dengxuan/vertex-go at $WORKSPACE/vertex-go" >&2; exit 1; }

PORT="${HELLO_DISCONNECT_PORT:-50053}"
export HELLO_DISCONNECT_PORT="$PORT"
export HELLO_DISCONNECT_TIMEOUT_MS="${HELLO_DISCONNECT_TIMEOUT_MS:-15000}"
export HELLO_DISCONNECT_RECONNECT_BUDGET_MS="${HELLO_DISCONNECT_RECONNECT_BUDGET_MS:-30000}"

CLIENT_PID=""
SERVER_PID=""
cleanup() {
  for pid in "$CLIENT_PID" "$SERVER_PID"; do
    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
      kill "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
    fi
  done
}
trap cleanup EXIT INT TERM

wait_for_bind() {
  local port="$1"
  local label="$2"
  for _ in $(seq 1 150); do
    if (exec 3<>/dev/tcp/127.0.0.1/"$port") 2>/dev/null; then
      exec 3<&-
      echo "→ $label: bound :$port"
      return 0
    fi
    sleep 0.1
  done
  echo "✗ $label: never bound :$port" >&2
  return 1
}

wait_for_free() {
  local port="$1"
  for _ in $(seq 1 150); do
    if ! (exec 3<>/dev/tcp/127.0.0.1/"$port") 2>/dev/null; then
      return 0
    fi
    exec 3<&-
    sleep 0.1
  done
  echo "✗ port :$port never freed" >&2
  return 1
}

echo "→ building .NET server"
dotnet build dotnet-server --configuration Release --nologo -v q

# ── Instance A ─────────────────────────────────────────────────────────────
echo "→ starting .NET server instance A on :$PORT"
dotnet run --project dotnet-server --configuration Release --no-build --no-launch-profile &
SERVER_PID=$!
wait_for_bind "$PORT" "server A"

# Launch the Go client in the background — it must live across the server
# restart to exercise auto-reconnect.
echo "→ starting Go client (long-lived across restart)"
(cd go-client && go run .) &
CLIENT_PID=$!

# Server A exits after handling the first request. Wait for it here so we
# can validate its exit status before launching B.
echo "→ waiting for server A to finish"
set +e
wait "$SERVER_PID"
SERVER_A_RC=$?
set -e
SERVER_PID=""

if [[ "$SERVER_A_RC" -ne 0 ]]; then
  echo "✗ compat/hello-disconnect FAIL — server A exited with $SERVER_A_RC" >&2
  exit "$SERVER_A_RC"
fi
echo "→ server A exited cleanly"

# Port must be free before we can rebind. Kestrel releases it on shutdown,
# but on Windows/WSL there's sometimes a TIME_WAIT tail.
wait_for_free "$PORT" || true

# ── Instance B ─────────────────────────────────────────────────────────────
echo "→ starting .NET server instance B on :$PORT"
dotnet run --project dotnet-server --configuration Release --no-build --no-launch-profile &
SERVER_PID=$!
wait_for_bind "$PORT" "server B"

# Server B exits after handling Invoke #2.
echo "→ waiting for server B to finish"
set +e
wait "$SERVER_PID"
SERVER_B_RC=$?
set -e
SERVER_PID=""

if [[ "$SERVER_B_RC" -ne 0 ]]; then
  echo "✗ compat/hello-disconnect FAIL — server B exited with $SERVER_B_RC" >&2
  exit "$SERVER_B_RC"
fi
echo "→ server B exited cleanly"

# Wait for the Go client to finish reporting PASS / FAIL.
echo "→ waiting for Go client"
set +e
wait "$CLIENT_PID"
CLIENT_RC=$?
set -e
CLIENT_PID=""

if [[ "$CLIENT_RC" -eq 0 ]]; then
  echo "✓ compat/hello-disconnect PASS"
else
  echo "✗ compat/hello-disconnect FAIL — client exited with $CLIENT_RC"
  exit "$CLIENT_RC"
fi
