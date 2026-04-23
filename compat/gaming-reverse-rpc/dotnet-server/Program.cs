// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/gaming-reverse-rpc — .NET server is the CALLER. Waits for a Go
// client (using gaming-go-sdk) to connect, then invokes OrderSubmit on
// that peer's handler; the Go side returns OrderSubmitAck and the
// server asserts the round-trip. This exercises the hardest path in
// Gaming: server-initiated request/response through the SDK's handler
// dispatch.

using System.Net;
using Feivoo.Gaming.Grpc;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vertex.Messaging;
using Vertex.Transport;
using Vertex.Transport.Grpc;

var port = int.Parse(Environment.GetEnvironmentVariable("GAMING_REVERSE_RPC_PORT") ?? "50057");
var timeoutMs = int.Parse(Environment.GetEnvironmentVariable("GAMING_REVERSE_RPC_TIMEOUT_MS") ?? "15000");
var expectedOrderId = Environment.GetEnvironmentVariable("GAMING_REVERSE_RPC_ORDER_ID") ?? "order-42";
var expectedBalance = double.Parse(Environment.GetEnvironmentVariable("GAMING_REVERSE_RPC_BALANCE") ?? "987.65");

const string ChannelName = "feivoo-gaming-message";

var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.IncludeScopes = false; });
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.WebHost.ConfigureKestrel(o =>
{
    o.Listen(IPAddress.Loopback, port, l => l.Protocols = HttpProtocols.Http2);
});

builder.Services.AddGrpc();
builder.Services.AddGrpcServerTransport(ChannelName);
builder.Services.AddMessagingChannel(ChannelName,
    reg => reg.RegisterRequest<OrderSubmit, OrderSubmitAck>());
// No AddRpcHandler<>: the server is the CALLER, not a handler.

var app = builder.Build();
app.MapGrpcService<BidiServiceImpl>();

var connectedTcs = new TaskCompletionSource<PeerId>(TaskCreationOptions.RunContinuationsAsynchronously);
var transport = (ITransport)app.Services.GetRequiredService<GrpcServerTransport>();
transport.PeerConnectionChanged += (_, e) =>
{
    if (e.State == PeerConnectionState.Connected)
    {
        connectedTcs.TrySetResult(e.Peer);
    }
};

await app.StartAsync();
Console.WriteLine($"server: listening on http://127.0.0.1:{port} (timeout {timeoutMs}ms)");

try
{
    using var cts = new CancellationTokenSource(timeoutMs);

    // 1) Wait for the Go client to connect.
    var peerId = await connectedTcs.Task.WaitAsync(cts.Token);
    Console.WriteLine($"server: peer connected id={peerId.Value}; invoking OrderSubmit");

    // 2) Invoke OrderSubmit on the peer's handler.
    var rpcClient = app.Services.GetRequiredKeyedService<IRpcClient>(ChannelName);
    var response = await rpcClient.InvokeAsync<OrderSubmit, OrderSubmitAck>(
        new OrderSubmit
        {
            MessageId = "compat-msg-001",
            ChannelId = "channel-lobby",
            UserId = "user-42",
            LotteryPlayId = "play-1",
            IssueNumber = "20260424-001",
            CurrencyId = "USD",
            BettingAmount = 12.34,
            BettingOdds = 1.95,
            OrderType = LotteryOrderType.TgMiniApp,
            OrderTime = Timestamp.FromDateTime(DateTime.UtcNow),
        },
        target: peerId,
        timeout: TimeSpan.FromSeconds(5));

    if (response.OrderId != expectedOrderId || response.BalanceAmount != expectedBalance)
    {
        Console.Error.WriteLine($"server: FAIL — unexpected Ack order_id=\"{response.OrderId}\" balance={response.BalanceAmount} " +
                                $"(want order_id=\"{expectedOrderId}\" balance={expectedBalance})");
        return 2;
    }

    Console.WriteLine($"server: PASS — reverse Invoke returned OrderSubmitAck(order_id=\"{response.OrderId}\" balance={response.BalanceAmount})");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("server: FAIL — timed out waiting for peer connect or response");
    return 1;
}
finally
{
    await app.StopAsync();
}
