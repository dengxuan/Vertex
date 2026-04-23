// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/hello — Go client side. Connects to the .NET server, publishes one
// HelloEvent, exits. Proves wire-format interop from Go → .NET.

package main

import (
	"context"
	"fmt"
	"os"
	"strconv"
	"time"

	"github.com/dengxuan/vertex-go/messaging"
	grpctransport "github.com/dengxuan/vertex-go/transport/grpc"

	pb "vertex-hello-compat/gen"
)

func main() {
	port := env("HELLO_PORT", "50051")
	greeting := env("HELLO_GREETING", "hello from go")

	// Give the Dial a generous budget so a slow-starting server doesn't flake
	// the compat test; the run.sh orchestrator has already waited for the
	// port to be listening before invoking us.
	dialBudget := envDuration("HELLO_DIAL_TIMEOUT_MS", 10*time.Second)

	ctx, cancel := context.WithTimeout(context.Background(), dialBudget)
	defer cancel()

	serverAddr := "127.0.0.1:" + port
	fmt.Fprintf(os.Stderr, "client: dialing %s\n", serverAddr)

	transport, err := grpctransport.Dial(ctx, serverAddr)
	if err != nil {
		fmt.Fprintf(os.Stderr, "client: FAIL — dial %s: %v\n", serverAddr, err)
		os.Exit(2)
	}
	defer transport.Close()

	channel := messaging.NewChannel("hello", transport)
	defer channel.Close()

	// Publish one HelloEvent and exit. The target is ignored for gRPC client
	// transports (single server peer); we pass "" per the Publish contract.
	ev := &pb.HelloEvent{Greeting: greeting}
	if err := channel.Publish(ctx, "", ev); err != nil {
		fmt.Fprintf(os.Stderr, "client: FAIL — publish: %v\n", err)
		os.Exit(3)
	}

	// No sleep-before-Close workaround: vertex-go's transport.Close is now
	// graceful — it half-closes the stream via CloseSend (flushing buffered
	// frames) and waits for the server's response side to close before
	// cancelling. See vertex-go commit 5e41b8d.
	fmt.Printf("client: published HelloEvent{greeting=%q}\n", ev.Greeting)
}

func env(name, def string) string {
	if v := os.Getenv(name); v != "" {
		return v
	}
	return def
}

func envDuration(name string, def time.Duration) time.Duration {
	if v := os.Getenv(name); v != "" {
		if n, err := strconv.Atoi(v); err == nil {
			return time.Duration(n) * time.Millisecond
		}
	}
	return def
}
