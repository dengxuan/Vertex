#!/usr/bin/env bash
# compat/payment-wallet-query — orchestrator. Launches a minimal Vertex-based
# wallet server (.NET) and runs the rewritten payment-go-sdk against it.
# This is the end-to-end gate for the Feivoo Payment migration: if this
# passes, the SDK's wire protocol is compatible with a Vertex server and
# we can safely migrate the real Payment C# service.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

WORKSPACE="$(cd "../../.." && pwd)"
[[ -d "$WORKSPACE/vertex-dotnet" ]] \
  || { echo "error: clone dengxuan/vertex-dotnet at $WORKSPACE/vertex-dotnet" >&2; exit 1; }
[[ -d "$WORKSPACE/vertex-go" ]] \
  || { echo "error: clone dengxuan/vertex-go at $WORKSPACE/vertex-go" >&2; exit 1; }
[[ -d "$WORKSPACE/../L8CHAT/payment-go-sdk" ]] \
  || { echo "error: clone L8CHAT/payment-go-sdk at ${WORKSPACE%/*}/L8CHAT/payment-go-sdk" >&2; exit 1; }

PORT="${PAYMENT_WALLET_QUERY_PORT:-50055}"
export PAYMENT_WALLET_QUERY_PORT="$PORT"
export PAYMENT_WALLET_QUERY_TIMEOUT_MS="${PAYMENT_WALLET_QUERY_TIMEOUT_MS:-15000}"

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
  echo "✗ compat/payment-wallet-query FAIL — server never bound :$PORT" >&2
  exit 1
fi
exec 3<&-

echo "→ running Go client (payment-go-sdk QueryWalletBalance)"
(cd go-client && go run .)

echo "→ waiting for server to finish"
set +e
wait "$SERVER_PID"
SERVER_RC=$?
set -e
SERVER_PID=""

if [[ "$SERVER_RC" -eq 0 ]]; then
  echo "✓ compat/payment-wallet-query PASS"
else
  echo "✗ compat/payment-wallet-query FAIL — server exited with $SERVER_RC"
  exit "$SERVER_RC"
fi
