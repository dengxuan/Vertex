// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/hello-reverse-rpc — Go client side. Connects to the .NET server,
// registers a HelloRequest handler, and waits for ONE inbound request to
// arrive. Exits 0 after handling; exits 1 on timeout.
//
// The .NET server drives: once it sees our connection, it invokes us.
// Our handler echoes "hello from go <name>" and returns the response;
// the Messaging layer ships the response back, .NET's InvokeAsync unblocks.

package main

import (
	"context"
	"fmt"
	"os"
	"strconv"
	"time"

	"github.com/dengxuan/vertex-go/messaging"
	grpctransport "github.com/dengxuan/vertex-go/transport/grpc"

	pb "vertex-hello-reverse-rpc-compat/gen"
)

func main() {
	port := env("HELLO_REVERSE_RPC_PORT", "50054")
	dialBudget := envDuration("HELLO_REVERSE_RPC_DIAL_TIMEOUT_MS", 10*time.Second)
	waitBudget := envDuration("HELLO_REVERSE_RPC_WAIT_TIMEOUT_MS", 15*time.Second)

	serverAddr := "127.0.0.1:" + port
	fmt.Fprintf(os.Stderr, "client: dialing %s\n", serverAddr)

	dialCtx, cancelDial := context.WithTimeout(context.Background(), dialBudget)
	defer cancelDial()

	tr, err := grpctransport.Dial(dialCtx, serverAddr)
	if err != nil {
		fmt.Fprintf(os.Stderr, "client: FAIL — dial %s: %v\n", serverAddr, err)
		os.Exit(2)
	}
	defer tr.Close()

	channel := messaging.NewChannel("hello-reverse-rpc", tr)
	defer channel.Close()

	// Signal when a HelloRequest has been handled.
	handled := make(chan *pb.HelloRequest, 1)

	if err := messaging.HandleRequest[*pb.HelloRequest, *pb.HelloResponse](channel,
		func(ctx context.Context, req *pb.HelloRequest) (*pb.HelloResponse, error) {
			// Push to the signal first, then return the response. The signal
			// tells main() to exit; even if the channel to main is full, we
			// still return the response so the server can observe success.
			select {
			case handled <- req:
			default:
			}
			return &pb.HelloResponse{Greeting: "hello from go " + req.Name}, nil
		}); err != nil {
		fmt.Fprintf(os.Stderr, "client: FAIL — HandleRequest: %v\n", err)
		os.Exit(3)
	}

	// Wait for one request to be handled. After this fires, the deferred
	// channel.Close will drain the dispatcher goroutine (up to its
	// closeDrainTimeout) so the handler's response reaches the wire
	// before process exit — see vertex-go messaging channel docstring.
	select {
	case req := <-handled:
		fmt.Printf("client: PASS — handled HelloRequest{name=%q}\n", req.Name)
	case <-time.After(waitBudget):
		fmt.Fprintf(os.Stderr, "client: FAIL — timed out after %s waiting for inbound HelloRequest\n", waitBudget)
		os.Exit(4)
	}
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
