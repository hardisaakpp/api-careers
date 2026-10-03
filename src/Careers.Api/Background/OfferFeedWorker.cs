using Careers.Application.Offers;

namespace Careers.Api.Background;

/// <summary>Looks for new offers on its own. The capture service still enforces each source's minimum interval.</summary>
public sealed class OfferFeedWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<OfferFeedWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var every = TimeSpan.FromMinutes(configuration.GetValue("OfferFeeds:CheckIntervalMinutes", 60));
        using var timer = new PeriodicTimer(every);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OfferCaptureService>().RunAsync(force: false, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Offer capture failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
