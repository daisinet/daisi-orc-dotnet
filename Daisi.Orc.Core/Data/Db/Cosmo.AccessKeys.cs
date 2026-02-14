using Daisi.Orc.Core.Data.Models;
using Daisi.SDK.Extensions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System.Net;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string AccessKeyIdPrefix = "key";
        public const string AccessKeyContainerName = "Keys";
        public const string AccessKeyPartitionKeyName = "type";

        private PartitionKey GetPartitionKey(AccessKey key)
        {
            return new PartitionKey(key.Type.ToLower());
        }
        private PartitionKey GetPartitionKey(KeyTypes type)
        {
            return new PartitionKey(type.Name);
        }
        public virtual async Task<AccessKey> GetKeyAsync(string key, KeyTypes type)
        {
            try
            {
                var container = await GetContainerAsync(AccessKeyContainerName);
                QueryDefinition def = new QueryDefinition("Select * from c where LOWER(c.key) = LOWER(@key)").WithParameter("@key", key);

                var options = new QueryRequestOptions() { PartitionKey = new PartitionKey(type.Name) };

                var it = container.GetItemQueryIterator<AccessKey>(def, requestOptions: options);

                if (it.HasMoreResults)
                {
                    var result = await it.ReadNextAsync();
                    var k = result.FirstOrDefault();
                    return k;
                }

                return default!;
            }
            catch { return default!; }
        }
        public async Task<AccessKey> CreateSecretKeyAsync(AccessKeyOwner owner)
        {
            var container = await GetContainerAsync(AccessKeyContainerName);
            AccessKey key = new AccessKey();
            key.Type = KeyTypes.Secret.Name;
            key.Key = $"secret-{StringExtensions.Random(20, false, true).ToLower()}";
            //key.TimeToLive = -1;
            key.Owner = owner;

            var result = (await container.CreateItemAsync<AccessKey>(key, new PartitionKey(KeyTypes.Secret.Name))).Resource;

            return result;
        }
        internal async Task<AccessKey> GetAppSecretKeyAsync(Dapp app)
        {
            try
            {
                var container = await GetContainerAsync(AccessKeyContainerName);
                QueryDefinition def = new QueryDefinition("Select * from c where LOWER(c.Owner.Id) = LOWER(@key) AND c.Owner.SystemRole = 1").WithParameter("@key", app.Id);

                var options = new QueryRequestOptions() { PartitionKey = new PartitionKey(KeyTypes.Secret.Name) };

                var it = container.GetItemQueryIterator<AccessKey>(def, requestOptions: options);

                if (it.HasMoreResults)
                {
                    var result = await it.ReadNextAsync();
                    var k = result.FirstOrDefault();
                    return k;
                }

                return default!;
            }
            catch { return default!; }
        }
        public async Task<AccessKey?> CreateClientKeyForAppAsync(string appId, string secretKey, IPAddress? requestorIPAddress, AccessKeyOwner owner)
        {
            var app = await GetDappAsync(appId);

            if (app is null) return null;

            return await CreateClientKeyForAppAsync(app, secretKey, requestorIPAddress, owner);
        }
        public async Task<AccessKey?> CreateClientKeyForAppAsync(Dapp app, string secretKey, IPAddress? requestorIPAddress, AccessKeyOwner owner)
        {
            var appSecretKey = await GetAppSecretKeyAsync(app);
            if (appSecretKey is null || (!app.IsDaisiApp && appSecretKey.Key != secretKey)) return null;

            var container = await GetContainerAsync(AccessKeyContainerName);
            AccessKey key = new AccessKey();
            key.Type = KeyTypes.Client.Name;
            key.Key = $"client-{StringExtensions.Random(20, true, false).ToLower()}";

            //key.TimeToLive = 30 * 24 * 60; // Every 30 days, everyone must reauth

            key.DateExpires = (owner.SystemRole == Protos.V1.SystemRoles.User)
                                ? DateTime.UtcNow.AddDays(30) // 30 days in seconds for users
                                : DateTime.UtcNow.AddMinutes(60); //60 minutes in seconds for non-users

            key.ParentKeyId = appSecretKey.Id;
            key.IpAddress = requestorIPAddress?.ToString() ?? string.Empty;
            key.Owner = owner;

            key.AccessToIDs = [app.Id];

            var result = (await container.CreateItemAsync<AccessKey>(key, new PartitionKey(KeyTypes.Client.Name))).Resource;

            return result;
        }
        public virtual async Task<AccessKey> CreateClientKeyAsync(AccessKey secretKey, IPAddress? requestorIPAddress, AccessKeyOwner owner, List<string>? accessToIds = null)
        {
            var container = await GetContainerAsync(AccessKeyContainerName);
            AccessKey key = new AccessKey();
            key.Type = KeyTypes.Client.Name;
            key.Key = $"client-{StringExtensions.Random(20, true, false).ToLower()}";

            //key.TimeToLive = 30 * 24 * 60; // Every 30 days, everyone must reauth

            key.DateExpires = (owner.SystemRole == Protos.V1.SystemRoles.User)
                                ? DateTime.UtcNow.AddDays(30) // 30 days in seconds for users
                                : DateTime.UtcNow.AddMinutes(60); //60 minutes in seconds for non-users

            key.ParentKeyId = secretKey.Id;
            key.IpAddress = requestorIPAddress?.ToString() ?? string.Empty;
            key.Owner = owner;

            //TODO: Needs to check to make sure that the account on this secret key has authority to get access to these IDs;
            key.AccessToIDs = accessToIds ?? new();

            var result = (await container.CreateItemAsync<AccessKey>(key, new PartitionKey(KeyTypes.Client.Name))).Resource;

            return result;
        }
        public async Task<bool> DeleteClientKeyAsync(string clientKey, string secretKey)
        {
            var container = await GetContainerAsync(AccessKeyContainerName);

            var cKey = await GetKeyAsync(clientKey, KeyTypes.Client);
            var sKey = await GetKeyAsync(secretKey, KeyTypes.Secret);

            if(cKey == null || sKey == null || cKey.ParentKeyId != sKey.Id)
            {
                return false;
            }

            var result = await container.DeleteItemAsync<AccessKey>(cKey.Id, new PartitionKey(KeyTypes.Client.Name));
            return result.StatusCode == HttpStatusCode.NoContent;
        }
        public async Task<bool> DeleteSecretKeyAsync(string keyId, string accountId)
        {
            var container = await GetContainerAsync(AccessKeyContainerName);

            var result = await container.DeleteItemAsync<AccessKey>(keyId, new PartitionKey(KeyTypes.Secret.Name));
            return result.StatusCode == HttpStatusCode.NoContent;
        }
        public async Task<ItemResponse<AccessKey>> UpsertKeyAsync(AccessKey key)
        {
            var container = await GetContainerAsync(AccessKeyContainerName);
            return await container.UpsertItemAsync(key, GetPartitionKey(key));
        }

        public async Task<ItemResponse<AccessKey>> SetClientKeyTTLAsync(string key, int? minutesToLive = 5)
        {
            var ck = await GetKeyAsync(key, KeyTypes.Client);
            return await SetKeyTTLAsync(ck, 30);
        }
        public async Task<ItemResponse<AccessKey>> SetKeyTTLAsync(AccessKey key, int? minutesToLive = 5)
        {
            var container = await GetContainerAsync(AccessKeyContainerName);
            if (minutesToLive.HasValue)
                key.DateExpires = DateTime.UtcNow.AddMinutes(minutesToLive.Value);
            else
                key.DateExpires = null;

            return await container.UpsertItemAsync<AccessKey>(key, GetPartitionKey(key));
        }

        public async Task<IEnumerable<PartitionKeyStub>> GetKeyStubsByOwnerIdAsync(string ownerId)
        {
            var container = await GetContainerAsync(AccessKeyContainerName);
            var query = container.GetItemLinqQueryable<AccessKey>()
                .Where(k => k.Owner.Id == ownerId)
                .Select(k => new
                {
                    k.Id,
                    k.Type
                });

            var iterator = query.ToFeedIterator();
            var results = new List<PartitionKeyStub>();
            while (iterator.HasMoreResults)
            {
                foreach (var key in await iterator.ReadNextAsync())
                {
                    results.Add(new PartitionKeyStub { EntityId = key.Id, PartitionKeyValue = key.Type });
                }
            }
            return results;
        }
        public async Task PatchKeyOwnerName(PartitionKeyStub key, string ownerName)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Replace("/Owner/Name", ownerName)
            };
            var container = await GetContainerAsync(AccessKeyContainerName);
            await container.PatchItemAsync<AccessKey>(key.EntityId, new PartitionKey(key.PartitionKeyValue), patchOperations);
        }

        public async Task PatchKeyOwnerUserFieldsAsync(PartitionKeyStub key, bool allowedToLogin, Protos.V1.UserRoles role)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Set("/Owner/AllowedToLogin", allowedToLogin),
                PatchOperation.Set("/Owner/Role", role)
            };
            var container = await GetContainerAsync(AccessKeyContainerName);
            await container.PatchItemAsync<AccessKey>(key.EntityId, new PartitionKey(key.PartitionKeyValue), patchOperations);
        }

        public async Task PatchKeyExpirationAsync(string keyId, DateTime newExpiration)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Set("/DateExpires", newExpiration)
            };
            var container = await GetContainerAsync(AccessKeyContainerName);
            await container.PatchItemAsync<AccessKey>(keyId, new PartitionKey(KeyTypes.Client.Name), patchOperations);
        }
        public async Task<IEnumerable<AccessKey>> GetKeysByOwnerIdAsync(string ownerId)
        {
            var container = await GetContainerAsync(AccessKeyContainerName);
            var query = container.GetItemLinqQueryable<AccessKey>()
                .Where(k => k.Owner.Id == ownerId);

            var iterator = query.ToFeedIterator();
            var results = new List<AccessKey>();
            while (iterator.HasMoreResults)
            {
                foreach (var key in await iterator.ReadNextAsync())
                {
                    results.Add(key);
                }
            }
            return results;
        }
    }
}
