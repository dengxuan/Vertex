// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/hello-rpc — .NET server side. Registers CreateRoomRequest handler
// and exits 0 after it handles the first request successfully. Exits 1 on
// timeout.

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vertex.Compat.HelloRpc.V1;
using Vertex.Messaging;
using Vertex.Transport.Grpc;

var port = int.Parse(Environment.GetEnvironmentVariable("HELLO_RPC_PORT") ?? "50052");
var timeoutMs = int.Parse(Environment.GetEnvironmentVariable("HELLO_RPC_TIMEOUT_MS") ?? "15000");
var expectedRoomName = Environment.GetEnvironmentVariable("HELLO_RPC_ROOM_NAME") ?? "lobby";

var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.IncludeScopes = false; });
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.WebHost.ConfigureKestrel(o =>
{
    o.Listen(IPAddress.Loopback, port, l => l.Protocols = HttpProtocols.Http2);
});

builder.Services.AddGrpc();
builder.Services.AddGrpcServerTransport("hello-rpc");
builder.Services.AddMessagingChannel("hello-rpc", reg =>
    reg.RegisterRequest<CreateRoomRequest, RoomCreatedResponse>());
builder.Services.AddRpcHandler<CreateRoomRequest, RoomCreatedResponse, HelloRpcHandler>("hello-rpc");

var app = builder.Build();
app.MapGrpcService<BidiServiceImpl>();

await app.StartAsync();
Console.WriteLine($"server: listening on http://127.0.0.1:{port} (timeout {timeoutMs}ms)");

try
{
    using var cts = new CancellationTokenSource(timeoutMs);
    var req = await HelloRpcHandler.ReceivedTcs.Task.WaitAsync(cts.Token);

    if (req.RoomName != expectedRoomName)
    {
        Console.Error.WriteLine($"server: FAIL — unexpected room_name \"{req.RoomName}\" (want \"{expectedRoomName}\")");
        return 2;
    }

    Console.WriteLine($"server: PASS — handled CreateRoom(room_name=\"{req.RoomName}\") and responded");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("server: FAIL — timed out waiting for CreateRoomRequest");
    return 1;
}
finally
{
    await app.StopAsync();
}

public class HelloRpcHandler : IRpcHandler<CreateRoomRequest, RoomCreatedResponse>
{
    // A static TCS the Program main awaits to detect successful handling.
    // Scoped handler would make passing state awkward; static is fine for a
    // single-shot compat test process.
    public static readonly TaskCompletionSource<CreateRoomRequest> ReceivedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask<RoomCreatedResponse> HandleAsync(RpcContext<CreateRoomRequest> ctx, CancellationToken ct)
    {
        ReceivedTcs.TrySetResult(ctx.Request);
        return ValueTask.FromResult(new RoomCreatedResponse
        {
            RoomId = $"room-{ctx.Request.RoomName}",
            Greeting = "hello from dotnet",
        });
    }
}
