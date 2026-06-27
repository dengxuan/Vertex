<?php

// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/hello-rpc — PHP client side. Invokes the CreateRoom RPC on the .NET
// server, verifies the typed response, exits. Proves request/response
// wire-format interop from PHP (Swoole) → .NET, complementary to /compat/hello
// (one-way event) and mirroring go-client/main.go.
//
// Requires ext-swoole. Run via run-php.sh (which wires up the autoloaders).

declare(strict_types=1);

require __DIR__ . '/bootstrap.php';

use Vertex\Compat\HelloRpc\V1\CreateRoomRequest;
use Vertex\Compat\HelloRpc\V1\RoomCreatedResponse;
use Vertex\Messaging\MessagingChannel;
use Vertex\Serialization\ProtobufSerializer;
use Vertex\Transport\Grpc\GrpcClientTransport;

use function Swoole\Coroutine\run;

$port = getenv('HELLO_RPC_PORT') ?: '50052';
$roomName = getenv('HELLO_RPC_ROOM_NAME') ?: 'lobby';
$invokeTimeout = ((int) (getenv('HELLO_RPC_INVOKE_TIMEOUT_MS') ?: '5000')) / 1000.0;
$serverAddr = '127.0.0.1:' . $port;

$exitCode = 0;
run(function () use ($serverAddr, $roomName, $invokeTimeout, &$exitCode) {
    fwrite(STDERR, "client: dialing {$serverAddr}\n");

    $transport = new GrpcClientTransport($serverAddr);
    try {
        $transport->connect();
    } catch (\Throwable $e) {
        fwrite(STDERR, "client: FAIL — dial {$serverAddr}: {$e->getMessage()}\n");
        $exitCode = 2;
        return;
    }

    $channel = new MessagingChannel('hello-rpc', $transport, new ProtobufSerializer());

    // Invoke CreateRoom and await the typed RoomCreatedResponse. The target is
    // ignored for gRPC client transports (single server peer); pass "".
    $request = new CreateRoomRequest(['room_name' => $roomName]);
    try {
        /** @var RoomCreatedResponse $response */
        $response = $channel->invoke($request, RoomCreatedResponse::class, '', $invokeTimeout);
    } catch (\Throwable $e) {
        fwrite(STDERR, "client: FAIL — invoke: {$e->getMessage()}\n");
        $exitCode = 3;
        $channel->close();
        return;
    }

    // Verify the round-trip carried both directions correctly — same assertions
    // as go-client so the two clients prove identical interop.
    $wantRoomId = 'room-' . $roomName;
    $wantGreeting = 'hello from dotnet';
    if ($response->getRoomId() !== $wantRoomId || $response->getGreeting() !== $wantGreeting) {
        fwrite(STDERR, \sprintf(
            "client: FAIL — unexpected response: room_id=%s greeting=%s (want room_id=%s greeting=%s)\n",
            var_export($response->getRoomId(), true),
            var_export($response->getGreeting(), true),
            var_export($wantRoomId, true),
            var_export($wantGreeting, true),
        ));
        $exitCode = 4;
        $channel->close();
        return;
    }

    printf(
        "client: PASS — RoomCreatedResponse{room_id=%s greeting=%s}\n",
        var_export($response->getRoomId(), true),
        var_export($response->getGreeting(), true),
    );

    $channel->close();
});

exit($exitCode);
