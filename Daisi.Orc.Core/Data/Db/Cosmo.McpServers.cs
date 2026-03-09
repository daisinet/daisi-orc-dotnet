using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string McpServerIdPrefix = "mcp";
        public const string McpServersContainerName = "McpServers";
        public const string McpServersPartitionKeyName = nameof(McpServerRecord.AccountId);

        public virtual async Task<McpServerRecord> CreateMcpServerAsync(McpServerRecord server)
        {
            server.Id = GenerateId(McpServerIdPrefix);
            server.DateCreated = DateTime.UtcNow;
            var container = await GetContainerAsync(McpServersContainerName);
            var response = await container.CreateItemAsync(server, new PartitionKey(server.AccountId));
            return response.Resource;
        }

        public virtual async Task<McpServerRecord?> GetMcpServerAsync(string serverId, string accountId)
        {
            try
            {
                var container = await GetContainerAsync(McpServersContainerName);
                var response = await container.ReadItemAsync<McpServerRecord>(serverId, new PartitionKey(accountId));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public virtual async Task<List<McpServerRecord>> GetMcpServersByAccountAsync(string accountId)
        {
            var container = await GetContainerAsync(McpServersContainerName);
            var query = container.GetItemLinqQueryable<McpServerRecord>()
                .Where(s => s.AccountId == accountId)
                .ToFeedIterator();

            var servers = new List<McpServerRecord>();
            while (query.HasMoreResults)
            {
                var response = await query.ReadNextAsync();
                servers.AddRange(response);
            }
            return servers;
        }

        public virtual async Task<McpServerRecord> UpdateMcpServerAsync(McpServerRecord server)
        {
            var container = await GetContainerAsync(McpServersContainerName);
            var response = await container.UpsertItemAsync(server, new PartitionKey(server.AccountId));
            return response.Resource;
        }

        public virtual async Task DeleteMcpServerAsync(string serverId, string accountId)
        {
            var container = await GetContainerAsync(McpServersContainerName);
            await container.DeleteItemAsync<McpServerRecord>(serverId, new PartitionKey(accountId));
        }

        public virtual async Task<List<McpServerRecord>> GetServersDueForSyncAsync()
        {
            var container = await GetContainerAsync(McpServersContainerName);
            var query = container.GetItemLinqQueryable<McpServerRecord>()
                .Where(s => s.Status == "CONNECTED" || s.Status == "PENDING")
                .ToFeedIterator();

            var now = DateTime.UtcNow;
            var servers = new List<McpServerRecord>();
            while (query.HasMoreResults)
            {
                var response = await query.ReadNextAsync();
                servers.AddRange(response.Where(s =>
                    !s.DateLastSync.HasValue
                    || (now - s.DateLastSync.Value).TotalMinutes >= s.SyncIntervalMinutes));
            }
            return servers;
        }

        public virtual async Task PatchMcpServerStatusAsync(string serverId, string accountId, string status,
            DateTime? dateLastSync = null, string? lastError = null, int? resourcesSynced = null)
        {
            var container = await GetContainerAsync(McpServersContainerName);
            var operations = new List<PatchOperation>
            {
                PatchOperation.Set("/Status", status)
            };
            if (dateLastSync.HasValue)
                operations.Add(PatchOperation.Set("/DateLastSync", dateLastSync.Value));
            if (lastError != null)
                operations.Add(PatchOperation.Set("/LastError", lastError));
            if (resourcesSynced.HasValue)
                operations.Add(PatchOperation.Set("/ResourcesSynced", resourcesSynced.Value));

            await container.PatchItemAsync<McpServerRecord>(serverId, new PartitionKey(accountId), operations);
        }
    }
}
