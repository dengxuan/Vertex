<?php

// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/hello — PHP client side. Connects to the .NET server, publishes one
// HelloEvent, exits. Proves wire-format interop from PHP (Swoole) → .NET,
// mirroring go-client/main.go and cpp-client/main.cpp.
//
// Requires ext-swoole. Run via run-php.sh (which wires up the autoloaders).

declare(strict_types=1);

require __DIR__ . '/bootstrap.php';

use Vertex\Compat\Hello\V1\HelloEvent;
use Vertex\Messaging\MessagingChannel;
use Vertex\Serialization\ProtobufSerializer;
use Vertex\Transport\Grpc\GrpcClientTransport;

use function Swoole\Coroutine\run;

$port = getenv('HELLO_PORT') ?: '50051';
$greeting = getenv('HELLO_GREETING') ?: 'hello from php';
$serverAddr = '127.0.0.1:' . $port;

// Everything runs inside one coroutine context — Swoole's HTTP/2 client, the
// transport read loop, and the messaging receive loop are all coroutines.
$exitCode = 0;
run(function () use ($serverAddr, $greeting, &$exitCode) {
    fwrite(STDERR, "client: dialing {$serverAddr}\n");

    $transport = new GrpcClientTransport($serverAddr);
    try {
        $transport->connect();
    } catch (\Throwable $e) {
        fwrite(STDERR, "client: FAIL — dial {$serverAddr}: {$e->getMessage()}\n");
        $exitCode = 2;
        return;
    }

    $channel = new MessagingChannel('hello', $transport, new ProtobufSerializer());

    // Publish one HelloEvent and exit. The target is ignored for gRPC client
    // transports (single server peer); we pass "" per the Publish contract.
    $event = new HelloEvent(['greeting' => $greeting]);
    try {
        $channel->publish($event, '');
    } catch (\Throwable $e) {
        fwrite(STDERR, "client: FAIL — publish: {$e->getMessage()}\n");
        $exitCode = 3;
        $channel->close();
        return;
    }

    printf("client: published HelloEvent{greeting=%s}\n", var_export($event->getGreeting(), true));

    // Graceful close: the transport half-closes the send side (CloseSend
    // semantics) so the just-published frames flush to the server before the
    // HTTP/2 stream is torn down. No sleep-before-close workaround needed —
    // mirrors vertex-go after commit 5e41b8d.
    $channel->close();
});

exit($exitCode);
