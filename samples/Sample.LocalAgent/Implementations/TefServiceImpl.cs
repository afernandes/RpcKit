using Microsoft.Extensions.Logging;
using Rpc.Contracts;
using Rpc.Core;
using Sample.Contracts;

namespace Sample.LocalAgent.Implementations;

public sealed class TefServiceImpl(ILogger<TefServiceImpl> logger) : ITefService
{
    // Defesa em profundidade: o router rejeita chamadas concorrentes para o mesmo agente
    // (via Exclusive=true no contrato), mas se outro processo na mesma máquina
    // abrir uma instância do CliSiTef, o lock do router não vê. Este semáforo
    // garante exclusão dentro deste processo.
    private readonly SemaphoreSlim _pinpadLock = new(initialCount: 1, maxCount: 1);

    public IDuplexCall<TefResponse, TefEvent> Charge(TefRequest initial, CancellationToken cancellationToken)
    {
        if (!_pinpadLock.Wait(0))
            throw new RpcBusyException("Outra transação TEF está em andamento neste pinpad.");

        var call = new DuplexCallChannel<TefResponse, TefEvent>(cancellationToken);
        _ = Task.Run(async () =>
        {
            try { await RunCliSiTefLoopAsync(initial, call); }
            finally { _pinpadLock.Release(); }
        }, call.Token);
        return call;
    }

    private async Task RunCliSiTefLoopAsync(
        TefRequest request,
        DuplexCallChannel<TefResponse, TefEvent> call)
    {
        var ct = call.Token;
        try
        {
            logger.LogInformation(
                "TEF: starting transaction Amount={Amount} Operator={Operator} Ref={Ref}",
                request.Amount, request.OperatorId, request.MerchantReference);

            await call.Emit.WriteAsync(new TefDisplayMessage("Iniciando comunicação com SiTef..."), ct);
            await Task.Delay(200, ct);
            await call.Emit.WriteAsync(new TefDisplayMessage("Aguardando cartão"), ct);

            await call.Emit.WriteAsync(new TefCollectInput("Informe o documento do portador (CPF):", 11, 11), ct);
            var cpfResp = await call.Received.ReadAsync(ct);
            logger.LogInformation("TEF: CPF received: {Cpf}", Mask(cpfResp.Text));

            await call.Emit.WriteAsync(new TefMenu("Selecione a forma:", ["Crédito à vista", "Crédito parcelado", "Débito"]), ct);
            var menuResp = await call.Received.ReadAsync(ct);
            logger.LogInformation("TEF: payment method selected: {Choice}", menuResp.Text);

            if (menuResp.Text == "9")
            {
                await call.Emit.WriteAsync(new TefError(-15, "Operação cancelada pelo operador."), ct);
                return;
            }

            await call.Emit.WriteAsync(new TefDisplayMessage("Insira ou aproxime o cartão no pinpad"), ct);
            await Task.Delay(800, ct);
            await call.Emit.WriteAsync(new TefDisplayMessage("Digite a senha no pinpad"), ct);
            await Task.Delay(600, ct);
            await call.Emit.WriteAsync(new TefDisplayMessage("Processando..."), ct);
            await Task.Delay(400, ct);

            await call.Emit.WriteAsync(new TefConfirm("Imprimir via do cliente? Confirme após impressão (S/N)"), ct);
            var confirmResp = await call.Received.ReadAsync(ct);
            if (!confirmResp.Text.Equals("S", StringComparison.OrdinalIgnoreCase))
            {
                await call.Emit.WriteAsync(new TefError(-1, "Cliente recusou confirmação pós-impressão. Transação desfeita."), ct);
                return;
            }

            var nsu = Random.Shared.Next(100000, 999999).ToString();
            var auth = Random.Shared.Next(100000, 999999).ToString();
            var receipt = $"========================\n" +
                          $" CUPOM TEF (simulação)\n" +
                          $" Valor: R$ {request.Amount:0.00}\n" +
                          $" NSU: {nsu}\n" +
                          $" Auth: {auth}\n" +
                          $"========================";

            await call.Emit.WriteAsync(new TefCompleted(nsu, auth, receipt), ct);
            logger.LogInformation("TEF: completed NSU={Nsu} Auth={Auth}", nsu, auth);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("TEF: transaction cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TEF: failure in CliSiTef loop");
            try { await call.Emit.WriteAsync(new TefError(-999, ex.Message), CancellationToken.None); } catch { }
        }
        finally
        {
            call.Emit.Complete();
        }
    }

    private static string Mask(string s) => s.Length <= 4 ? s : s[..3] + new string('*', s.Length - 3);
}
