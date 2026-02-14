using Daisi.Orc.Core.Data.Models;
using Daisi.Protos.V1;
using Daisi.SDK.Extensions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Text;
using Dapp = Daisi.Orc.Core.Data.Models.Dapp;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string AppsIdPrefix = "dapp";
        public const string AppsContainerName = "Apps";
        public const string AppsContainerPartitionKeyName = "AccountId";

        public PartitionKey GetPartitionKey(Dapp dapp)
        {
            return GetDappPartitionKey(dapp.AccountId);
        }
        public PartitionKey GetDappPartitionKey(string accountId)
        {
            return new PartitionKey(accountId);
        }
        
        public async Task<Dapp?> GetDappAsync(string appId)
        {
            var container = await GetContainerAsync(AppsContainerName);
            var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @appId")
                            .WithParameter("@appId", appId);

            using (FeedIterator<Dapp> resultSet = container.GetItemQueryIterator<Dapp>(query))
            {
                while (resultSet.HasMoreResults)
                {
                    FeedResponse<Dapp> response = await resultSet.ReadNextAsync();
                    if (response.Count > 0)
                    {
                        return response.First();
                    }
                }
            }
            return default;
        }
        public async Task<Dapp> GetDappAsync(string appId, string accountId)
        {
            var container = await GetContainerAsync(AppsContainerName);
            var response = await container.ReadItemAsync<Dapp>(appId, GetAccountPartitionKey(accountId));
            return response.Resource;
        }

        public async Task<PagedResult<Dapp>> GetDappsAsync(string accountId, PagingInfo paging)
        {
            var container = await GetContainerAsync(AppsContainerName);
            var query = container.GetItemLinqQueryable<Dapp>()
                                 .Where(app => app.AccountId == accountId);

            if (!string.IsNullOrWhiteSpace(paging.SearchTerm))
            {
                query = query.Where(dapp => dapp.Name.ToLower().Contains(paging.SearchTerm.ToLower()));
            }

            var results = await query.OrderBy(app => app.Name).ToPagedResultAsync(paging.PageSize, paging.PageIndex);
            return results;
        }

        public async Task<Dapp> CreateDappAsync(Dapp dapp, string? accountName = null)
        {
            var container = await GetContainerAsync(AppsContainerName);
            if (string.IsNullOrEmpty(accountName))
            {
                var account = await GetAccountAsync(dapp.AccountId);
                accountName = account.Name;
            }
            dapp.AccountName = accountName;
            dapp.Id = $"app-{DateTime.UtcNow.ToString("yyMMddhhmmss")}-{StringExtensions.Random(5, false, true).ToLower()}";

            var key = await CreateSecretKeyAsync(new AccessKeyOwner()
            {
                Id = dapp.Id,
                AccountId = dapp.AccountId,
                Name = dapp.Name,
                SystemRole = SystemRoles.App                
            });            
            
            dapp.SecretKeyId = key.Id;

            var response = await container.UpsertItemAsync(dapp);
            return response.Resource;

        }

        public async Task<bool> DeleteDappAsync(string dappId, string accountId)
        {
            var dapp = await GetDappAsync(dappId);
            if (dapp.AccountId != accountId) return false;

            var container = await GetContainerAsync(AppsContainerName);
            var keyResult = await DeleteSecretKeyAsync(dapp.SecretKeyId, accountId);

            if (keyResult)
            {
                var result = await container.DeleteItemAsync<Dapp>(dapp.Id, GetPartitionKey(dapp));

                return result.StatusCode == System.Net.HttpStatusCode.NoContent;
            }

            return false;
        }

        public async Task<Dapp> PatchDappForWebUpdateAsync(Dapp dapp, string accountId)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Replace("/Name", dapp.Name)
            };


            var container = await GetContainerAsync(AppsContainerName);
            var response = await container.PatchItemAsync<Dapp>(dapp.Id, GetPartitionKey(dapp), patchOperations);
            return response.Resource;
        }

        /// <summary>
        /// Gets the count of apps for an account.
        /// </summary>
        public async Task<int> GetDappCountAsync(string accountId)
        {
            var container = await GetContainerAsync(AppsContainerName);
            var count = await container.GetItemLinqQueryable<Dapp>()
                .Where(d => d.AccountId == accountId)
                .CountAsync();
            return count;
        }

        public async Task<Dapp?> SetDappIsDaisiAppAsync(string appId, bool isDaisiApp)
        {
            var dapp = await GetDappAsync(appId);
            if (dapp is null) return null;

            var patchOperations = new List<PatchOperation>
            {
                PatchOperation.Replace("/IsDaisiApp", isDaisiApp)
            };

            var container = await GetContainerAsync(AppsContainerName);
            var response = await container.PatchItemAsync<Dapp>(dapp.Id, GetDappPartitionKey(dapp.AccountId), patchOperations);
            return response.Resource;
        }
    }
}
