using Daisi.Orc.Core.Data.Models;
using Daisi.Protos.V1;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Printing;
using System.Text;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string UsersIdPrefix = "acct";
        public const string AccountsIdPrefix = "acct";
        public const string AccountsContainerName = "Accounts";
        public const string AccountsPartitionKeyName = "AccountId";

        public PartitionKey GetPartitionKey(Models.User user)
        {
            return GetAccountPartitionKey(user.AccountId);
        }
        public PartitionKey GetPartitionKey(Models.Account account)
        {
            return GetAccountPartitionKey(account.Id);
        }
        public PartitionKey GetAccountPartitionKey(string accountId)
        {
            return new PartitionKey(accountId);
        }
        public virtual async Task<bool> UserAllowedToLogin(string userId)
        {
            var container = await GetContainerAsync(AccountsContainerName);

            var query = new QueryDefinition("SELECT VALUE a.AllowedToLogin FROM a WHERE a.id = @userId")
                .WithParameter("@userId", userId);

            using (FeedIterator<bool> resultSet = container.GetItemQueryIterator<bool>(query))
            {
                while (resultSet.HasMoreResults)
                {
                    FeedResponse<bool> response = await resultSet.ReadNextAsync();
                    if (response.Count > 0)
                    {
                        return response.First();
                    }
                }
            }
            return false;
        }
        public async Task<Models.User> CreateUserAsync(Models.User user)
        {
            var container = await GetContainerAsync(AccountsContainerName);
            Models.Account account;
            user.Id = GenerateId("user");

            if (string.IsNullOrEmpty(user.AccountId))
            {
                account = await CreateAccountAsync(new Models.Account
                {
                    Name = user.Name,
                    Users = new List<Models.Stub> { new Stub() { Id = user.Id, Name = user.Name } }
                });
            }
            else
            {
                account = await GetAccountAsync(user.AccountId);
            }
            user.AccountId = account.Id;
            user.AccountName =  account.Name;
            user.Email = user.Email.ToLower();            

            user.Status = Protos.V1.UserStatus.Active;
             
            var response = await container.CreateItemAsync(user, GetPartitionKey(account));
            return response.Resource;
        }
        public async Task<Models.Account> GetAccountAsync(string accountId)
        {
            var container = await GetContainerAsync(AccountsContainerName);
            var response = await container.ReadItemAsync<Models.Account>(accountId, GetAccountPartitionKey(accountId));
            return response.Resource;
        }
        public async Task<Models.Account> CreateAccountAsync(Models.Account account)
        {
            var container = await GetContainerAsync(AccountsContainerName);
            account.Id = GenerateId("acct");
            var response = await container.CreateItemAsync(account, new PartitionKey(account.Id));
            return response.Resource;
        }

        public async Task<bool> UserWithEmailExistsAsync(string email)
        {
            var container = await GetContainerAsync(AccountsContainerName);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.type = @type AND LOWER(c.Email) = LOWER(@Email)")
                .WithParameter("@type", "User")
                .WithParameter("@Email", email);

            using (FeedIterator<Models.User> resultSet = container.GetItemQueryIterator<Models.User>(query))
            {
                while (resultSet.HasMoreResults)
                {
                    FeedResponse<Models.User> response = await resultSet.ReadNextAsync();
                    if (response.Count > 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public async Task<Models.User?> GetUserByEmailOrPhoneAsync(string emailOrPhone)
        {
            var container = await GetContainerAsync(AccountsContainerName);
            var query = new QueryDefinition("SELECT * FROM c WHERE c.type = @type AND (LOWER(c.Email) = LOWER(@EmailOrPhone) OR c.Phone = @EmailOrPhone)")
                .WithParameter("@type", "User")
                .WithParameter("@EmailOrPhone", emailOrPhone);
            using (FeedIterator<Models.User> resultSet = container.GetItemQueryIterator<Models.User>(query))
            {
                while (resultSet.HasMoreResults)
                {
                    FeedResponse<Models.User> response = await resultSet.ReadNextAsync();
                    if (response.Count > 0)
                    {
                        return response.First();
                    }
                }
            }
            return null;
        }

        public async Task<PagedResult<Models.User>> GetUsersAsync(string accountId, PagingInfo paging)
        {
            var container = await GetContainerAsync(AccountsContainerName);
            var query = container.GetItemLinqQueryable<Models.User>()
                .Where(user => user.AccountId == accountId && user.Status != UserStatus.Archived);

            if (paging.HasSearchTerm)
            {
                query = query.Where(u => u.Name.ToLower().Contains(paging.SearchTerm.ToLower()));
            }

            var results = await query.OrderBy(u => u.Name).ToPagedResultAsync(paging.PageSize, paging.PageIndex);
            return results;
        }
        public async Task<Models.User> GetUserAsync(string id, string accountId)
        {
            var container = await GetContainerAsync(AccountsContainerName);
            var result = await container.ReadItemAsync<Models.User>(id, new PartitionKey(accountId));
            return result.Resource;
        }

        public async Task UpdateUserAsync(Models.User user)
        {
            var container = await GetContainerAsync(AccountsContainerName);
            await container.UpsertItemAsync(user, GetPartitionKey(user));
        }   


        public async Task<Models.Account> PatchAccountForWebUpdateAsync(Models.Account account)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Replace("/Name", account.Name),
            };

            if (!string.IsNullOrWhiteSpace(account.TaxId))
                patchOperations.Add(PatchOperation.Replace("/TaxId", account.TaxId));


            var container = await GetContainerAsync(AccountsContainerName);
            var response = await container.PatchItemAsync<Models.Account>(account.Id, GetPartitionKey(account), patchOperations);
            return response.Resource;
        }

        public async Task<Models.User> PatchUserForWebUpdateAsync(Models.User user)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Replace("/Name", user.Name),
                PatchOperation.Replace("/Email", user.Email),
                PatchOperation.Replace("/Phone", user.Phone),
                PatchOperation.Replace("/Role", user.Role),
                PatchOperation.Replace("/AllowEmail", user.AllowEmail),
                PatchOperation.Replace("/AllowSMS", user.AllowSMS),
                PatchOperation.Replace("/AllowedToLogin", user.AllowedToLogin),
                PatchOperation.Replace("/Status", user.Status)

            };

            var container = await GetContainerAsync(AccountsContainerName);
            var response = await container.PatchItemAsync<Models.User>(user.Id, GetPartitionKey(user), patchOperations);
            return response.Resource;
        }

        /// <summary>
        /// Patches the DriveStorageLimits field on an account record.
        /// </summary>
        public async Task<Models.Account> PatchAccountStorageLimitsAsync(string accountId, object storageLimits)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Set("/DriveStorageLimits", storageLimits),
            };

            var container = await GetContainerAsync(AccountsContainerName);
            var response = await container.PatchItemAsync<Models.Account>(accountId, GetAccountPartitionKey(accountId), patchOperations);
            return response.Resource;
        }

        /// <summary>
        /// Gets all accounts across all partitions with paging and optional search on Name.
        /// </summary>
        public async Task<PagedResult<Models.Account>> GetAllAccountsAsync(PagingInfo paging)
        {
            var container = await GetContainerAsync(AccountsContainerName);
            var query = container.GetItemLinqQueryable<Models.Account>(allowSynchronousQueryExecution: false, requestOptions: new QueryRequestOptions { MaxItemCount = -1 })
                .Where(a => a.type == "Account");

            if (paging.HasSearchTerm)
            {
                query = query.Where(a => a.Name.ToLower().Contains(paging.SearchTerm.ToLower()));
            }

            var results = await query.OrderBy(a => a.Name).ToPagedResultAsync(paging.PageSize, paging.PageIndex);
            return results;
        }

        /// <summary>
        /// Gets the count of non-archived users for an account.
        /// </summary>
        public async Task<int> GetAccountUserCountAsync(string accountId)
        {
            var container = await GetContainerAsync(AccountsContainerName);
            var count = await container.GetItemLinqQueryable<Models.User>()
                .Where(u => u.AccountId == accountId && u.Status != UserStatus.Archived && u.Type == "User")
                .CountAsync();
            return count;
        }
    }
}
