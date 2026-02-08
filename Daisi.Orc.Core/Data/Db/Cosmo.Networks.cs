using Daisi.Orc.Core.Data.Models;
using Daisi.Protos.V1;
using Daisi.SDK.Extensions;
using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Net;
using System.Text;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string NetworksIdPrefix = "net";
        public const string NetworksContainerName = "Networks";
        public const string NetworksPartitionKeyName = nameof(Models.Network.AccountId);


        public PartitionKey GetPartitionKey(Models.Network network)
        {
            return new PartitionKey(network.AccountId);
        }

        public async Task<bool> ArchiveNetworkAsync(string networkId, string accountId)
        {
            var container = await GetContainerAsync(NetworksContainerName);
            var net = await container.ReadItemAsync<Models.Network>(networkId, new PartitionKey(accountId));
            if (net == null) return false;

            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Replace("/Status", NetworkStatus.Archived)
            };

            var response = await container.PatchItemAsync<Models.Network>(networkId, new PartitionKey(accountId), patchOperations);           
            return true;
        }

        public async Task<Models.Network> CreateNetworkAsync(Models.Network network)
        {
            network.Id = GenerateId(NetworksIdPrefix);
            var container = await GetContainerAsync(NetworksContainerName);
            var result = await container.CreateItemAsync(network);
            return result;
        }

        public async Task<PagedResult<Models.Network>> GetNetworksAsync(PagingInfo? paging = null, bool? isPublic = null, string? accountId = null, string? id = null)
        {
            var container = await GetContainerAsync(NetworksContainerName);
            var query = container.GetItemLinqQueryable<Models.Network>()
                                 .Where(net => net.Status != NetworkStatus.Archived);

            if (!string.IsNullOrWhiteSpace(paging?.SearchTerm))
            {
                query = query.Where(net => net.Name.Contains(paging.SearchTerm) || net.AccountName.Contains(paging.SearchTerm));
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                query = query.Where(net => net.Id == id && net.AccountId == accountId);
            }
            else if(isPublic.HasValue)
            {
                if (isPublic.Value)
                {
                    query = query.Where(net => net.IsPublic);
                }
                else
                {
                    query = query.Where(net => net.AccountId == accountId && !net.IsPublic);

                    if (!string.IsNullOrWhiteSpace(paging?.SearchTerm))
                    {
                        query = query.Where(net => net.Name.ToLower().Contains(paging.SearchTerm.ToLower()));
                    }
                }
            }

            var results = await query.OrderBy(host => host.Name).ToPagedResultAsync(paging?.PageSize, paging?.PageIndex);
            return results;
        }

        public async Task<Models.Network> PatchNetworkForWebAsync(Models.Network network, string accountId)
        {
            var container = await GetContainerAsync(NetworksContainerName);

            var netAccountIdMatches = container.GetItemLinqQueryable<Models.Network>()
                                               .Count(net => net.Id == network.Id && net.AccountId == accountId) > 0;

            if (!netAccountIdMatches) throw new Exception("DAISI: Invalid Account ID.");


            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Replace("/Name", network.Name),
                PatchOperation.Replace("/Description", network.Description),
                PatchOperation.Replace("/IsPublic", network.IsPublic)
            };

            var publicChanged = container.GetItemLinqQueryable<Models.Network>()                                         
                                         .Count(net => net.Id == network.Id && net.IsPublic != network.IsPublic) > 0;

            if (publicChanged && network.IsPublic)
            {
                patchOperations.Add(PatchOperation.Replace("/Status", NetworkStatus.Unapproved));
            }
            network.AccountId = accountId;
            var response = await container.PatchItemAsync<Models.Network>(network.Id, GetPartitionKey(network), patchOperations);
            return response.Resource;
        }
    }
}
