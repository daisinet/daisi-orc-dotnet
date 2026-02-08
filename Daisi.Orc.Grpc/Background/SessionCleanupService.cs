
using Daisi.Orc.Grpc.CommandServices.Containers;

namespace Daisi.Orc.Grpc.Background
{
    public class SessionCleanupService(ILogger<SessionCleanupService> logger) : BackgroundService
    {
        protected async override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            DateTime start = DateTime.UtcNow;

            while (!Program.App.Lifetime.ApplicationStopping.IsCancellationRequested)
            {
                if ((DateTime.UtcNow - start).TotalSeconds > 300)
                {
                    var sessions = SessionContainer.CleanUpExpired();
                    logger.LogInformation($"Cleaning Up Sessions");

                    if (sessions.Count > 0)
                    {
                        logger.LogDebug($"Cleaned Up {sessions.Count} Sessions");
                    }
                    else
                        logger.LogInformation($"No Sessions need cleanup");

                    start = DateTime.UtcNow;
                }
            }

            logger.LogCritical($"Exit Session Cleanup");

        }
    }
}
