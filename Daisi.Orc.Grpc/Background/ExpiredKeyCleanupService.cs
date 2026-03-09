using Daisi.Orc.Core.Data.Db;

namespace Daisi.Orc.Grpc.Background
{
    /// <summary>
    /// Background service that periodically deletes expired client keys from the database.
    /// Cosmos DB TTL is not enabled on client keys, so this service handles cleanup.
    /// </summary>
    public class ExpiredKeyCleanupService(
        ILogger<ExpiredKeyCleanupService> logger,
        IServiceProvider serviceProvider) : BackgroundService
    {
        private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(StartupDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CleanupExpiredKeysAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in ExpiredKeyCleanupService");
                }

                await Task.Delay(Interval, stoppingToken);
            }

            logger.LogCritical("Exit Expired Key Cleanup Service");
        }

        private async Task CleanupExpiredKeysAsync()
        {
            logger.LogInformation("Starting expired client key cleanup");

            using var scope = serviceProvider.CreateScope();
            var cosmo = scope.ServiceProvider.GetRequiredService<Cosmo>();

            var deletedCount = await cosmo.DeleteExpiredClientKeysAsync();

            logger.LogInformation("Expired client key cleanup complete — deleted {Count} keys", deletedCount);
        }
    }
}
