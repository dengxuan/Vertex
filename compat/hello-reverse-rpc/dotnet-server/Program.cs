// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/hello-reverse-rpc — .NET server side. Unlike hello-rpc where .NET
// handles, this scenario has .NET as the CALLER: the server waits for a Go
// client to connect, then invokes the client's handler. Proves server→client
// RPC direction (MessagingChannel.InvokeAsync with an explicit PeerId target
// → GrpcServerTransport.SendAsync → Go client read loop → HandleRequest
// dispatch → response back → .NET pending-request map resolves).

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vertex.Compat.HelloReverseRpc.V1;
using Vertex.Messaging;
using Vertex.Transport;
using Vertex.Transport.Grpc;

var port = int.Parse(Environment.GetEnvironmentVariable("HELLO_REVERSE_RPC_PORT") ?? "50054");
var timeoutMs = int.Parse(Environment.GetEnvironmentVariable("HELLO_REVERSE_RPC_TIMEOUT_MS") ?? "15000");
var expectedName = Environment.GetEnvironmentVariable("HELLO_REVERSE_RPC_NAME") ?? "world";

var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.IncludeScopes = false; });
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.WebHost.ConfigureKestrel(o =>
{
    o.Listen(IPAddress.Loopback, port, l => l.Protocols = HttpProtocols.Http2);
});

builder.Services.AddGrpc();
builder.Services.AddGrpcServerTransport("hello-reverse-rpc");
builder.Services.AddMessagingChannel("hello-reverse-rpc",
    reg => reg.RegisterRequest<HelloRequest, HelloResponse>());
// No AddRpcHandler<> here — the server is the CALLER, not a handler.

var app = builder.Build();
app.MapGrpcService<BidiServiceImpl>();

// Signal the first Connected event so the main flow can then invoke the peer.
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
    Console.WriteLine($"server: peer connected id={peerId.Value}; invoking HelloRequest");

    // 2) Invoke the client's handler over the just-established stream.
    var rpcClient = app.Services.GetRequiredKeyedService<IRpcClient>("hello-reverse-rpc");
    var response = await rpcClient.InvokeAsync<HelloRequest, HelloResponse>(
        new HelloRequest { Name = expectedName },
        target: peerId,
        timeout: TimeSpan.FromSeconds(5));

    // 3) Verify.
    var expectedGreeting = $"hello from go {expectedName}";
    if (response.Greeting != expectedGreeting)
    {
        Console.Error.WriteLine($"server: FAIL — unexpected response Greeting=\"{response.Greeting}\" (want \"{expectedGreeting}\")");
        return 2;
    }

    Console.WriteLine($"server: PASS — reverse Invoke returned Greeting=\"{response.Greeting}\"");
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
