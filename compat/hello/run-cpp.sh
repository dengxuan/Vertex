#!/usr/bin/env bash
# compat/hello — cpp variant orchestrator.
# 起 .NET server，跑 cpp-client，按 server 是否收到事件 PASS/FAIL。
# 跟 run.sh（go variant）对称，但 client 端走 vertex-cpp。
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

WORKSPACE="$(cd "../../.." && pwd)"
[[ -d "$WORKSPACE/vertex-dotnet" ]] \
  || { echo "error: clone dengxuan/vertex-dotnet at $WORKSPACE/vertex-dotnet" >&2; exit 1; }
[[ -d "$WORKSPACE/vertex-cpp" ]] \
  || { echo "error: clone dengxuan/vertex-cpp at $WORKSPACE/vertex-cpp" >&2; exit 1; }

PORT="${HELLO_PORT:-50051}"
export HELLO_PORT="$PORT"
export HELLO_TIMEOUT_MS="${HELLO_TIMEOUT_MS:-15000}"
export HELLO_GREETING="${HELLO_GREETING:-hello from cpp}"

# Build the C++ client first（如果还没 build）
CPP_BUILD_DIR="cpp-client/build"
if [[ ! -f "$CPP_BUILD_DIR/Release/cpp-client.exe" ]] && [[ ! -f "$CPP_BUILD_DIR/cpp-client" ]]; then
    echo "→ building cpp-client (first time only — pulls grpc/protobuf via vcpkg)"
    if [[ -n "${VCPKG_ROOT:-}" ]]; then
        TOOLCHAIN="-DCMAKE_TOOLCHAIN_FILE=$VCPKG_ROOT/scripts/buildsystems/vcpkg.cmake"
    elif [[ -f "C:/vcpkg/scripts/buildsystems/vcpkg.cmake" ]]; then
        TOOLCHAIN="-DCMAKE_TOOLCHAIN_FILE=C:/vcpkg/scripts/buildsystems/vcpkg.cmake"
    else
        echo "error: VCPKG_ROOT not set and C:/vcpkg/scripts/buildsystems/vcpkg.cmake not found" >&2
        exit 1
    fi
    cmake -S cpp-client -B "$CPP_BUILD_DIR" $TOOLCHAIN
    cmake --build "$CPP_BUILD_DIR" --config Release --target cpp-client
fi

# 找 cpp-client 可执行（Windows multi-config / Linux single-config）
if [[ -f "$CPP_BUILD_DIR/Release/cpp-client.exe" ]]; then
    CPP_CLIENT="$CPP_BUILD_DIR/Release/cpp-client.exe"
elif [[ -f "$CPP_BUILD_DIR/cpp-client.exe" ]]; then
    CPP_CLIENT="$CPP_BUILD_DIR/cpp-client.exe"
elif [[ -f "$CPP_BUILD_DIR/cpp-client" ]]; then
    CPP_CLIENT="$CPP_BUILD_DIR/cpp-client"
else
    echo "error: cpp-client binary not found under $CPP_BUILD_DIR" >&2
    exit 1
fi

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

# 等服务端 listen
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
  echo "✗ compat/hello (cpp) FAIL — server never bound :$PORT" >&2
  exit 1
fi
exec 3<&-

echo "→ running cpp client"
"$CPP_CLIENT"

echo "→ waiting for server to finish (it exits after handling the event)"
set +e
wait "$SERVER_PID"
SERVER_RC=$?
set -e
SERVER_PID=""

if [[ "$SERVER_RC" -eq 0 ]]; then
  echo "✓ compat/hello (cpp) PASS"
else
  echo "✗ compat/hello (cpp) FAIL — server exited with $SERVER_RC"
  exit "$SERVER_RC"
fi
