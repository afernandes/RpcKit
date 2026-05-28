using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Rpc.Core;
using Rpc.SignalR.Client;
using Sample.Contracts;
using Sample.Contracts.Generated;

namespace Rpc.IntegrationTests;

public sealed class RpcTestFixture : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSignalR(o => o.EnableDetailedErrors = true);
        });
    }

    public async Task<HubConnection> ConnectAsync(
        string deviceId,
        string? userId = null,
        string? tenantId = null,
        IEnumerable<Capability>? capabilities = null,
        IDispatcherRegistry? agentDispatchers = null,
        CancellationToken cancellationToken = default)
    {
        var qs = new List<string> { "deviceId=" + Uri.EscapeDataString(deviceId) };
        if (userId is not null) qs.Add("userId=" + Uri.EscapeDataString(userId));
        if (tenantId is not null) qs.Add("tenantId=" + Uri.EscapeDataString(tenantId));
        if (capabilities is not null)
            qs.Add("caps=" + Uri.EscapeDataString(string.Join(",", capabilities.Select(c => c.Name))));

        var url = Server.BaseAddress + "rpc?" + string.Join("&", qs);

        var connection = new HubConnectionBuilder()
            .WithUrl(url, o => o.HttpMessageHandlerFactory = _ => Server.CreateHandler())
            .Build();

        if (agentDispatchers is not null)
            WireUpAgentHandlers(connection, agentDispatchers);

        await connection.StartAsync(cancellationToken);
        return connection;
    }

    public IRpcInvoker InvokerFor(HubConnection connection) => new SignalRRpcInvoker(connection);

    public IRpcClient ClientFor(HubConnection connection)
    {
        var proxies = new ProxyRegistry();
        proxies.Register(typeof(IOrderService), (inv, addr) => new OrderServiceProxy(inv, addr));
        proxies.Register(typeof(IPosTerminal), (inv, addr) => new PosTerminalProxy(inv, addr));
        proxies.Register(typeof(IPrinterService), (inv, addr) => new PrinterServiceProxy(inv, addr));
        proxies.Register(typeof(ITefService), (inv, addr) => new TefServiceProxy(inv, addr));
        return new RpcClient(InvokerFor(connection), proxies);
    }

    private static void WireUpAgentHandlers(HubConnection connection, IDispatcherRegistry registry)
    {
        var sessions = new AgentSessions();

        connection.On<InvokeEnvelope, InvokeResponse>("HandleInvoke", async env =>
        {
            try
            {
                var d = registry.GetInvoke(env.ContractId, env.MethodId)
                    ?? throw new RpcRouteNotFoundException($"No handler for {env.ContractId}#{env.MethodId}.");
                var result = await d(env.PayloadJson, CancellationToken.None);
                return new InvokeResponse(true, result, null);
            }
            catch (RpcException rex)
            {
                return new InvokeResponse(false, null,
                    new RpcErrorInfo(rex.Kind, rex.Message, rex.RemoteTypeName, rex.Detail));
            }
            catch (Exception ex)
            {
                return new InvokeResponse(false, null,
                    new RpcErrorInfo("RemoteException", ex.Message, ex.GetType().FullName, ex.ToString()));
            }
        });

        connection.On<string, string, int, string>("ServeDuplex",
            (corr, contractId, methodId, initialJson) =>
        {
            var dispatcher = registry.GetDuplex(contractId, methodId);
            if (dispatcher is null) return Task.CompletedTask;

            var upstream = Channel.CreateUnbounded<string>();
            var downstream = Channel.CreateUnbounded<string>();
            var cts = new CancellationTokenSource();
            sessions.Add(corr, upstream, cts);

            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var json in downstream.Reader.ReadAllAsync(cts.Token))
                        await connection.InvokeAsync("SubmitDuplexResponse", corr, json, cts.Token);
                    await connection.InvokeAsync("CompleteDuplexResponse", corr, cts.Token);
                }
                catch (OperationCanceledException) { }
                catch { /* swallow */ }
            }, cts.Token);

            _ = Task.Run(async () =>
            {
                try { await dispatcher(initialJson, upstream.Reader, downstream.Writer, cts.Token); }
                catch (Exception ex) { downstream.Writer.TryComplete(ex); }
                finally { downstream.Writer.TryComplete(); sessions.Remove(corr); }
            }, cts.Token);

            return Task.CompletedTask;
        });

        connection.On<string, string>("DuplexUpstream", async (corr, json) =>
        {
            if (sessions.TryGet(corr, out var s))
                await s.Upstream.Writer.WriteAsync(json, s.Cts.Token);
        });

        connection.On<string>("DuplexComplete", corr =>
        {
            if (sessions.TryGet(corr, out var s)) s.Upstream.Writer.TryComplete();
            return Task.CompletedTask;
        });

        connection.On<string>("DuplexCancel", corr =>
        {
            sessions.Remove(corr);
            return Task.CompletedTask;
        });
    }

    private sealed class AgentSessions
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (Channel<string> Upstream, CancellationTokenSource Cts)> _map = new();

        public void Add(string corr, Channel<string> upstream, CancellationTokenSource cts) =>
            _map[corr] = (upstream, cts);

        public bool TryGet(string corr, out (Channel<string> Upstream, CancellationTokenSource Cts) session) =>
            _map.TryGetValue(corr, out session);

        public void Remove(string corr)
        {
            if (_map.TryRemove(corr, out var s))
            {
                try { s.Cts.Cancel(); } catch { }
                s.Upstream.Writer.TryComplete();
            }
        }
    }
}
