// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/hello-disconnect — Go client side. Exercises the transport's
// auto-reconnect path end-to-end:
//
//  1. Invoke #1   → server A handles it and exits
//  2. transport's read loop observes EOF → Disconnected → backoff loop
//  3. run.sh starts server B on the same port
//  4. transport reconnects (new bidi stream, no caller involvement)
//  5. Invoke #2   → server B handles it; PongResponse.ServerBoot != boot1
//
// The retry-until-distinct-boot pattern is deliberate: it tests *functional*
// recovery (Invoke works again after disconnect) without needing a side
// channel into the transport's Connections() events — those are consumed by
// the Channel's own connectionLoop, not exposed to user code.

package main

import (
	"context"
	"errors"
	"fmt"
	"os"
	"strconv"
	"time"

	"github.com/dengxuan/vertex-go/messaging"
	grpctransport "github.com/dengxuan/vertex-go/transport/grpc"

	pb "vertex-hello-disconnect-compat/gen"
)

func main() {
	port := env("HELLO_DISCONNECT_PORT", "50053")
	dialBudget := envDuration("HELLO_DISCONNECT_DIAL_TIMEOUT_MS", 10*time.Second)
	invokeTimeout := envDuration("HELLO_DISCONNECT_INVOKE_TIMEOUT_MS", 3*time.Second)
	reconnectBudget := envDuration("HELLO_DISCONNECT_RECONNECT_BUDGET_MS", 30*time.Second)

	serverAddr := "127.0.0.1:" + port
	fmt.Fprintf(os.Stderr, "client: dialing %s\n", serverAddr)

	dialCtx, cancelDial := context.WithTimeout(context.Background(), dialBudget)
	defer cancelDial()

	// Tight backoff so the test finishes well inside reconnectBudget.
	tr, err := grpctransport.Dial(dialCtx, serverAddr,
		grpctransport.WithReconnect(grpctransport.ReconnectPolicy{
			Enabled:        true,
			InitialBackoff: 100 * time.Millisecond,
			MaxBackoff:     1 * time.Second,
			Multiplier:     2.0,
			Jitter:         0.1,
		}),
	)
	if err != nil {
		fmt.Fprintf(os.Stderr, "client: FAIL — dial %s: %v\n", serverAddr, err)
		os.Exit(2)
	}
	defer tr.Close()

	channel := messaging.NewChannel("hello-disconnect", tr)
	defer channel.Close()

	// Invoke #1 — against server A.
	boot1, err := pingOnce(dialCtx, channel, "ping-1", invokeTimeout)
	if err != nil {
		fmt.Fprintf(os.Stderr, "client: FAIL — invoke #1: %v\n", err)
		os.Exit(3)
	}
	fmt.Printf("client: invoke #1 OK boot=%s\n", boot1)

	// Retry Invoke #2 until:
	//   - it succeeds AND returns a different server_boot (= reconnected to
	//     a fresh process), OR
	//   - we exhaust the reconnect budget.
	//
	// Transient failures are expected during the window where server A has
	// exited and server B has not yet bound the port: Invoke either times
	// out (no stream / stream dies mid-flight) or returns a transport-level
	// send error. Those are fine — we just retry.
	deadline := time.Now().Add(reconnectBudget)
	var lastErr error
	attempt := 0
	for time.Now().Before(deadline) {
		attempt++
		invokeCtx, cancel := context.WithTimeout(context.Background(), invokeTimeout+500*time.Millisecond)
		boot2, err := pingOnce(invokeCtx, channel, fmt.Sprintf("ping-2-%d", attempt), invokeTimeout)
		cancel()

		if err == nil {
			if boot2 == boot1 {
				fmt.Fprintf(os.Stderr, "client: FAIL — invoke #2 attempt %d returned same server_boot %q; run.sh must restart the server process, not reuse it\n", attempt, boot2)
				os.Exit(4)
			}
			fmt.Printf("client: invoke #2 OK boot=%s (attempt %d)\n", boot2, attempt)
			fmt.Printf("client: PASS — two invokes across disconnect, distinct server boots (%s, %s)\n", boot1, boot2)
			return
		}

		lastErr = err
		if !isTransient(err) {
			fmt.Fprintf(os.Stderr, "client: FAIL — invoke #2 attempt %d: non-transient error: %v\n", attempt, err)
			os.Exit(5)
		}
		fmt.Fprintf(os.Stderr, "client: invoke #2 attempt %d transient: %v — retrying\n", attempt, err)
		time.Sleep(200 * time.Millisecond)
	}

	fmt.Fprintf(os.Stderr, "client: FAIL — reconnect budget %s exhausted after %d attempts; last error: %v\n", reconnectBudget, attempt, lastErr)
	os.Exit(6)
}

func pingOnce(ctx context.Context, channel *messaging.Channel, id string, timeout time.Duration) (string, error) {
	resp := &pb.PongResponse{}
	if err := channel.Invoke(ctx, "", &pb.PingRequest{Id: id}, resp, timeout); err != nil {
		return "", err
	}
	if resp.Id != id {
		return "", fmt.Errorf("echo mismatch: want id=%q got id=%q", id, resp.Id)
	}
	if resp.ServerBoot == "" {
		return "", fmt.Errorf("empty server_boot")
	}
	return resp.ServerBoot, nil
}

// isTransient reports whether an Invoke error is the kind we should retry
// while waiting for the server to come back up. Timeouts, peer-disconnected,
// transport send failures, and the single-shot server's "already served"
// RemoteError are all expected during the restart window. Anything else is
// fatal.
func isTransient(err error) bool {
	if errors.Is(err, messaging.ErrTimeout) {
		return true
	}
	if errors.Is(err, context.DeadlineExceeded) || errors.Is(err, context.Canceled) {
		return true
	}
	var peerErr *messaging.PeerDisconnectedError
	if errors.As(err, &peerErr) {
		return true
	}
	var remoteErr *messaging.RemoteError
	if errors.As(err, &remoteErr) {
		return true
	}
	// transport.Send wrapper: "vertex messaging: send request: ..." — retry
	// those too; they're the client-side reflection of the stream dying.
	if msg := err.Error(); len(msg) > 0 {
		if contains(msg, "send request") || contains(msg, "stream send") {
			return true
		}
	}
	return false
}

func contains(s, sub string) bool {
	for i := 0; i+len(sub) <= len(s); i++ {
		if s[i:i+len(sub)] == sub {
			return true
		}
	}
	return false
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
