using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Services;

namespace Daisi.Orc.Grpc.Background;

public class ToolExecutionBillingService(ILogger<ToolExecutionBillingService> logger, IServiceProvider serviceProvider) : BackgroundService
{
    private const int IntervalSeconds = 60;
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before starting so the app fully initializes
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessUnbilledExecutionsAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing tool execution billing");
            }

            await Task.Delay(TimeSpan.FromSeconds(IntervalSeconds), stoppingToken);
        }
    }

    private async Task ProcessUnbilledExecutionsAsync()
    {
        using var scope = serviceProvider.CreateScope();
        var cosmo = scope.ServiceProvider.GetRequiredService<Cosmo>();
        var creditService = scope.ServiceProvider.GetRequiredService<CreditService>();

        var records = await cosmo.GetUnprocessedToolExecutionsAsync(BatchSize);
        if (records.Count == 0)
            return;

        logger.LogInformation("Processing {Count} unbilled tool executions", records.Count);

        // Group by ConsumerAccountId
        var grouped = records.GroupBy(r => r.ConsumerAccountId);

        foreach (var group in grouped)
        {
            var accountId = group.Key;
            var executions = group.ToList();
            var totalCost = executions.Sum(e => e.ExecutionCost);

            if (totalCost <= 0)
            {
                // Free executions — mark as processed with no transaction
                var freeIds = executions.Select(e => e.Id).ToList();
                await cosmo.MarkToolExecutionsProcessedAsync(freeIds, accountId, "free");
                continue;
            }

            var description = $"Tool execution billing: {executions.Count} execution(s), {totalCost} credits";
            var transaction = await creditService.SpendToolExecutionCreditsAsync(accountId, totalCost, executions.Count, description);

            if (transaction is not null)
            {
                var ids = executions.Select(e => e.Id).ToList();
                await cosmo.MarkToolExecutionsProcessedAsync(ids, accountId, transaction.Id);
                logger.LogInformation("Billed account {AccountId}: {Count} executions, {Cost} credits, txn {TransactionId}",
                    accountId, executions.Count, totalCost, transaction.Id);
            }
            else
            {
                logger.LogWarning("Insufficient balance for account {AccountId}: {Cost} credits for {Count} executions (will retry next cycle)",
                    accountId, totalCost, executions.Count);
            }
        }
    }
}
