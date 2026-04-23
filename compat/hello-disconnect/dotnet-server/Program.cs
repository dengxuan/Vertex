// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/hello-disconnect — .NET server side. Handles exactly ONE PingRequest
// then abruptly exits. Each process exposes a fresh ServerBoot UUID so the
// client can verify its transport reconnected to a NEW process.
//
// We intentionally use Environment.Exit instead of app.StopAsync: a graceful
// shutdown would wait for the client's HTTP/2 stream to drain, but the
// client's whole point is to keep Invoking *across* the disconnect — that
// traffic would stall graceful shutdown indefinitely. A hard exit realistically
// simulates the crash case that the reconnect logic exists to handle.

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vertex.Compat.HelloDisconnect.V1;
using Vertex.Messaging;
using Vertex.Transport.Grpc;

var port = int.Parse(Environment.GetEnvironmentVariable("HELLO_DISCONNECT_PORT") ?? "50053");
var timeoutMs = int.Parse(Environment.GetEnvironmentVariable("HELLO_DISCONNECT_TIMEOUT_MS") ?? "15000");
var responseFlushMs = int.Parse(Environment.GetEnvironmentVariable("HELLO_DISCONNECT_FLUSH_MS") ?? "300");
var serverBoot = Guid.NewGuid().ToString("N");

var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.IncludeScopes = false; });
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.WebHost.ConfigureKestrel(o =>
{
    o.Listen(IPAddress.Loopback, port, l => l.Protocols = HttpProtocols.Http2);
});

builder.Services.AddGrpc();
builder.Services.AddGrpcServerTransport("hello-disconnect");
builder.Services.AddMessagingChannel("hello-disconnect", reg =>
    reg.RegisterRequest<PingRequest, PongResponse>());
builder.Services.AddSingleton(new ServerBoot(serverBoot));
builder.Services.AddRpcHandler<PingRequest, PongResponse, PingHandler>("hello-disconnect");

var app = builder.Build();
app.MapGrpcService<BidiServiceImpl>();

await app.StartAsync();
Console.WriteLine($"server: listening on http://127.0.0.1:{port} boot={serverBoot} (timeout {timeoutMs}ms)");

using var cts = new CancellationTokenSource(timeoutMs);
try
{
    var req = await PingHandler.ReceivedTcs.Task.WaitAsync(cts.Token);
    // Let the response reach the wire before we crash the process.
    await Task.Delay(responseFlushMs);
    Console.WriteLine($"server: PASS — handled Ping(id=\"{req.Id}\") boot={serverBoot}");
    Environment.Exit(0);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("server: FAIL — timed out waiting for PingRequest");
    Environment.Exit(1);
}

return 0; // unreachable

public record ServerBoot(string Value);

public class PingHandler : IRpcHandler<PingRequest, PongResponse>
{
    public static readonly TaskCompletionSource<PingRequest> ReceivedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Serve exactly one request. The orchestrator relies on "new process =
    // new boot UUID" to verify reconnect; letting a shutting-down instance
    // answer a second request would leak the old boot UUID and mask the
    // race we're trying to catch.
    private static int _served;

    private readonly ServerBoot _boot;

    public PingHandler(ServerBoot boot) => _boot = boot;

    public ValueTask<PongResponse> HandleAsync(RpcContext<PingRequest> ctx, CancellationToken ct)
    {
        if (Interlocked.Increment(ref _served) > 1)
        {
            throw new InvalidOperationException("hello-disconnect: single-shot server already served one request");
        }
        ReceivedTcs.TrySetResult(ctx.Request);
        return ValueTask.FromResult(new PongResponse
        {
            Id = ctx.Request.Id,
            ServerBoot = _boot.Value,
        });
    }
}
