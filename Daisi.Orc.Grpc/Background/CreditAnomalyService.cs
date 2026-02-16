using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Grpc.CommandServices.Containers;
using System.Text.Json;

namespace Daisi.Orc.Grpc.Background
{
    /// <summary>
    /// Background service that runs every 30 minutes to detect suspicious credit patterns.
    /// Flags anomalies for admin review — does not auto-penalize.
    /// </summary>
    public class CreditAnomalyService(
        ILogger<CreditAnomalyService> logger,
        IServiceProvider serviceProvider) : BackgroundService
    {
        private const int ScanIntervalMinutes = 30;

        /// <summary>
        /// Token count threshold multiplier. If a host's average token count per inference
        /// exceeds this multiple of the network-wide median, flag it.
        /// </summary>
        private const double TokenInflationThresholdMultiplier = 10.0;

        /// <summary>
        /// If a host submits more receipts in the last hour than this multiple of its
        /// 7-day hourly average, flag it.
        /// </summary>
        private const double ReceiptSpikeThresholdMultiplier = 3.0;

        /// <summary>
        /// Minimum days online with zero inferences before flagging.
        /// </summary>
        private const int ZeroWorkUptimeMinDays = 7;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Wait a bit on startup to let the system stabilize
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunAnomalyScanAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in CreditAnomalyService scan");
                }

                await Task.Delay(TimeSpan.FromMinutes(ScanIntervalMinutes), stoppingToken);
            }

            logger.LogCritical("Exit Credit Anomaly Service");
        }

        private async Task RunAnomalyScanAsync()
        {
            logger.LogInformation("Starting credit anomaly scan");

            using var scope = serviceProvider.CreateScope();
            var cosmo = scope.ServiceProvider.GetRequiredService<Cosmo>();

            await CheckInflatedTokenCountsAsync(cosmo);
            await CheckReceiptVolumeSpikesAsync(cosmo);
            await CheckZeroWorkUptimeAsync(cosmo);
            await CheckCircularCreditFlowAsync(cosmo);

            logger.LogInformation("Credit anomaly scan complete");
        }

        /// <summary>
        /// Flags hosts whose average token count per inference exceeds 10x the network-wide median.
        /// </summary>
        private async Task CheckInflatedTokenCountsAsync(Cosmo cosmo)
        {
            try
            {
                var hostsOnline = HostContainer.HostsOnline.Values.ToList();
                if (hostsOnline.Count == 0) return;

                var since = DateTime.UtcNow.AddDays(-1);
                var allAverages = new List<(string HostId, string AccountId, double Average, int Count)>();

                foreach (var hostOnline in hostsOnline)
                {
                    var stats = await cosmo.GetInferenceMessageStatsForHostAsync(hostOnline.Host.Id, since);
                    if (stats.Count == 0) continue;

                    var avg = stats.Average(s => s.TokenCount);
                    allAverages.Add((hostOnline.Host.Id, hostOnline.Host.AccountId, avg, stats.Count));
                }

                if (allAverages.Count < 2) return;

                var sorted = allAverages.OrderBy(a => a.Average).ToList();
                var median = sorted[sorted.Count / 2].Average;

                if (median <= 0) return;

                var threshold = median * TokenInflationThresholdMultiplier;

                foreach (var entry in allAverages.Where(a => a.Average > threshold))
                {
                    var details = JsonSerializer.Serialize(new
                    {
                        entry.Average,
                        NetworkMedian = median,
                        Threshold = threshold,
                        InferenceCount = entry.Count
                    });

                    await cosmo.CreateCreditAnomalyAsync(new CreditAnomaly
                    {
                        AccountId = entry.AccountId,
                        HostId = entry.HostId,
                        Type = AnomalyType.InflatedTokens,
                        Severity = entry.Average > threshold * 2 ? AnomalySeverity.High : AnomalySeverity.Medium,
                        Description = $"Host average token count ({entry.Average:F0}) is {entry.Average / median:F1}x the network median ({median:F0})",
                        Details = details
                    });

                    logger.LogWarning(
                        $"Anomaly flagged: InflatedTokens for host {entry.HostId} (account {entry.AccountId}), " +
                        $"avg={entry.Average:F0}, median={median:F0}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking inflated token counts");
            }
        }

        /// <summary>
        /// Flags hosts that submit more receipts in the last hour than 3x their 7-day hourly average.
        /// </summary>
        private async Task CheckReceiptVolumeSpikesAsync(Cosmo cosmo)
        {
            try
            {
                var hostsOnline = HostContainer.HostsOnline.Values.ToList();
                if (hostsOnline.Count == 0) return;

                var oneHourAgo = DateTime.UtcNow.AddHours(-1);
                var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);

                foreach (var hostOnline in hostsOnline)
                {
                    var recentStats = await cosmo.GetInferenceMessageStatsForHostAsync(hostOnline.Host.Id, oneHourAgo);
                    var weekStats = await cosmo.GetInferenceMessageStatsForHostAsync(hostOnline.Host.Id, sevenDaysAgo);

                    if (weekStats.Count == 0) continue;

                    double hoursOnline = (DateTime.UtcNow - sevenDaysAgo).TotalHours;
                    double weeklyHourlyAvg = weekStats.Count / hoursOnline;

                    if (weeklyHourlyAvg <= 0) continue;

                    double spikeRatio = recentStats.Count / weeklyHourlyAvg;

                    if (spikeRatio > ReceiptSpikeThresholdMultiplier && recentStats.Count >= 10)
                    {
                        var details = JsonSerializer.Serialize(new
                        {
                            LastHourCount = recentStats.Count,
                            WeeklyHourlyAvg = weeklyHourlyAvg,
                            SpikeRatio = spikeRatio
                        });

                        await cosmo.CreateCreditAnomalyAsync(new CreditAnomaly
                        {
                            AccountId = hostOnline.Host.AccountId,
                            HostId = hostOnline.Host.Id,
                            Type = AnomalyType.ReceiptVolumeSpike,
                            Severity = spikeRatio > ReceiptSpikeThresholdMultiplier * 2
                                ? AnomalySeverity.High
                                : AnomalySeverity.Medium,
                            Description = $"Host submitted {recentStats.Count} receipts in the last hour ({spikeRatio:F1}x its 7-day hourly average of {weeklyHourlyAvg:F1})",
                            Details = details
                        });

                        logger.LogWarning(
                            $"Anomaly flagged: ReceiptVolumeSpike for host {hostOnline.Host.Id}, " +
                            $"lastHour={recentStats.Count}, avgHourly={weeklyHourlyAvg:F1}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking receipt volume spikes");
            }
        }

        /// <summary>
        /// Flags hosts that have been online for 7+ days but processed zero inferences.
        /// </summary>
        private async Task CheckZeroWorkUptimeAsync(Cosmo cosmo)
        {
            try
            {
                var hostsOnline = HostContainer.HostsOnline.Values.ToList();
                if (hostsOnline.Count == 0) return;

                var cutoff = DateTime.UtcNow.AddDays(-ZeroWorkUptimeMinDays);

                foreach (var hostOnline in hostsOnline)
                {
                    if (hostOnline.Host.DateStarted == null || hostOnline.Host.DateStarted > cutoff)
                        continue;

                    var stats = await cosmo.GetInferenceMessageStatsForHostAsync(
                        hostOnline.Host.Id, hostOnline.Host.DateStarted);

                    if (stats.Count == 0)
                    {
                        int daysOnline = (int)(DateTime.UtcNow - hostOnline.Host.DateStarted.Value).TotalDays;

                        var details = JsonSerializer.Serialize(new
                        {
                            DaysOnline = daysOnline,
                            DateStarted = hostOnline.Host.DateStarted
                        });

                        await cosmo.CreateCreditAnomalyAsync(new CreditAnomaly
                        {
                            AccountId = hostOnline.Host.AccountId,
                            HostId = hostOnline.Host.Id,
                            Type = AnomalyType.ZeroWorkUptime,
                            Severity = AnomalySeverity.Low,
                            Description = $"Host has been online for {daysOnline} days with zero inferences processed",
                            Details = details
                        });

                        logger.LogInformation(
                            $"Anomaly flagged: ZeroWorkUptime for host {hostOnline.Host.Id}, " +
                            $"online {daysOnline} days with 0 inferences");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking zero-work uptime");
            }
        }

        /// <summary>
        /// Flags pairs of accounts where hosts consistently serve each other's consumers,
        /// suggesting potential collusion.
        /// </summary>
        private async Task CheckCircularCreditFlowAsync(Cosmo cosmo)
        {
            try
            {
                var hostsOnline = HostContainer.HostsOnline.Values.ToList();
                if (hostsOnline.Count < 2) return;

                // Group hosts by account
                var accountHosts = hostsOnline
                    .GroupBy(h => h.Host.AccountId)
                    .Where(g => !string.IsNullOrEmpty(g.Key))
                    .ToDictionary(g => g.Key, g => g.Select(h => h.Host.Id).ToList());

                if (accountHosts.Count < 2) return;

                var since = DateTime.UtcNow.AddDays(-7);

                // Build a map of which accounts earned credits from which consumer accounts
                // by analyzing credit transactions with RelatedEntityId (InferenceId)
                var accountPairs = new Dictionary<string, Dictionary<string, int>>();

                foreach (var (accountId, hostIds) in accountHosts)
                {
                    var transactions = await cosmo.GetAllCreditTransactionsAsync(accountId);
                    var recentEarnings = transactions
                        .Where(t => t.Type == CreditTransactionType.TokenEarning
                                    && t.DateCreated >= since
                                    && !string.IsNullOrEmpty(t.RelatedEntityId))
                        .ToList();

                    if (recentEarnings.Count == 0) continue;

                    // For each earning, check if the corresponding spend came from another tracked account
                    // We can't directly see the consumer from the earning transaction,
                    // but we can cross-reference: if account B has a spend with the same RelatedEntityId
                    foreach (var otherAccountId in accountHosts.Keys.Where(a => a != accountId))
                    {
                        var otherTransactions = await cosmo.GetAllCreditTransactionsAsync(otherAccountId);
                        var matchingSpends = otherTransactions
                            .Where(t => t.Type == CreditTransactionType.InferenceSpend
                                        && t.DateCreated >= since
                                        && recentEarnings.Any(e => e.RelatedEntityId == t.RelatedEntityId))
                            .Count();

                        if (matchingSpends > 0)
                        {
                            if (!accountPairs.ContainsKey(accountId))
                                accountPairs[accountId] = new Dictionary<string, int>();

                            accountPairs[accountId][otherAccountId] = matchingSpends;
                        }
                    }
                }

                // Check for bidirectional flows: A serves B AND B serves A
                var flaggedPairs = new HashSet<string>();
                foreach (var (accountA, targets) in accountPairs)
                {
                    foreach (var (accountB, countAB) in targets)
                    {
                        var pairKey = string.Compare(accountA, accountB) < 0
                            ? $"{accountA}:{accountB}"
                            : $"{accountB}:{accountA}";

                        if (flaggedPairs.Contains(pairKey)) continue;

                        if (accountPairs.TryGetValue(accountB, out var bTargets)
                            && bTargets.TryGetValue(accountA, out var countBA))
                        {
                            flaggedPairs.Add(pairKey);

                            var details = JsonSerializer.Serialize(new
                            {
                                AccountA = accountA,
                                AccountB = accountB,
                                AtoB_Inferences = countAB,
                                BtoA_Inferences = countBA
                            });

                            // Flag both accounts
                            await cosmo.CreateCreditAnomalyAsync(new CreditAnomaly
                            {
                                AccountId = accountA,
                                Type = AnomalyType.CircularCreditFlow,
                                Severity = AnomalySeverity.High,
                                Description = $"Circular credit flow detected: this account and {accountB} are consistently serving each other's inferences ({countAB} + {countBA} cross-inferences in 7 days)",
                                Details = details
                            });

                            await cosmo.CreateCreditAnomalyAsync(new CreditAnomaly
                            {
                                AccountId = accountB,
                                Type = AnomalyType.CircularCreditFlow,
                                Severity = AnomalySeverity.High,
                                Description = $"Circular credit flow detected: this account and {accountA} are consistently serving each other's inferences ({countBA} + {countAB} cross-inferences in 7 days)",
                                Details = details
                            });

                            logger.LogWarning(
                                $"Anomaly flagged: CircularCreditFlow between {accountA} and {accountB}, " +
                                $"A→B={countAB}, B→A={countBA}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking circular credit flow");
            }
        }
    }
}
