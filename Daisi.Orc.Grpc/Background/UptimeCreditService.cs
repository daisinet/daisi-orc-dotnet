using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.CommandServices.Containers;
using System.Collections.Concurrent;

namespace Daisi.Orc.Grpc.Background
{
    public class UptimeCreditService(ILogger<UptimeCreditService> logger, IServiceProvider serviceProvider) : BackgroundService
    {
        /// <summary>
        /// Tracks the last time uptime credits were awarded for each host.
        /// </summary>
        private static readonly ConcurrentDictionary<string, DateTime> LastAwardTime = new();

        /// <summary>
        /// Interval between uptime credit awards (60 minutes).
        /// </summary>
        private const int AwardIntervalMinutes = 60;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            DateTime lastRun = DateTime.UtcNow;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if ((DateTime.UtcNow - lastRun).TotalMinutes >= AwardIntervalMinutes)
                    {
                        await AwardUptimeCreditsToOnlineHostsAsync();
                        lastRun = DateTime.UtcNow;
                    }

                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in UptimeCreditService");
                }
            }

            logger.LogCritical("Exit Uptime Credit Service");
        }

        private async Task AwardUptimeCreditsToOnlineHostsAsync()
        {
            var hostsOnline = HostContainer.HostsOnline.Values.ToList();

            if (hostsOnline.Count == 0)
            {
                logger.LogInformation("No hosts online for uptime credits");
                return;
            }

            using var scope = serviceProvider.CreateScope();
            var creditService = scope.ServiceProvider.GetRequiredService<CreditService>();

            foreach (var hostOnline in hostsOnline)
            {
                try
                {
                    var hostId = hostOnline.Host.Id;
                    var accountId = hostOnline.Host.AccountId;

                    if (string.IsNullOrWhiteSpace(accountId))
                        continue;

                    var now = DateTime.UtcNow;
                    int minutes = AwardIntervalMinutes;

                    if (LastAwardTime.TryGetValue(hostId, out var lastAwarded))
                    {
                        minutes = (int)(now - lastAwarded).TotalMinutes;
                    }

                    if (minutes <= 0)
                        continue;

                    await creditService.AwardUptimeCreditsAsync(accountId, hostId, minutes, hostOnline.Host.Name);
                    LastAwardTime[hostId] = now;

                    logger.LogInformation(
                        $"Awarded uptime credits to {hostOnline.Host.Name} ({accountId}) for {minutes} minutes");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Error awarding uptime credits to host {hostOnline.Host.Name}");
                }
            }
        }

        /// <summary>
        /// Called when a host unregisters to award partial-hour credits.
        /// </summary>
        public static async Task AwardPartialUptimeCreditsAsync(
            string hostId, string accountId, CreditService creditService, ILogger logger)
        {
            try
            {
                if (LastAwardTime.TryRemove(hostId, out var lastAwarded))
                {
                    int minutes = (int)(DateTime.UtcNow - lastAwarded).TotalMinutes;
                    if (minutes > 0)
                    {
                        await creditService.AwardUptimeCreditsAsync(accountId, hostId, minutes);
                        logger.LogInformation(
                            $"Awarded partial uptime credits to {accountId} for host {hostId}: {minutes} minutes");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error awarding partial uptime credits for host {hostId}");
            }
        }
    }
}
