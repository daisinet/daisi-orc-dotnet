using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Text;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string OrcsIdPrefix = "orc";
        public const string OrcsContainerName = "Orcs";
        public const string OrcsPartitionKeyName = nameof(Orchestrator.AccountId);

        public PartitionKey GetPartitionKey(Orchestrator orc)
        {
            return new PartitionKey(orc.AccountId);
        }

        public async Task<PagedResult<Orchestrator>> GetOrcsAsync(Protos.V1.PagingInfo? paging, string? orcId, string? accountId)
        {
            var container = await GetContainerAsync(OrcsContainerName);
            var networks = await GetNetworksAsync(accountId: accountId);

            var query = container.GetItemLinqQueryable<Orchestrator>()
                                 .Where(orc => orc.AccountId == accountId || orc.Networks.Any(n => n.IsPublic));

            if (!string.IsNullOrWhiteSpace(paging?.SearchTerm))
            {
                query = query.Where(orc => orc.Name.ToLower().Contains(paging.SearchTerm.ToLower()));
            }

            var results = await query.OrderBy(host => host.Name).ToPagedResultAsync(paging?.PageSize, paging?.PageIndex);

            return results;
        }

        public async Task<Orchestrator> CreateOrcAsync(Orchestrator orc, string accountId)
        {
            var container = await GetContainerAsync(OrcsContainerName);
            var account = await GetAccountAsync(accountId);
            orc.Id = GenerateId(OrcsIdPrefix);
            orc.AccountId = accountId;
            orc.AccountName = account.Name;

            orc.Name = orc.Name.ToLower();
            var result = await container.CreateItemAsync(orc);
            return result.Resource;
        }

        public async Task<Orchestrator> PatchOrcForWebUpdateAsync(Orchestrator orc, string accountId)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Set("/Name", orc.Name.ToLower()),
                PatchOperation.Set("/RequiresSSL", orc.RequiresSSL),
                PatchOperation.Set("/Port", orc.Port),
                PatchOperation.Set("/Domain", orc.Domain),
                PatchOperation.Set("/Networks", orc.Networks),
            };

            var container = await GetContainerAsync(OrcsContainerName);
            var response = await container.PatchItemAsync<Orchestrator>(orc.Id, new PartitionKey(accountId), patchOperations);
            return response.Resource;

        }

        public async Task<Orchestrator> PatchOrcStatusAsync(string orcId, Protos.V1.OrcStatus status, string? accountId)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Set("/Status", status)
            };

            var container = await GetContainerAsync(OrcsContainerName);

            var response = await container.PatchItemAsync<Orchestrator>(orcId, new PartitionKey(accountId), patchOperations);
            return response.Resource;

        }

        public async Task<Orchestrator> PatchOrcConnectionCountAsync(string orcId, int connectionCount, string? accountId)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Set("/OpenConnectionCount", connectionCount)
            };

            var container = await GetContainerAsync(OrcsContainerName);

            var response = await container.PatchItemAsync<Orchestrator>(orcId, new PartitionKey(accountId), patchOperations);
            return response.Resource;

        }
    }
}
