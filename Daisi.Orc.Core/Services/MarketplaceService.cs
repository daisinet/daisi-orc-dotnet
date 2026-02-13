using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Data.Models.Marketplace;
using Daisi.Protos.V1;

namespace Daisi.Orc.Core.Services;

public class MarketplaceService(Cosmo cosmo, CreditService creditService)
{
    /// <summary>
    /// Purchase a marketplace item. Handles Free, OneTimePurchase, and Subscription pricing.
    /// </summary>
    public async Task<(bool Success, string? Error, MarketplacePurchase? Purchase)> PurchaseItemAsync(string buyerAccountId, string marketplaceItemId)
    {
        var item = await cosmo.GetMarketplaceItemByIdAsync(marketplaceItemId);
        if (item is null)
            return (false, "Marketplace item not found", null);

        if (item.Status != MarketplaceItemStatus.Approved)
            return (false, "Item is not available for purchase", null);

        // Check if already purchased (for non-subscription items)
        var existingPurchase = await cosmo.GetPurchaseAsync(buyerAccountId, marketplaceItemId);
        if (existingPurchase is not null && !existingPurchase.IsSubscription)
            return (false, "Item already purchased", null);

        // For subscriptions, allow re-purchase if expired
        if (existingPurchase is not null && existingPurchase.IsSubscription && existingPurchase.IsActive)
            return (false, "Active subscription already exists", null);

        var purchase = new MarketplacePurchase
        {
            AccountId = buyerAccountId,
            MarketplaceItemId = marketplaceItemId,
            MarketplaceItemName = item.Name,
            ItemType = item.ItemType
        };

        switch (item.PricingModel)
        {
            case MarketplacePricingModel.MarketplacePricingFree:
                purchase.CreditsPaid = 0;
                break;

            case MarketplacePricingModel.MarketplacePricingOneTime:
                var spent = await creditService.SpendMarketplaceCreditsAsync(buyerAccountId, item.CreditPrice, marketplaceItemId, item.Name);
                if (spent is null)
                    return (false, "Insufficient credits", null);

                purchase.CreditsPaid = item.CreditPrice;
                purchase.TransactionId = spent.Id;

                // Credit provider share
                await CreditProviderAsync(item, item.CreditPrice);
                break;

            case MarketplacePricingModel.MarketplacePricingSubscription:
                var subSpent = await creditService.SpendMarketplaceCreditsAsync(buyerAccountId, item.SubscriptionCreditPrice, marketplaceItemId, item.Name);
                if (subSpent is null)
                    return (false, "Insufficient credits", null);

                purchase.CreditsPaid = item.SubscriptionCreditPrice;
                purchase.TransactionId = subSpent.Id;
                purchase.IsSubscription = true;
                purchase.SubscriptionExpiresAt = DateTime.UtcNow.AddDays(item.SubscriptionPeriodDays);

                // Credit provider share
                await CreditProviderAsync(item, item.SubscriptionCreditPrice);
                break;
        }

        purchase = await cosmo.CreatePurchaseAsync(purchase);

        // Increment purchase count
        item.PurchaseCount++;
        await cosmo.UpdateMarketplaceItemAsync(item);

        return (true, null, purchase);
    }

    /// <summary>
    /// Check if an account is entitled to use a marketplace item.
    /// </summary>
    public async Task<bool> CheckEntitlementAsync(string accountId, string marketplaceItemId)
    {
        var item = await cosmo.GetMarketplaceItemByIdAsync(marketplaceItemId);
        if (item is null)
            return false;

        // Free items are always entitled
        if (item.PricingModel == MarketplacePricingModel.MarketplacePricingFree && item.Status == MarketplaceItemStatus.Approved)
            return true;

        // Check if account owns the item
        if (item.AccountId == accountId)
            return true;

        // Check purchase
        var purchase = await cosmo.GetPurchaseAsync(accountId, marketplaceItemId);
        if (purchase is null)
            return false;

        if (!purchase.IsActive)
            return false;

        // For subscriptions, check expiry
        if (purchase.IsSubscription && purchase.SubscriptionExpiresAt.HasValue)
            return purchase.SubscriptionExpiresAt.Value > DateTime.UtcNow;

        return true;
    }

    /// <summary>
    /// Process subscription renewals. Called by background service.
    /// </summary>
    public async Task<int> ProcessSubscriptionRenewalsAsync()
    {
        var subscriptions = await cosmo.GetActiveSubscriptionsAsync();
        int renewed = 0;

        foreach (var sub in subscriptions)
        {
            if (!sub.IsSubscription || !sub.SubscriptionExpiresAt.HasValue)
                continue;

            // Only process subscriptions expiring within 24 hours
            if (sub.SubscriptionExpiresAt.Value > DateTime.UtcNow.AddHours(24))
                continue;

            var item = await cosmo.GetMarketplaceItemByIdAsync(sub.MarketplaceItemId);
            if (item is null)
            {
                sub.IsActive = false;
                await cosmo.UpdatePurchaseAsync(sub);
                continue;
            }

            // Attempt renewal
            var spent = await creditService.SpendSubscriptionRenewalCreditsAsync(sub.AccountId, item.SubscriptionCreditPrice, item.Id, item.Name);
            if (spent is null)
            {
                // Insufficient credits — deactivate subscription
                sub.IsActive = false;
                await cosmo.UpdatePurchaseAsync(sub);
                continue;
            }

            // Extend subscription
            sub.SubscriptionExpiresAt = sub.SubscriptionExpiresAt.Value.AddDays(item.SubscriptionPeriodDays);
            sub.CreditsPaid += item.SubscriptionCreditPrice;
            sub.TransactionId = spent.Id;
            await cosmo.UpdatePurchaseAsync(sub);

            // Credit provider share
            await CreditProviderAsync(item, item.SubscriptionCreditPrice);

            renewed++;
        }

        return renewed;
    }

    private async Task CreditProviderAsync(MarketplaceItem item, long amount)
    {
        var provider = await cosmo.GetProviderProfileAsync(item.AccountId);
        if (provider is null)
            return;

        long providerShare = (long)(amount * provider.RevenueSharePercent / 100.0);
        if (providerShare <= 0)
            return;

        await creditService.EarnProviderCreditsAsync(item.AccountId, providerShare, item.Id);

        provider.TotalEarnings += providerShare;
        provider.PendingPayout += providerShare;
        await cosmo.UpdateProviderProfileAsync(provider);
    }
}
