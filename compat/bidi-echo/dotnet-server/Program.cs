// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/bidi-echo — .NET server side. A LONG-LIVED echo server: it registers
// an Echo RPC handler that returns each request's seq+payload verbatim, and
// stays up until killed. Unlike the single-shot hello-rpc server, it serves an
// unbounded number of round-trips — which is what lets a client run a bounded
// (~30s) bidirectional smoke test against it without racing a shutdown.

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vertex.Compat.BidiEcho.V1;
using Vertex.Messaging;
using Vertex.Transport.Grpc;

var port = int.Parse(Environment.GetEnvironmentVariable("BIDI_ECHO_PORT") ?? "50063");

var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.IncludeScopes = false; });
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.WebHost.ConfigureKestrel(o =>
{
    o.Listen(IPAddress.Loopback, port, l => l.Protocols = HttpProtocols.Http2);
});

builder.Services.AddGrpc();
builder.Services.AddGrpcServerTransport("bidi-echo");
builder.Services.AddMessagingChannel("bidi-echo", reg =>
    reg.RegisterRequest<EchoRequest, EchoResponse>());
builder.Services.AddRpcHandler<EchoRequest, EchoResponse, EchoHandler>("bidi-echo");

var app = builder.Build();
app.MapGrpcService<BidiServiceImpl>();

await app.StartAsync();
Console.WriteLine($"server: echo listening on http://127.0.0.1:{port} (long-lived; Ctrl+C to stop)");

// Stay up until the process is signalled (SIGINT/SIGTERM). The smoke client
// kills us when its bounded run is done.
await app.WaitForShutdownAsync();

// An Echo handler that mirrors the request straight back. Stateless; one
// instance per request (Scoped), so no shared state to race.
public class EchoHandler : IRpcHandler<EchoRequest, EchoResponse>
{
    public ValueTask<EchoResponse> HandleAsync(RpcContext<EchoRequest> ctx, CancellationToken ct)
        => ValueTask.FromResult(new EchoResponse
        {
            Seq = ctx.Request.Seq,
            Payload = ctx.Request.Payload,
        });
}
