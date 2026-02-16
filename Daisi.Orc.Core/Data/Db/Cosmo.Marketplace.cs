using Daisi.Orc.Core.Data.Models.Marketplace;
using Daisi.Protos.V1;
using Microsoft.Azure.Cosmos;

namespace Daisi.Orc.Core.Data.Db;

public partial class Cosmo
{
    public const string MarketplaceIdPrefix = "mkt";
    public const string MarketplaceContainerName = "Marketplace";
    public const string MarketplacePartitionKeyName = "AccountId";

    public async Task<MarketplaceItem> CreateMarketplaceItemAsync(MarketplaceItem item)
    {
        var container = await GetContainerAsync(MarketplaceContainerName);
        item.Id = GenerateId(MarketplaceIdPrefix);
        item.CreatedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;
        var response = await container.CreateItemAsync(item, new PartitionKey(item.AccountId));
        return response.Resource;
    }

    public async Task<MarketplaceItem?> GetMarketplaceItemByIdAsync(string id)
    {
        var container = await GetContainerAsync(MarketplaceContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
            .WithParameter("@id", id);

        using var resultSet = container.GetItemQueryIterator<MarketplaceItem>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            if (response.Count > 0)
                return response.First();
        }
        return null;
    }

    public async Task<MarketplaceItem?> GetMarketplaceItemAsync(string id, string accountId)
    {
        try
        {
            var container = await GetContainerAsync(MarketplaceContainerName);
            var response = await container.ReadItemAsync<MarketplaceItem>(id, new PartitionKey(accountId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<List<MarketplaceItem>> GetMarketplaceItemsByAccountAsync(string accountId)
    {
        var container = await GetContainerAsync(MarketplaceContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.AccountId = @accountId ORDER BY c.UpdatedAt DESC")
            .WithParameter("@accountId", accountId);

        var items = new List<MarketplaceItem>();
        using var resultSet = container.GetItemQueryIterator<MarketplaceItem>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            items.AddRange(response);
        }
        return items;
    }

    public async Task<List<MarketplaceItem>> GetPublicApprovedMarketplaceItemsAsync(string? search = null, string? tag = null, MarketplaceItemType? itemType = null)
    {
        var container = await GetContainerAsync(MarketplaceContainerName);
        var queryText = "SELECT * FROM c WHERE c.Visibility = 'Public' AND c.Status = 'Approved'";

        if (!string.IsNullOrWhiteSpace(search))
        {
            queryText += " AND (CONTAINS(LOWER(c.Name), LOWER(@search)) OR CONTAINS(LOWER(c.Description), LOWER(@search)))";
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            queryText += " AND ARRAY_CONTAINS(c.Tags, @tag)";
        }

        if (itemType.HasValue)
        {
            queryText += " AND c.ItemType = @itemType";
        }

        queryText += " ORDER BY c.DownloadCount DESC";

        var queryDef = new QueryDefinition(queryText);
        if (!string.IsNullOrWhiteSpace(search))
            queryDef = queryDef.WithParameter("@search", search);
        if (!string.IsNullOrWhiteSpace(tag))
            queryDef = queryDef.WithParameter("@tag", tag);
        if (itemType.HasValue)
            queryDef = queryDef.WithParameter("@itemType", itemType.Value.ToString());

        var items = new List<MarketplaceItem>();
        using var resultSet = container.GetItemQueryIterator<MarketplaceItem>(queryDef);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            items.AddRange(response);
        }
        // Sort featured items first, then by download count (Cosmos composite indexes not available)
        return items.OrderByDescending(i => i.IsFeatured).ThenByDescending(i => i.DownloadCount).ToList();
    }

    public async Task UpdateMarketplaceItemAsync(MarketplaceItem item)
    {
        var container = await GetContainerAsync(MarketplaceContainerName);
        item.UpdatedAt = DateTime.UtcNow;
        await container.UpsertItemAsync(item, new PartitionKey(item.AccountId));
    }

    public async Task DeleteMarketplaceItemAsync(string id, string accountId)
    {
        var container = await GetContainerAsync(MarketplaceContainerName);
        await container.DeleteItemAsync<MarketplaceItem>(id, new PartitionKey(accountId));
    }

    public async Task<List<MarketplaceItem>> GetPendingReviewMarketplaceItemsAsync()
    {
        var container = await GetContainerAsync(MarketplaceContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.Status = 'PendingReview' ORDER BY c.UpdatedAt ASC");

        var items = new List<MarketplaceItem>();
        using var resultSet = container.GetItemQueryIterator<MarketplaceItem>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            items.AddRange(response);
        }
        return items;
    }

    public async Task<int> GetFeaturedItemCountByAccountAsync(string accountId)
    {
        var container = await GetContainerAsync(MarketplaceContainerName);
        var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.AccountId = @id AND c.IsFeatured = true")
            .WithParameter("@id", accountId);

        using var resultSet = container.GetItemQueryIterator<int>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            if (response.Any())
                return response.First();
        }
        return 0;
    }

    /// <summary>
    /// Returns all approved, public, free marketplace items that have secure execution enabled.
    /// Used to discover free secure tools available to all accounts.
    /// </summary>
    public async Task<List<MarketplaceItem>> GetApprovedFreeSecureToolsAsync()
    {
        var container = await GetContainerAsync(MarketplaceContainerName);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.Status = 'Approved' AND c.IsSecureExecution = true AND c.PricingModel = 'MarketplacePricingFree' AND c.Visibility = 'Public'");

        var items = new List<MarketplaceItem>();
        using var resultSet = container.GetItemQueryIterator<MarketplaceItem>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            items.AddRange(response);
        }
        return items;
    }

    public async Task ClearFeaturedItemsByAccountAsync(string accountId)
    {
        var container = await GetContainerAsync(MarketplaceContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.AccountId = @id AND c.IsFeatured = true")
            .WithParameter("@id", accountId);

        var items = new List<MarketplaceItem>();
        using var resultSet = container.GetItemQueryIterator<MarketplaceItem>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            items.AddRange(response);
        }

        foreach (var item in items)
        {
            item.IsFeatured = false;
            item.UpdatedAt = DateTime.UtcNow;
            await container.UpsertItemAsync(item, new PartitionKey(item.AccountId));
        }
    }
}
