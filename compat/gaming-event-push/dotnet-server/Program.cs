// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/gaming-event-push — .NET server. Publishes a single IssueOpening
// event to the first peer that connects. Validates the server → client
// fire-and-forget event path that Gaming's handler uses to push issue
// lifecycle updates and livekit events to subscribers like l8-game-server.
//
// Vertex side uses IMessageBus.PublishAsync(event, target: peerId) — the
// same API the real Gaming MerchantServerHandler will use once migrated.

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

var port = int.Parse(Environment.GetEnvironmentVariable("GAMING_EVENT_PUSH_PORT") ?? "50056");
var timeoutMs = int.Parse(Environment.GetEnvironmentVariable("GAMING_EVENT_PUSH_TIMEOUT_MS") ?? "15000");
var expectedChannelId = Environment.GetEnvironmentVariable("GAMING_EVENT_PUSH_CHANNEL_ID") ?? "channel-lobby";
var expectedIssueNumber = Environment.GetEnvironmentVariable("GAMING_EVENT_PUSH_ISSUE") ?? "20260424-001";

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
    reg => reg.RegisterEvent<IssueOpening>());
// No handler registrations — this scenario only pushes events.

var app = builder.Build();
app.MapGrpcService<BidiServiceImpl>();

// Capture the first Connected peer and hand its PeerId to the publish path.
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
    Console.WriteLine($"server: peer connected id={peerId.Value}; publishing IssueOpening");

    // 2) Publish the event targeted at that peer.
    var bus = app.Services.GetRequiredKeyedService<IMessageBus>(ChannelName);
    var evt = new IssueOpening
    {
        ChannelId = expectedChannelId,
        LotteryGameId = "game-lottery-001",
        IssueNumber = expectedIssueNumber,
        OpenTime = Timestamp.FromDateTime(DateTime.UtcNow),
        StopTime = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(5)),
    };
    await bus.PublishAsync(evt, target: peerId);

    // 3) Give the client a window to receive + handle the event. The client
    //    will print PASS and exit; this Task.Delay here is just the server's
    //    grace period before we tear down the stream. The .NET MessagingChannel
    //    drains in-flight dispatches on StopAsync; 1s covers the wire + dispatch.
    await Task.Delay(TimeSpan.FromSeconds(1));

    Console.WriteLine($"server: PASS — published IssueOpening(channel_id=\"{evt.ChannelId}\" issue=\"{evt.IssueNumber}\")");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("server: FAIL — timed out waiting for peer connect");
    return 1;
}
finally
{
    await app.StopAsync();
}
