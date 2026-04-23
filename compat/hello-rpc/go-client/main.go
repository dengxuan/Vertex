// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/hello-rpc — Go client side. Invokes CreateRoom RPC on the .NET
// server, verifies the typed response, exits. Proves wire-format interop
// in the request/response direction (complementary to /compat/hello which
// is one-way event).

package main

import (
	"context"
	"fmt"
	"os"
	"strconv"
	"time"

	"github.com/dengxuan/vertex-go/messaging"
	grpctransport "github.com/dengxuan/vertex-go/transport/grpc"

	pb "vertex-hello-rpc-compat/gen"
)

func main() {
	port := env("HELLO_RPC_PORT", "50052")
	roomName := env("HELLO_RPC_ROOM_NAME", "lobby")
	dialBudget := envDuration("HELLO_RPC_DIAL_TIMEOUT_MS", 10*time.Second)
	invokeTimeout := envDuration("HELLO_RPC_INVOKE_TIMEOUT_MS", 5*time.Second)

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

	channel := messaging.NewChannel("hello-rpc", transport)
	defer channel.Close()

	resp := &pb.RoomCreatedResponse{}
	err = channel.Invoke(ctx, "", &pb.CreateRoomRequest{RoomName: roomName}, resp, invokeTimeout)
	if err != nil {
		fmt.Fprintf(os.Stderr, "client: FAIL — Invoke: %v\n", err)
		os.Exit(3)
	}

	// Verify the round-trip carried both directions correctly.
	wantRoomID := "room-" + roomName
	wantGreeting := "hello from dotnet"
	if resp.RoomId != wantRoomID || resp.Greeting != wantGreeting {
		fmt.Fprintf(os.Stderr, "client: FAIL — unexpected response: %+v (want RoomId=%q Greeting=%q)\n",
			resp, wantRoomID, wantGreeting)
		os.Exit(4)
	}

	fmt.Printf("client: PASS — RoomCreatedResponse{room_id=%q greeting=%q}\n", resp.RoomId, resp.Greeting)
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
