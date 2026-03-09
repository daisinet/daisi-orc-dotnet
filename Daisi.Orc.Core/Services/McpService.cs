using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Microsoft.Extensions.Logging;

namespace Daisi.Orc.Core.Services
{
    /// <summary>
    /// Business logic for MCP server management.
    /// Handles registration, updates, removal, and sync scheduling.
    /// </summary>
    public class McpService(Cosmo cosmo, ILogger<McpService> logger)
    {
        /// <summary>
        /// Registers a new MCP server for an account.
        /// </summary>
        public async Task<McpServerRecord> RegisterServerAsync(
            string accountId, string createdByUserId, string createdByUserName,
            string name, string serverUrl, string authType, string authSecretEncrypted,
            int syncIntervalMinutes, string repositoryId = "")
        {
            var server = new McpServerRecord
            {
                AccountId = accountId,
                Name = name,
                ServerUrl = serverUrl,
                AuthType = authType,
                AuthSecretEncrypted = authSecretEncrypted,
                SyncIntervalMinutes = syncIntervalMinutes > 0 ? syncIntervalMinutes : 60,
                RepositoryId = repositoryId,
                CreatedByUserId = createdByUserId,
                CreatedByUserName = createdByUserName,
                Status = "PENDING"
            };

            var created = await cosmo.CreateMcpServerAsync(server);
            logger.LogInformation("Registered MCP server {Name} ({Id}) for account {AccountId}",
                name, created.Id, accountId);
            return created;
        }

        /// <summary>
        /// Gets all MCP servers for an account.
        /// </summary>
        public async Task<List<McpServerRecord>> GetServersAsync(string accountId)
        {
            return await cosmo.GetMcpServersByAccountAsync(accountId);
        }

        /// <summary>
        /// Gets a specific MCP server.
        /// </summary>
        public async Task<McpServerRecord?> GetServerAsync(string serverId, string accountId)
        {
            return await cosmo.GetMcpServerAsync(serverId, accountId);
        }

        /// <summary>
        /// Updates an MCP server's configuration.
        /// </summary>
        public async Task<McpServerRecord> UpdateServerAsync(McpServerRecord server)
        {
            return await cosmo.UpdateMcpServerAsync(server);
        }

        /// <summary>
        /// Removes an MCP server and its data.
        /// </summary>
        public async Task RemoveServerAsync(string serverId, string accountId)
        {
            await cosmo.DeleteMcpServerAsync(serverId, accountId);
            logger.LogInformation("Removed MCP server {ServerId} for account {AccountId}", serverId, accountId);
        }

        /// <summary>
        /// Updates sync status for a server.
        /// </summary>
        public async Task UpdateSyncStatusAsync(string serverId, string accountId, string status,
            string? lastError = null, int? resourcesSynced = null)
        {
            await cosmo.PatchMcpServerStatusAsync(serverId, accountId, status,
                DateTime.UtcNow, lastError, resourcesSynced);
        }

        /// <summary>
        /// Gets servers that are due for a sync based on their interval.
        /// </summary>
        public async Task<List<McpServerRecord>> GetServersDueForSyncAsync()
        {
            return await cosmo.GetServersDueForSyncAsync();
        }
    }
}
