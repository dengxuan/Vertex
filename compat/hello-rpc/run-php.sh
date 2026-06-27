#!/usr/bin/env bash
# compat/hello-rpc — PHP variant orchestrator. Starts the .NET server, runs the
# PHP (Swoole) client, both sides must PASS. Mirror of run.sh (Go variant) and
# of compat/hello/run-php.sh.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

# Sibling-clone pre-flight.
WORKSPACE="$(cd "../../.." && pwd)"
[[ -d "$WORKSPACE/vertex-dotnet" ]] \
  || { echo "error: clone dengxuan/vertex-dotnet at $WORKSPACE/vertex-dotnet" >&2; exit 1; }
[[ -d "$WORKSPACE/vertex-php" ]] \
  || { echo "error: clone dengxuan/vertex-php at $WORKSPACE/vertex-php" >&2; exit 1; }

# Toolchain pre-flight: PHP with ext-swoole + composer.
command -v php >/dev/null \
  || { echo "error: php not found on PATH" >&2; exit 1; }
php -m | grep -qi '^swoole$' \
  || { echo "error: ext-swoole not loaded (php -m | grep swoole). Install it: pecl install swoole" >&2; exit 1; }
command -v composer >/dev/null \
  || { echo "error: composer not found on PATH" >&2; exit 1; }

PORT="${HELLO_RPC_PORT:-50052}"
export HELLO_RPC_PORT="$PORT"
export HELLO_RPC_TIMEOUT_MS="${HELLO_RPC_TIMEOUT_MS:-15000}"
export HELLO_RPC_ROOM_NAME="${HELLO_RPC_ROOM_NAME:-lobby}"

# One-shot client setup: install google/protobuf and (re)generate the message
# classes from the shared hello_rpc.proto. Cheap to repeat; idempotent.
echo "→ preparing php-client (composer install + protoc)"
(cd php-client && composer install --no-interaction --quiet)
protoc --proto_path=. --php_out=php-client/gen hello_rpc.proto

# Server teardown on any exit path.
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
  echo "✗ compat/hello-rpc (php) FAIL — server never bound :$PORT" >&2
  exit 1
fi
exec 3<&-

echo "→ running PHP client"
php php-client/main.php

echo "→ waiting for server to finish (it exits after handling the request)"
set +e
wait "$SERVER_PID"
SERVER_RC=$?
set -e
SERVER_PID=""

if [[ "$SERVER_RC" -eq 0 ]]; then
  echo "✓ compat/hello-rpc (php) PASS"
else
  echo "✗ compat/hello-rpc (php) FAIL — server exited with $SERVER_RC"
  exit "$SERVER_RC"
fi
