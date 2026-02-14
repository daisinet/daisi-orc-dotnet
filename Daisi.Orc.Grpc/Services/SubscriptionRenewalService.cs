using Daisi.Orc.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Daisi.Orc.Grpc.Services;

/// <summary>
/// Background service that processes marketplace subscription renewals daily.
/// </summary>
public class SubscriptionRenewalService(IServiceScopeFactory scopeFactory, ILogger<SubscriptionRenewalService> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Subscription renewal service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRenewalsAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing subscription renewals");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessRenewalsAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var marketplaceService = scope.ServiceProvider.GetRequiredService<MarketplaceService>();

        var renewed = await marketplaceService.ProcessSubscriptionRenewalsAsync();
        logger.LogInformation("Processed subscription renewals: {Count} renewed", renewed);

        var premiumRenewed = await marketplaceService.ProcessPremiumRenewalsAsync();
        logger.LogInformation("Processed premium provider renewals: {Count} renewed", premiumRenewed);
    }
}
