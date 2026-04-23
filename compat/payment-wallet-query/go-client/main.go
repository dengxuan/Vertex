// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/payment-wallet-query — Go client side. Uses the (Vertex-based)
// rewrite of payment-go-sdk to call QueryWalletBalance against a minimal
// Vertex server. This is the smallest possible end-to-end test proving
// the SDK wire protocol matches Vertex's, before the full Payment C#
// server gets migrated.
//
// Expectations:
//   - NewClient + Connect succeed
//   - QueryWalletBalance returns a WalletBalanceQueryAck with the canned
//     balance (1000.00 / frozen 50.00) the server hands back
//   - No RemoteError / timeout / dial failure

package main

import (
	"context"
	"fmt"
	"os"
	"strconv"
	"time"

	paymentsdk "github.com/L8CHAT/payment-go-sdk/feivoo/payment/client"
)

func main() {
	port := env("PAYMENT_WALLET_QUERY_PORT", "50055")
	userID := env("PAYMENT_WALLET_QUERY_USER_ID", "user-42")
	currency := env("PAYMENT_WALLET_QUERY_CURRENCY", "USD")
	dialBudget := envDuration("PAYMENT_WALLET_QUERY_DIAL_TIMEOUT_MS", 10*time.Second)
	requestTimeout := envDuration("PAYMENT_WALLET_QUERY_REQUEST_TIMEOUT_MS", 5*time.Second)

	addr := "127.0.0.1:" + port
	fmt.Fprintf(os.Stderr, "client: dialing %s\n", addr)

	client, err := paymentsdk.NewClient(paymentsdk.Options{
		Address:        addr,
		AccessId:       "compat-test",
		SecretKey:      "compat-test-secret",
		RequestTimeout: requestTimeout,
		DialTimeout:    dialBudget,
		AutoReconnect:  false,
	}, nil)
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

	callCtx, cancelCall := context.WithTimeout(context.Background(), requestTimeout+2*time.Second)
	defer cancelCall()

	ack, err := client.QueryWalletBalance(callCtx, &paymentsdk.WalletBalanceQuery{
		UserId:     userID,
		CurrencyId: currency,
	})
	if err != nil {
		fmt.Fprintf(os.Stderr, "client: FAIL — QueryWalletBalance: %v\n", err)
		os.Exit(4)
	}

	if ack == nil || ack.Balance == nil {
		fmt.Fprintf(os.Stderr, "client: FAIL — empty Ack or Balance: %+v\n", ack)
		os.Exit(5)
	}

	bal := ack.Balance
	wantCurrency := currency
	wantAmount := 1000.00
	wantFrozen := 50.00
	if bal.CurrencyId != wantCurrency || bal.Amount != wantAmount || bal.FrozenAmount != wantFrozen {
		fmt.Fprintf(os.Stderr, "client: FAIL — unexpected balance: currency=%q amount=%v frozen=%v "+
			"(want currency=%q amount=%v frozen=%v)\n",
			bal.CurrencyId, bal.Amount, bal.FrozenAmount,
			wantCurrency, wantAmount, wantFrozen)
		os.Exit(6)
	}

	fmt.Printf("client: PASS — QueryWalletBalance returned currency=%q amount=%v frozen=%v\n",
		bal.CurrencyId, bal.Amount, bal.FrozenAmount)
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
