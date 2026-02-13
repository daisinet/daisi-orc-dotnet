using Daisi.Orc.Core.Data.Models.Marketplace;
using Microsoft.Azure.Cosmos;

namespace Daisi.Orc.Core.Data.Db;

public partial class Cosmo
{
    public const string ProviderIdPrefix = "prv";
    public const string ProvidersContainerName = "Providers";
    public const string ProvidersPartitionKeyName = "AccountId";

    public async Task<ProviderProfile> CreateProviderProfileAsync(ProviderProfile profile)
    {
        var container = await GetContainerAsync(ProvidersContainerName);
        profile.Id = GenerateId(ProviderIdPrefix);
        profile.CreatedAt = DateTime.UtcNow;
        profile.UpdatedAt = DateTime.UtcNow;
        var response = await container.CreateItemAsync(profile, new PartitionKey(profile.AccountId));
        return response.Resource;
    }

    public async Task<ProviderProfile?> GetProviderProfileAsync(string accountId)
    {
        var container = await GetContainerAsync(ProvidersContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.AccountId = @accountId")
            .WithParameter("@accountId", accountId);

        using var resultSet = container.GetItemQueryIterator<ProviderProfile>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            if (response.Count > 0)
                return response.First();
        }
        return null;
    }

    public async Task UpdateProviderProfileAsync(ProviderProfile profile)
    {
        var container = await GetContainerAsync(ProvidersContainerName);
        profile.UpdatedAt = DateTime.UtcNow;
        await container.UpsertItemAsync(profile, new PartitionKey(profile.AccountId));
    }

    public async Task<List<ProviderProfile>> GetPendingProviderProfilesAsync()
    {
        var container = await GetContainerAsync(ProvidersContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.Status = 'Pending' ORDER BY c.CreatedAt ASC");

        var profiles = new List<ProviderProfile>();
        using var resultSet = container.GetItemQueryIterator<ProviderProfile>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            profiles.AddRange(response);
        }
        return profiles;
    }

    public async Task<bool> IsProviderDisplayNameTakenAsync(string displayName, string? excludeAccountId = null)
    {
        var container = await GetContainerAsync(ProvidersContainerName);
        var queryText = "SELECT VALUE COUNT(1) FROM c WHERE LOWER(c.DisplayName) = LOWER(@displayName)";
        if (!string.IsNullOrEmpty(excludeAccountId))
            queryText += " AND c.AccountId != @excludeAccountId";

        var queryDef = new QueryDefinition(queryText)
            .WithParameter("@displayName", displayName);
        if (!string.IsNullOrEmpty(excludeAccountId))
            queryDef = queryDef.WithParameter("@excludeAccountId", excludeAccountId);

        using var resultSet = container.GetItemQueryIterator<int>(queryDef);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            if (response.Any() && response.First() > 0)
                return true;
        }
        return false;
    }

    public async Task<List<ProviderProfile>> GetAllProvidersAsync()
    {
        var container = await GetContainerAsync(ProvidersContainerName);
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.CreatedAt DESC");

        var profiles = new List<ProviderProfile>();
        using var resultSet = container.GetItemQueryIterator<ProviderProfile>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            profiles.AddRange(response);
        }
        return profiles;
    }
}
