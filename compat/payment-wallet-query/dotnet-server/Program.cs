// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// compat/payment-wallet-query — .NET server. Validates the full DI
// wiring path used by the real Feivoo Payment service:
//
//   AddFeivooPaymentGrpcServer<THandler, TAuth>()
//     └→ auth interceptor + Vertex grpc transport + messaging channel
//     └→ 9× AddRpcHandler<TReq, TResp, THandler>("feivoo-payment-wallet")
//
// A FakeWalletHandler implements all 9 IRpcHandler<> interfaces so the
// generic constraint is satisfied; only the WalletBalanceQuery path is
// exercised by the compat's Go client — the other 8 throw (they should
// never be called in this scenario).

using System.Net;
using Feivoo.Payment.Grpc.Proto;
using Feivoo.Payment.GrpcServer;
using Feivoo.Payment.GrpcServer.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vertex.Messaging;

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

builder.Services.AddFeivooPaymentGrpcServer<FakeWalletHandler, AllowAllAuthenticator>();

var app = builder.Build();
app.MapFeivooPaymentGrpcServer();

await app.StartAsync();
Console.WriteLine($"server: listening on http://127.0.0.1:{port} (timeout {timeoutMs}ms)");

try
{
    using var cts = new CancellationTokenSource(timeoutMs);
    var req = await FakeWalletHandler.ReceivedTcs.Task.WaitAsync(cts.Token);

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

// ── fixtures ───────────────────────────────────────────────────────────

public sealed class AllowAllAuthenticator : IPaymentServerAuthenticator
{
    public Task<PaymentPrincipal?> AuthenticateAsync(string accessId, string secretKey, CancellationToken cancellationToken = default)
        => Task.FromResult<PaymentPrincipal?>(new PaymentPrincipal(accessId));
}

public sealed class FakeWalletHandler :
    IRpcHandler<WalletBalanceQuery, WalletBalanceQueryAck>,
    IRpcHandler<WalletAllBalancesQuery, WalletAllBalancesQueryAck>,
    IRpcHandler<WalletTransfer, WalletTransferAck>,
    IRpcHandler<WalletFreeze, WalletFreezeAck>,
    IRpcHandler<WalletUnfreeze, WalletUnfreezeAck>,
    IRpcHandler<WalletFreezeWithTransfer, WalletFreezeWithTransferAck>,
    IRpcHandler<WalletUnfreezeWithTransfer, WalletUnfreezeWithTransferAck>,
    IRpcHandler<WalletBalanceAdd, WalletBalanceAddAck>,
    IRpcHandler<WalletBalanceSubtract, WalletBalanceSubtractAck>
{
    // Program main awaits this to observe that the Go client's query actually
    // reached the handler; the single-shot TCS fires once then exit 0.
    public static readonly TaskCompletionSource<WalletBalanceQuery> ReceivedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    ValueTask<WalletBalanceQueryAck> IRpcHandler<WalletBalanceQuery, WalletBalanceQueryAck>.HandleAsync(
        RpcContext<WalletBalanceQuery> ctx, CancellationToken ct)
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

    // The remaining 8 satisfy the generic constraint but should never fire
    // in this scenario; trip loudly if they do.
    ValueTask<WalletAllBalancesQueryAck> IRpcHandler<WalletAllBalancesQuery, WalletAllBalancesQueryAck>.HandleAsync(RpcContext<WalletAllBalancesQuery> c, CancellationToken t) => throw Unexpected("WalletAllBalancesQuery");
    ValueTask<WalletTransferAck> IRpcHandler<WalletTransfer, WalletTransferAck>.HandleAsync(RpcContext<WalletTransfer> c, CancellationToken t) => throw Unexpected("WalletTransfer");
    ValueTask<WalletFreezeAck> IRpcHandler<WalletFreeze, WalletFreezeAck>.HandleAsync(RpcContext<WalletFreeze> c, CancellationToken t) => throw Unexpected("WalletFreeze");
    ValueTask<WalletUnfreezeAck> IRpcHandler<WalletUnfreeze, WalletUnfreezeAck>.HandleAsync(RpcContext<WalletUnfreeze> c, CancellationToken t) => throw Unexpected("WalletUnfreeze");
    ValueTask<WalletFreezeWithTransferAck> IRpcHandler<WalletFreezeWithTransfer, WalletFreezeWithTransferAck>.HandleAsync(RpcContext<WalletFreezeWithTransfer> c, CancellationToken t) => throw Unexpected("WalletFreezeWithTransfer");
    ValueTask<WalletUnfreezeWithTransferAck> IRpcHandler<WalletUnfreezeWithTransfer, WalletUnfreezeWithTransferAck>.HandleAsync(RpcContext<WalletUnfreezeWithTransfer> c, CancellationToken t) => throw Unexpected("WalletUnfreezeWithTransfer");
    ValueTask<WalletBalanceAddAck> IRpcHandler<WalletBalanceAdd, WalletBalanceAddAck>.HandleAsync(RpcContext<WalletBalanceAdd> c, CancellationToken t) => throw Unexpected("WalletBalanceAdd");
    ValueTask<WalletBalanceSubtractAck> IRpcHandler<WalletBalanceSubtract, WalletBalanceSubtractAck>.HandleAsync(RpcContext<WalletBalanceSubtract> c, CancellationToken t) => throw Unexpected("WalletBalanceSubtract");

    private static InvalidOperationException Unexpected(string topic)
        => new($"compat/payment-wallet-query scenario only exercises WalletBalanceQuery; got {topic}");
}
