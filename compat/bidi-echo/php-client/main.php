<?php

// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/bidi-echo — PHP client side. A bounded-duration bidirectional smoke
// test: over ONE long-lived connection it runs continuous echo round-trips for
// ~30s (configurable), interleaving concurrent bursts, and asserts every
// response matches its request. Proves sustained send/receive health and that
// concurrent invokes don't interleave/cross — exercising the gRPC client
// transport's send-loop + recv-loop on a real connection, not a fake one.
//
// A smoke test can't run forever, so the duration is bounded and the client
// exits 0 (PASS) / non-zero (FAIL) when the window closes.

declare(strict_types=1);

require __DIR__ . '/bootstrap.php';

use Swoole\Coroutine;
use Swoole\Coroutine\WaitGroup;
use Vertex\Compat\BidiEcho\V1\EchoRequest;
use Vertex\Compat\BidiEcho\V1\EchoResponse;
use Vertex\Messaging\MessagingChannel;
use Vertex\Serialization\ProtobufSerializer;
use Vertex\Transport\Grpc\GrpcClientTransport;

use function Swoole\Coroutine\run;

$port = getenv('BIDI_ECHO_PORT') ?: '50063';
$durationS = (float) (getenv('BIDI_ECHO_DURATION_S') ?: '30');
$burstEvery = (int) (getenv('BIDI_ECHO_BURST_EVERY') ?: '25'); // concurrent burst every N sequential calls
$burstSize = (int) (getenv('BIDI_ECHO_BURST_SIZE') ?: '20');
$serverAddr = '127.0.0.1:' . $port;

$exitCode = 0;
run(function () use ($serverAddr, $durationS, $burstEvery, $burstSize, &$exitCode) {
    fwrite(STDERR, "client: dialing {$serverAddr} (bidi-echo, {$durationS}s)\n");

    $transport = new GrpcClientTransport($serverAddr);
    try {
        $transport->connect();
    } catch (\Throwable $e) {
        fwrite(STDERR, "client: FAIL — dial {$serverAddr}: {$e->getMessage()}\n");
        $exitCode = 2;
        return;
    }

    $channel = new MessagingChannel('bidi-echo', $transport, new ProtobufSerializer());

    // One echo round-trip with a fresh seq/payload; returns true iff the
    // response mirrors the request exactly (proves correlation + integrity).
    $seq = 0;
    $roundTrip = static function () use ($channel, &$seq): bool {
        $n = ++$seq;
        $payload = 'echo-' . $n;
        /** @var EchoResponse $resp */
        $resp = $channel->invoke(new EchoRequest(['seq' => $n, 'payload' => $payload]), EchoResponse::class, '', 5.0);

        return $resp->getSeq() === $n && $resp->getPayload() === $payload;
    };

    $deadline = \microtime(true) + $durationS;
    $total = 0;
    $failures = 0;
    $sinceBurst = 0;

    while (\microtime(true) < $deadline) {
        try {
            if (!$roundTrip()) {
                ++$failures;
            }
        } catch (\Throwable $e) {
            ++$failures;
            fwrite(STDERR, "client: round-trip error: {$e->getMessage()}\n");
        }
        ++$total;

        // Periodically fire a concurrent burst: $burstSize invokes at once, each
        // must get back ITS OWN response. This is the interleave/serialization
        // check — if the send-loop or request_id correlation were wrong, bursts
        // would cross responses.
        if (++$sinceBurst >= $burstEvery) {
            $sinceBurst = 0;
            $wg = new WaitGroup();
            $burstFail = 0;
            for ($i = 0; $i < $burstSize; ++$i) {
                $wg->add();
                Coroutine::create(function () use ($roundTrip, $wg, &$burstFail): void {
                    try {
                        if (!$roundTrip()) {
                            ++$burstFail;
                        }
                    } catch (\Throwable) {
                        ++$burstFail;
                    } finally {
                        $wg->done();
                    }
                });
            }
            $wg->wait();
            $total += $burstSize;
            $failures += $burstFail;
        }
    }

    $channel->close();

    if ($failures === 0 && $total > 0) {
        fwrite(STDOUT, \sprintf("client: PASS — %d echo round-trips over %.0fs, 0 failures\n", $total, $durationS));
    } else {
        fwrite(STDERR, \sprintf("client: FAIL — %d/%d round-trips failed\n", $failures, $total));
        $exitCode = 3;
    }
});

exit($exitCode);
