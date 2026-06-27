#!/usr/bin/env bash
# compat/bidi-echo — orchestrator. Starts the LONG-LIVED .NET echo server, runs
# the PHP client's bounded bidirectional smoke (~30s of continuous + concurrent
# echo round-trips over one connection), reports PASS/FAIL, and stops the server.
#
# Unlike the single-shot scenarios, the server stays up for the whole window, so
# this exercises the client transport's sustained long-lived send/receive — no
# shutdown race. Set BIDI_ECHO_DURATION_S to shorten the window in CI.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

# Sibling-clone pre-flight.
WORKSPACE="$(cd "../../.." && pwd)"
[[ -d "$WORKSPACE/vertex-dotnet" ]] \
  || { echo "error: clone dengxuan/vertex-dotnet at $WORKSPACE/vertex-dotnet" >&2; exit 1; }
[[ -d "$WORKSPACE/vertex-php" ]] \
  || { echo "error: clone dengxuan/vertex-php at $WORKSPACE/vertex-php" >&2; exit 1; }
# The SDK owns its dependencies, exactly like go-client relies on the sibling
# vertex-go module. compat installs nothing of its own.
[[ -f "$WORKSPACE/vertex-php/vendor/autoload.php" ]] \
  || { echo "error: run 'composer install' in $WORKSPACE/vertex-php (the SDK owns its deps)" >&2; exit 1; }

# Toolchain pre-flight: PHP with ext-swoole.
command -v php >/dev/null \
  || { echo "error: php not found on PATH" >&2; exit 1; }
php -m | grep -qi '^swoole$' \
  || { echo "error: ext-swoole not loaded (php -m | grep swoole). Install it: pecl install swoole" >&2; exit 1; }

PORT="${BIDI_ECHO_PORT:-50063}"
export BIDI_ECHO_PORT="$PORT"
export BIDI_ECHO_DURATION_S="${BIDI_ECHO_DURATION_S:-30}"

# (Re)generate the message classes from the shared echo.proto.
echo "→ generating php-client message classes (protoc)"
protoc --proto_path=. --php_out=php-client/gen echo.proto

# Server teardown on any exit path.
SERVER_PID=""
cleanup() {
  if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

echo "→ building .NET echo server"
dotnet build dotnet-server --configuration Release --nologo -v q

echo "→ starting .NET echo server on :$PORT"
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
  echo "✗ compat/bidi-echo FAIL — server never bound :$PORT" >&2
  exit 1
fi
exec 3<&-

echo "→ running PHP client (${BIDI_ECHO_DURATION_S}s bounded bidi smoke)"
if php php-client/main.php; then
  echo "✓ compat/bidi-echo PASS"
else
  RC=$?
  echo "✗ compat/bidi-echo FAIL — client exited with $RC" >&2
  exit "$RC"
fi
