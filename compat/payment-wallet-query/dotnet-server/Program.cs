// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/payment-wallet-query — .NET server side. Models what a
// Vertex-based Feivoo Payment wallet server would look like for one RPC
// (WalletBalanceQuery → WalletBalanceQueryAck). Exits 0 after handling
// the first request; exits 1 on timeout.
//
// This scenario exists to prove that the rewritten payment-go-sdk
// (consuming vertex-go) actually talks on-wire to a Vertex server.
// No real auth / DB / business logic — just handler echoes a canned
// balance keyed off the request fields.

using System.Net;
using Feivoo.Payment.Grpc.Proto;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vertex.Messaging;
using Vertex.Transport.Grpc;

var port = int.Parse(Environment.GetEnvironmentVariable("PAYMENT_WALLET_QUERY_PORT") ?? "50055");
var timeoutMs = int.Parse(Environment.GetEnvironmentVariable("PAYMENT_WALLET_QUERY_TIMEOUT_MS") ?? "15000");
var expectedUserId = Environment.GetEnvironmentVariable("PAYMENT_WALLET_QUERY_USER_ID") ?? "user-42";
var expectedCurrency = Environment.GetEnvironmentVariable("PAYMENT_WALLET_QUERY_CURRENCY") ?? "USD";

var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.IncludeScopes = false; });
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.WebHost.ConfigureKestrel(o =>
{
    o.Listen(IPAddress.Loopback, port, l => l.Protocols = HttpProtocols.Http2);
});

// Channel name must match the Go SDK's hard-coded channelName
// ("feivoo-payment-wallet"). If either side drifts, the messaging layer
// can't correlate pending invokes.
const string ChannelName = "feivoo-payment-wallet";

builder.Services.AddGrpc();
builder.Services.AddGrpcServerTransport(ChannelName);
builder.Services.AddMessagingChannel(ChannelName,
    reg => reg.RegisterRequest<WalletBalanceQuery, WalletBalanceQueryAck>());
builder.Services.AddRpcHandler<WalletBalanceQuery, WalletBalanceQueryAck, WalletBalanceQueryHandler>(ChannelName);

var app = builder.Build();
app.MapGrpcService<BidiServiceImpl>();

await app.StartAsync();
Console.WriteLine($"server: listening on http://127.0.0.1:{port} (timeout {timeoutMs}ms)");

try
{
    using var cts = new CancellationTokenSource(timeoutMs);
    var req = await WalletBalanceQueryHandler.ReceivedTcs.Task.WaitAsync(cts.Token);

    if (req.UserId != expectedUserId || req.CurrencyId != expectedCurrency)
    {
        Console.Error.WriteLine($"server: FAIL — unexpected request user_id=\"{req.UserId}\" currency=\"{req.CurrencyId}\" " +
                                $"(want user_id=\"{expectedUserId}\" currency=\"{expectedCurrency}\")");
        return 2;
    }

    Console.WriteLine($"server: PASS — handled WalletBalanceQuery(user_id=\"{req.UserId}\" currency=\"{req.CurrencyId}\")");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("server: FAIL — timed out waiting for WalletBalanceQuery");
    return 1;
}
finally
{
    await app.StopAsync();
}

public class WalletBalanceQueryHandler : IRpcHandler<WalletBalanceQuery, WalletBalanceQueryAck>
{
    // Single-shot TCS so Program.cs can observe "a request arrived" and
    // exit 0 once we've echoed back a response. Real Payment server
    // obviously serves unbounded requests.
    public static readonly TaskCompletionSource<WalletBalanceQuery> ReceivedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask<WalletBalanceQueryAck> HandleAsync(RpcContext<WalletBalanceQuery> ctx, CancellationToken ct)
    {
        ReceivedTcs.TrySetResult(ctx.Request);
        return ValueTask.FromResult(new WalletBalanceQueryAck
        {
            Balance = new WalletBalance
            {
                CurrencyId = ctx.Request.CurrencyId,
                Amount = 1000.00,
                FrozenAmount = 50.00,
            },
        });
    }
}
