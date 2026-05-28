using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sample.Contracts;

namespace Sample.LocalAgent.Implementations;

/// <summary>
/// Impressão é por natureza sequencial num driver físico — enfileira e processa
/// um job de cada vez. Caller espera pelo SEU job sair pela impressora (TCS),
/// sem bloquear outros callers. Backpressure por canal bounded: o 33° request
/// concorrente espera o canal abrir.
/// </summary>
public sealed class PrinterServiceImpl : IPrinterService, IHostedService, IAsyncDisposable
{
    private readonly ILogger<PrinterServiceImpl> _logger;
    private readonly Channel<QueuedJob> _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _worker;

    public PrinterServiceImpl(ILogger<PrinterServiceImpl> logger)
    {
        _logger = logger;
        _queue = Channel.CreateBounded<QueuedJob>(
            new BoundedChannelOptions(capacity: 32) { FullMode = BoundedChannelFullMode.Wait });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _worker = Task.Run(ProcessQueueAsync, _shutdown.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdown.Cancel();
        _queue.Writer.TryComplete();
        if (_worker is not null)
        {
            try { await _worker.WaitAsync(cancellationToken); }
            catch { /* swallow */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _shutdown.Dispose();
    }

    public async Task<PrintResult> PrintAsync(PrintJob job, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<PrintResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = new QueuedJob(job, tcs, cancellationToken);

        await _queue.Writer.WriteAsync(queued, cancellationToken);
        await using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return await tcs.Task;
    }

    private async Task ProcessQueueAsync()
    {
        var ct = _shutdown.Token;
        try
        {
            await foreach (var (job, tcs, jobCt) in _queue.Reader.ReadAllAsync(ct))
            {
                if (jobCt.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(jobCt);
                    continue;
                }
                try
                {
                    _logger.LogInformation("Printing '{Title}' x{Copies}", job.Title, job.Copies);
                    await Task.Delay(150, jobCt); // simula trabalho do driver ESC/POS
                    tcs.TrySetResult(new PrintResult(true, Environment.MachineName + ":sim", null));
                }
                catch (OperationCanceledException) { tcs.TrySetCanceled(jobCt); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Print job failed");
                    tcs.TrySetResult(new PrintResult(false, null, ex.Message));
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    private readonly record struct QueuedJob(PrintJob Job, TaskCompletionSource<PrintResult> Tcs, CancellationToken Ct);
}
