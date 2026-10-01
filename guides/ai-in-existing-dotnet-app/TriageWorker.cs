using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiInExistingApp;

// --- guide snippet starts (background processing) ---
public sealed class TriageWorker(
    ChannelReader<IncomingRequest> queue,
    IServiceScopeFactory scopes,
    ILogger<TriageWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (IncomingRequest request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(request, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One failed message must not stop the worker. The request stays untreated and visible.
                logger.LogError(exception, "Triage failed for request {RequestId}", request.Id);
            }
        }
    }

    public async Task ProcessAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IRequestStore store = scope.ServiceProvider.GetRequiredService<IRequestStore>();

        // Idempotence: a message delivered twice is triaged, and billed, once.
        if (!await store.TryBeginAsync(request.Id, cancellationToken))
        {
            return;
        }

        RequestTriageService triage = scope.ServiceProvider.GetRequiredService<RequestTriageService>();
        TriageDecision decision = await triage.TriageAsync(request, cancellationToken);
        await store.SaveAsync(request.Id, decision, cancellationToken);
    }
}
// --- guide snippet ends ---
