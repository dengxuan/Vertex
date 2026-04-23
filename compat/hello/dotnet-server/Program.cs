// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/hello — .NET server side. Subscribes to HelloEvent; on receipt prints
// PASS to stdout and exits 0. If nothing arrives within HELLO_TIMEOUT_MS
// (default 15s), prints FAIL and exits 1.

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vertex.Compat.Hello.V1;
using Vertex.Messaging;
using Vertex.Transport.Grpc;

var port = int.Parse(Environment.GetEnvironmentVariable("HELLO_PORT") ?? "50051");
var timeoutMs = int.Parse(Environment.GetEnvironmentVariable("HELLO_TIMEOUT_MS") ?? "15000");
var expectedGreeting = Environment.GetEnvironmentVariable("HELLO_GREETING") ?? "hello from go";

var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.IncludeScopes = false; });
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.WebHost.ConfigureKestrel(o =>
{
    o.Listen(IPAddress.Loopback, port, l => l.Protocols = HttpProtocols.Http2);
});

builder.Services.AddGrpc();
builder.Services.AddGrpcServerTransport("hello");
builder.Services.AddMessagingChannel("hello", reg => reg.RegisterEvent<HelloEvent>());

var app = builder.Build();
app.MapGrpcService<BidiServiceImpl>();

// Subscribe BEFORE starting the host so no incoming event can race past the subscription.
var channel = app.Services.GetRequiredKeyedService<IMessageBus>("hello");
var received = new TaskCompletionSource<HelloEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
using var sub = channel.Subscribe<HelloEvent>((ctx, _) =>
{
    received.TrySetResult(ctx.Payload);
    return ValueTask.CompletedTask;
});

await app.StartAsync();
Console.WriteLine($"server: listening on http://127.0.0.1:{port} (timeout {timeoutMs}ms)");

try
{
    using var cts = new CancellationTokenSource(timeoutMs);
    var evt = await received.Task.WaitAsync(cts.Token);

    if (evt.Greeting != expectedGreeting)
    {
        Console.Error.WriteLine($"server: FAIL — unexpected greeting \"{evt.Greeting}\" (want \"{expectedGreeting}\")");
        return 2;
    }

    Console.WriteLine($"server: PASS — received HelloEvent{{greeting=\"{evt.Greeting}\"}}");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("server: FAIL — timed out waiting for HelloEvent");
    return 1;
}
finally
{
    await app.StopAsync();
}
