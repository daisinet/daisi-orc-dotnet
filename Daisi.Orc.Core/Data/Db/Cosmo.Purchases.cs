using Daisi.Orc.Core.Data.Models.Marketplace;
using Microsoft.Azure.Cosmos;

namespace Daisi.Orc.Core.Data.Db;

public partial class Cosmo
{
    public const string PurchaseIdPrefix = "pur";
    public const string PurchasesContainerName = "Purchases";
    public const string PurchasesPartitionKeyName = "AccountId";

    public async Task<MarketplacePurchase> CreatePurchaseAsync(MarketplacePurchase purchase)
    {
        var container = await GetContainerAsync(PurchasesContainerName);
        purchase.Id = GenerateId(PurchaseIdPrefix);
        purchase.PurchasedAt = DateTime.UtcNow;
        var response = await container.CreateItemAsync(purchase, new PartitionKey(purchase.AccountId));
        return response.Resource;
    }

    public async Task<List<MarketplacePurchase>> GetPurchasesByAccountAsync(string accountId)
    {
        var container = await GetContainerAsync(PurchasesContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.AccountId = @accountId ORDER BY c.PurchasedAt DESC")
            .WithParameter("@accountId", accountId);

        var purchases = new List<MarketplacePurchase>();
        using var resultSet = container.GetItemQueryIterator<MarketplacePurchase>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            purchases.AddRange(response);
        }
        return purchases;
    }

    public async Task<MarketplacePurchase?> GetPurchaseAsync(string accountId, string marketplaceItemId)
    {
        var container = await GetContainerAsync(PurchasesContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.AccountId = @accountId AND c.MarketplaceItemId = @itemId AND c.IsActive = true")
            .WithParameter("@accountId", accountId)
            .WithParameter("@itemId", marketplaceItemId);

        using var resultSet = container.GetItemQueryIterator<MarketplacePurchase>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            if (response.Count > 0)
                return response.First();
        }
        return null;
    }

    public async Task<bool> HasPurchasedAsync(string accountId, string marketplaceItemId)
    {
        var purchase = await GetPurchaseAsync(accountId, marketplaceItemId);
        return purchase is not null;
    }

    public async Task<List<MarketplacePurchase>> GetActiveSubscriptionsAsync()
    {
        var container = await GetContainerAsync(PurchasesContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.IsSubscription = true AND c.IsActive = true");

        var purchases = new List<MarketplacePurchase>();
        using var resultSet = container.GetItemQueryIterator<MarketplacePurchase>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            purchases.AddRange(response);
        }
        return purchases;
    }

    public async Task UpdatePurchaseAsync(MarketplacePurchase purchase)
    {
        var container = await GetContainerAsync(PurchasesContainerName);
        await container.UpsertItemAsync(purchase, new PartitionKey(purchase.AccountId));
    }
}
