using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Orc.Grpc.RPCServices.V1;

namespace Daisi.Orc.Grpc.Background
{
    /// <summary>
    /// Background service that polls for MCP servers due for sync
    /// and dispatches sync commands to available hosts.
    /// </summary>
    public class McpSyncSchedulerService(
        IServiceProvider serviceProvider,
        ILogger<McpSyncSchedulerService> logger) : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Wait for startup to complete
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndDispatchSyncsAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Error in MCP sync scheduler");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }
        }

        private async Task CheckAndDispatchSyncsAsync(CancellationToken ct)
        {
            using var scope = serviceProvider.CreateScope();
            var mcpService = scope.ServiceProvider.GetRequiredService<McpService>();

            var serversDue = await mcpService.GetServersDueForSyncAsync();
            if (serversDue.Count == 0) return;

            logger.LogInformation("Found {Count} MCP servers due for sync", serversDue.Count);

            foreach (var server in serversDue)
            {
                try
                {
                    // Find an online host for this account
                    var availableHost = HostContainer.HostsOnline.Values
                        .FirstOrDefault(h => h.Host.AccountId == server.AccountId);

                    if (availableHost == null)
                    {
                        logger.LogWarning("No host online for account {AccountId}, skipping MCP sync for {ServerName}",
                            server.AccountId, server.Name);
                        continue;
                    }

                    var command = McpRPC.BuildSyncCommand(server);
                    availableHost.OutgoingQueue.Writer.TryWrite(command);

                    await mcpService.UpdateSyncStatusAsync(server.Id, server.AccountId, "SYNCING");
                    logger.LogInformation("Dispatched MCP sync for {ServerName} to host {HostId}",
                        server.Name, availableHost.Host.Id);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to dispatch sync for MCP server {ServerName}", server.Name);
                    await mcpService.UpdateSyncStatusAsync(server.Id, server.AccountId, "ERROR",
                        lastError: ex.Message);
                }
            }
        }
    }
}
