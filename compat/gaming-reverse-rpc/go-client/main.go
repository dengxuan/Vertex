// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/gaming-reverse-rpc — Go client. Uses gaming-go-sdk to register
// a OrderSubmit handler and wait for one inbound reverse-RPC. Returns a
// canned OrderSubmitAck. The .NET server drives the Invoke and asserts
// the round-trip.

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
	port := env("GAMING_REVERSE_RPC_PORT", "50057")
	expectedOrderID := env("GAMING_REVERSE_RPC_ORDER_ID", "order-42")
	balanceStr := env("GAMING_REVERSE_RPC_BALANCE", "987.65")
	balance, _ := strconv.ParseFloat(balanceStr, 64)
	dialBudget := envDuration("GAMING_REVERSE_RPC_DIAL_TIMEOUT_MS", 10*time.Second)
	waitBudget := envDuration("GAMING_REVERSE_RPC_WAIT_TIMEOUT_MS", 15*time.Second)

	addr := "127.0.0.1:" + port
	fmt.Fprintf(os.Stderr, "client: dialing %s\n", addr)

	handled := make(chan *gamingsdk.OrderSubmit, 1)

	client, err := gamingsdk.NewClient(gamingsdk.Options{
		Address:       addr,
		TenantID:      "compat-tenant",
		SecretKey:     "compat-test-secret",
		DialTimeout:   dialBudget,
		AutoReconnect: false,
	}, gamingsdk.HandlerFuncs{
		OrderSubmit: func(ctx context.Context, msg *gamingsdk.OrderSubmit) (*gamingsdk.OrderSubmitAck, error) {
			select {
			case handled <- msg:
			default:
			}
			return &gamingsdk.OrderSubmitAck{
				OrderId:       expectedOrderID,
				BalanceAmount: balance,
			}, nil
		},
		// Other reverse handlers left unset — they won't fire here; if they
		// ever do, HandlerFuncs returns a "not configured" error which the
		// server would observe as a RemoteError (test would fail loudly).
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
	case msg := <-handled:
		fmt.Printf("client: PASS — handled OrderSubmit(user_id=%q channel_id=%q amount=%v)\n",
			msg.UserId, msg.ChannelId, msg.BettingAmount)
	case <-time.After(waitBudget):
		fmt.Fprintf(os.Stderr, "client: FAIL — timed out after %s waiting for inbound OrderSubmit\n", waitBudget)
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
