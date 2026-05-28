using Rpc.Contracts;
using Rpc.Core;
using Sample.Contracts;

namespace Rpc.IntegrationTests.Agents;

public sealed class TestTefServiceImpl : ITefService
{
    public Action<DuplexCallChannel<TefResponse, TefEvent>>? OnStart { get; init; }
    public bool StallUntilCancelled { get; set; }

    public IDuplexCall<TefResponse, TefEvent> Charge(TefRequest initial, CancellationToken cancellationToken)
    {
        var call = new DuplexCallChannel<TefResponse, TefEvent>(cancellationToken);
        _ = Task.Run(() => RunAsync(initial, call), call.Token);
        return call;
    }

    private async Task RunAsync(TefRequest req, DuplexCallChannel<TefResponse, TefEvent> call)
    {
        var ct = call.Token;
        OnStart?.Invoke(call);
        try
        {
            await call.Emit.WriteAsync(new TefDisplayMessage("Aguardando cartão"), ct);

            if (StallUntilCancelled)
            {
                // segura o duplex aberto até o caller cancelar — usado pelo teste de Exclusive
                await Task.Delay(Timeout.Infinite, ct);
                return;
            }

            await call.Emit.WriteAsync(new TefCollectInput("CPF?", 11, 11), ct);
            var cpfReply = await call.Received.ReadAsync(ct);

            await call.Emit.WriteAsync(new TefMenu("Pagamento", ["Crédito", "Débito"]), ct);
            var menuReply = await call.Received.ReadAsync(ct);

            await call.Emit.WriteAsync(new TefConfirm("Confirmar?"), ct);
            var confirmReply = await call.Received.ReadAsync(ct);
            if (!confirmReply.Text.Equals("S", StringComparison.OrdinalIgnoreCase))
            {
                await call.Emit.WriteAsync(new TefError(-1, "Cliente recusou."), ct);
                return;
            }

            await call.Emit.WriteAsync(new TefCompleted("123456", "AUTH-001", $"Valor R$ {req.Amount:0.00}"), ct);
        }
        catch (OperationCanceledException) { /* expected on cancel */ }
        finally { call.Emit.Complete(); }
    }
}

public sealed class TestPosTerminal(string deviceId) : IPosTerminal
{
    public Task<PingResult> PingAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PingResult(deviceId, DateTimeOffset.UtcNow, "ok"));
}

public sealed class TestPrinterService(string name) : IPrinterService
{
    public List<PrintJob> Jobs { get; } = new();

    public Task<PrintResult> PrintAsync(PrintJob job, CancellationToken cancellationToken)
    {
        Jobs.Add(job);
        return Task.FromResult(new PrintResult(true, name, null));
    }
}
