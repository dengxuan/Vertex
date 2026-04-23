// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/gaming-event-push — Go client. Uses the (Vertex-based) rewrite
// of gaming-go-sdk to subscribe to a server-pushed IssueOpening event.
// Smallest end-to-end check that the gaming SDK's event-push wire path
// matches Vertex on both sides before we migrate the real Gaming server.

package main

import (
	"context"
	"fmt"
	"os"
	"strconv"
	"time"

	gamingsdk "github.com/L8CHAT/gaming-go-sdk/feivoo/gaming/client"
)

func main() {
	port := env("GAMING_EVENT_PUSH_PORT", "50056")
	expectedChannelID := env("GAMING_EVENT_PUSH_CHANNEL_ID", "channel-lobby")
	expectedIssueNumber := env("GAMING_EVENT_PUSH_ISSUE", "20260424-001")
	dialBudget := envDuration("GAMING_EVENT_PUSH_DIAL_TIMEOUT_MS", 10*time.Second)
	waitBudget := envDuration("GAMING_EVENT_PUSH_WAIT_TIMEOUT_MS", 15*time.Second)

	addr := "127.0.0.1:" + port
	fmt.Fprintf(os.Stderr, "client: dialing %s\n", addr)

	received := make(chan *gamingsdk.IssueOpening, 1)

	client, err := gamingsdk.NewClient(gamingsdk.Options{
		Address:       addr,
		TenantID:      "compat-tenant",
		SecretKey:     "compat-test-secret",
		DialTimeout:   dialBudget,
		AutoReconnect: false,
	}, gamingsdk.HandlerFuncs{
		IssueOpening: func(ctx context.Context, msg *gamingsdk.IssueOpening) error {
			select {
			case received <- msg:
			default:
			}
			return nil
		},
		// Reverse-direction handlers are required by the SDK protocol but
		// are not exercised here — HandlerFuncs' zero-value implementations
		// return "not configured" errors which never fire on this scenario.
	})
	if err != nil {
		fmt.Fprintf(os.Stderr, "client: FAIL — NewClient: %v\n", err)
		os.Exit(2)
	}
	defer client.Close()

	connectCtx, cancelConnect := context.WithTimeout(context.Background(), dialBudget)
	defer cancelConnect()
	if err := client.Connect(connectCtx); err != nil {
		fmt.Fprintf(os.Stderr, "client: FAIL — Connect: %v\n", err)
		os.Exit(3)
	}

	select {
	case msg := <-received:
		if msg.ChannelId != expectedChannelID || msg.IssueNumber != expectedIssueNumber {
			fmt.Fprintf(os.Stderr, "client: FAIL — unexpected IssueOpening channel_id=%q issue=%q (want channel_id=%q issue=%q)\n",
				msg.ChannelId, msg.IssueNumber, expectedChannelID, expectedIssueNumber)
			os.Exit(4)
		}
		fmt.Printf("client: PASS — received IssueOpening(channel_id=%q issue=%q)\n", msg.ChannelId, msg.IssueNumber)
	case <-time.After(waitBudget):
		fmt.Fprintf(os.Stderr, "client: FAIL — timed out after %s waiting for IssueOpening event\n", waitBudget)
		os.Exit(5)
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
