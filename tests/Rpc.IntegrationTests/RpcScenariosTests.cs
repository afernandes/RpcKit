using Rpc.Core;
using Rpc.IntegrationTests.Agents;
using Sample.Contracts;
using Sample.Contracts.Generated;

namespace Rpc.IntegrationTests;

public class RpcScenariosTests
{
    [Fact]
    public async Task Caller_invokes_server_side_handler_via_RpcAddress_Server()
    {
        await using var fixture = new RpcTestFixture();

        await using var caller = await fixture.ConnectAsync(deviceId: "caller-1");
        var rpc = fixture.ClientFor(caller);
        var svc = rpc.For<IOrderService>(RpcAddress.Server);

        var result = await svc.GetOpenOrdersAsync(default);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Orders);
    }

    [Fact]
    public async Task Caller_invokes_agent_via_RpcAddress_Device()
    {
        await using var fixture = new RpcTestFixture();

        var agentDispatchers = new DispatcherRegistry();
        PosTerminalDispatcher.Register(agentDispatchers, new TestPosTerminal("agent-A"));

        await using var agent = await fixture.ConnectAsync(
            deviceId: "agent-A",
            capabilities: [Capability.PosTerminal],
            agentDispatchers: agentDispatchers);

        await using var caller = await fixture.ConnectAsync(deviceId: "caller-1");
        var rpc = fixture.ClientFor(caller);
        var pos = rpc.For<IPosTerminal>(RpcAddress.Device("agent-A"));

        var pong = await pos.PingAsync(default);
        Assert.Equal("agent-A", pong.DeviceId);
        Assert.Equal("ok", pong.Status);
    }

    [Fact]
    public async Task Caller_invokes_agent_via_RpcAddress_Role_for_capability()
    {
        await using var fixture = new RpcTestFixture();

        var printerDispatchers = new DispatcherRegistry();
        var printer = new TestPrinterService("kitchen-01");
        PrinterServiceDispatcher.Register(printerDispatchers, printer);

        await using var agent = await fixture.ConnectAsync(
            deviceId: "kitchen-01",
            tenantId: "tenantX",
            capabilities: [Capability.PrinterKitchen],
            agentDispatchers: printerDispatchers);

        await using var caller = await fixture.ConnectAsync(deviceId: "caller-1");
        var rpc = fixture.ClientFor(caller);
        var p = rpc.For<IPrinterService>(RpcAddress.Role(Capability.PrinterKitchen, "tenantX"));

        var result = await p.PrintAsync(new PrintJob("Pedido 1", "1x cafe"), default);
        Assert.True(result.Success);
        Assert.Single(printer.Jobs);
    }

    [Fact]
    public async Task Caller_invokes_agent_via_RpcAddress_User_for_user_plus_capability()
    {
        await using var fixture = new RpcTestFixture();

        var tefDispatchers = new DispatcherRegistry();
        TefServiceDispatcher.Register(tefDispatchers, new TestTefServiceImpl());

        await using var agent = await fixture.ConnectAsync(
            deviceId: "pdv-01",
            userId: "operator-1",
            capabilities: [Capability.Tef],
            agentDispatchers: tefDispatchers);

        await using var caller = await fixture.ConnectAsync(deviceId: "blazor-session-1");
        var rpc = fixture.ClientFor(caller);
        var tef = rpc.For<ITefService>(RpcAddress.User("operator-1", Capability.Tef));

        await using var call = tef.Charge(new TefRequest(50m, "operator-1", "REF1"), default);

        var seen = new List<TefEvent>();
        var done = new TaskCompletionSource<TefCompleted>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var ev in call.Responses)
                {
                    seen.Add(ev);
                    switch (ev)
                    {
                        case TefCollectInput: await call.SendAsync(new TefResponse("12345678901")); break;
                        case TefMenu: await call.SendAsync(new TefResponse("1")); break;
                        case TefConfirm: await call.SendAsync(new TefResponse("S")); break;
                        case TefCompleted ok: done.TrySetResult(ok); break;
                        case TefError err: done.TrySetException(new InvalidOperationException(err.Message)); break;
                    }
                }
            }
            catch (Exception ex) { done.TrySetException(ex); }
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        timeout.Token.Register(() => done.TrySetCanceled());
        var completed = await done.Task;

        Assert.Equal("123456", completed.Nsu);
        Assert.Contains(seen, e => e is TefDisplayMessage);
        Assert.Contains(seen, e => e is TefCollectInput);
        Assert.Contains(seen, e => e is TefMenu);
        Assert.Contains(seen, e => e is TefConfirm);
    }

    [Fact]
    public async Task Tef_caller_cancellation_propagates_to_agent_loop()
    {
        await using var fixture = new RpcTestFixture();

        var agentCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var impl = new TestTefServiceImpl
        {
            OnStart = call => call.Token.Register(() => agentCancelled.TrySetResult(true))
        };
        var tefDispatchers = new DispatcherRegistry();
        TefServiceDispatcher.Register(tefDispatchers, impl);

        await using var agent = await fixture.ConnectAsync(
            deviceId: "pdv-02", userId: "u2",
            capabilities: [Capability.Tef], agentDispatchers: tefDispatchers);

        await using var caller = await fixture.ConnectAsync(deviceId: "blazor-2");
        var rpc = fixture.ClientFor(caller);
        var tef = rpc.For<ITefService>(RpcAddress.User("u2", Capability.Tef));

        using var cts = new CancellationTokenSource();
        await using var call = tef.Charge(new TefRequest(10m, "u2", "X"), cts.Token);

        var enumerator = call.Responses.GetAsyncEnumerator(cts.Token);
        try
        {
            Assert.True(await enumerator.MoveNextAsync()); // first message
        }
        finally { await enumerator.DisposeAsync(); }

        cts.Cancel();

        var won = await Task.WhenAny(agentCancelled.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(agentCancelled.Task, won);
        Assert.True(await agentCancelled.Task);
    }

    [Fact]
    public async Task Invoke_to_offline_device_throws_RouteNotFound()
    {
        await using var fixture = new RpcTestFixture();
        await using var caller = await fixture.ConnectAsync(deviceId: "caller-x");
        var rpc = fixture.ClientFor(caller);
        var pos = rpc.For<IPosTerminal>(RpcAddress.Device("nobody-here"));

        var ex = await Assert.ThrowsAsync<RpcRouteNotFoundException>(() => pos.PingAsync(default));
        Assert.Equal("RouteNotFound", ex.Kind);
    }

    [Fact]
    public async Task Exclusive_duplex_rejects_second_concurrent_call_with_Busy_without_hitting_agent()
    {
        await using var fixture = new RpcTestFixture();

        var agentInvocations = 0;
        var implGate = new SemaphoreSlim(0, 1);
        var impl = new TestTefServiceImpl
        {
            OnStart = _ =>
            {
                Interlocked.Increment(ref agentInvocations);
                implGate.Release();
            },
            StallUntilCancelled = true
        };
        var tefDispatchers = new DispatcherRegistry();
        TefServiceDispatcher.Register(tefDispatchers, impl);

        await using var agent = await fixture.ConnectAsync(
            deviceId: "pdv-busy",
            userId: "operator-busy",
            capabilities: [Capability.Tef],
            agentDispatchers: tefDispatchers);

        await using var caller = await fixture.ConnectAsync(deviceId: "blazor-busy");
        var rpc = fixture.ClientFor(caller);
        var tef = rpc.For<ITefService>(RpcAddress.User("operator-busy", Capability.Tef));

        // 1ª chamada — abre o duplex e fica aguardando dentro do impl.
        var first = tef.Charge(new TefRequest(10m, "operator-busy", "REF-1"), default);
        await using var firstScope = first;
        var iterator = first.Responses.GetAsyncEnumerator();
        await iterator.MoveNextAsync();    // garante que o duplex está estabelecido
        Assert.True(await implGate.WaitAsync(TimeSpan.FromSeconds(5)),
            "impl não foi invocada na 1ª chamada");

        // 2ª chamada concorrente — deve falhar com Busy SEM disparar o impl novamente.
        var second = tef.Charge(new TefRequest(20m, "operator-busy", "REF-2"), default);
        var ex = await Assert.ThrowsAsync<RpcBusyException>(async () =>
        {
            await foreach (var _ in second.Responses) { /* nada deve sair */ }
        });
        Assert.Equal("Busy", ex.Kind);
        Assert.Equal(1, agentInvocations);   // a segunda foi rejeitada no router

        // Após dispor a primeira, o lock deve liberar e uma terceira chamada deve ir adiante.
        await iterator.DisposeAsync();
        await firstScope.DisposeAsync();

        impl.StallUntilCancelled = false;   // permite que a próxima complete normalmente

        // dá um respiro para o cleanup do duplex se propagar
        await Task.Delay(200);

        var third = tef.Charge(new TefRequest(30m, "operator-busy", "REF-3"), default);
        await using var thirdScope = third;
        var thirdIter = third.Responses.GetAsyncEnumerator();
        await thirdIter.MoveNextAsync();
        await thirdIter.DisposeAsync();
        Assert.Equal(2, agentInvocations);
    }
}
