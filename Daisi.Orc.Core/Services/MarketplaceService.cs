using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Data.Models.Marketplace;
using Daisi.Protos.V1;

namespace Daisi.Orc.Core.Services;

public class MarketplaceService(Cosmo cosmo, CreditService creditService, SecureToolService secureToolService)
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

        // If item is paid, verify seller is still Premium
        if (item.PricingModel != MarketplacePricingModel.MarketplacePricingFree)
        {
            var sellerProfile = await cosmo.GetProviderProfileAsync(item.AccountId);
            if (sellerProfile is null || !sellerProfile.IsPremium)
                return (false, "This provider's premium subscription has expired.", null);
        }

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

        // If this is a secure tool, generate InstallId and notify provider
        if (item.IsSecureExecution)
        {
            purchase.SecureInstallId = Cosmo.GenerateId("inst");
        }

        // Bundle handling: if this is a Plugin with bundled items, generate a shared BundleInstallId
        string? bundleInstallId = null;
        if (item.ItemType == MarketplaceItemType.Plugin && item.BundledItemIds.Count > 0)
        {
            bundleInstallId = Cosmo.GenerateId("binst");
            purchase.BundleInstallId = bundleInstallId;
        }

        purchase = await cosmo.CreatePurchaseAsync(purchase);

        // Notify provider after purchase is persisted (best-effort)
        if (item.IsSecureExecution && !string.IsNullOrEmpty(purchase.SecureInstallId))
        {
            await secureToolService.NotifyProviderInstallAsync(item, purchase.SecureInstallId, bundleInstallId);
        }

        // Create child purchases for bundled secure tools
        if (bundleInstallId is not null)
        {
            foreach (var bundledItemId in item.BundledItemIds)
            {
                var bundledItem = await cosmo.GetMarketplaceItemByIdAsync(bundledItemId);
                if (bundledItem is null || !bundledItem.IsSecureExecution)
                    continue;

                var childPurchase = new MarketplacePurchase
                {
                    AccountId = buyerAccountId,
                    MarketplaceItemId = bundledItemId,
                    MarketplaceItemName = bundledItem.Name,
                    ItemType = bundledItem.ItemType,
                    CreditsPaid = 0,
                    SecureInstallId = Cosmo.GenerateId("inst"),
                    BundleInstallId = bundleInstallId
                };

                await cosmo.CreatePurchaseAsync(childPurchase);

                // Notify provider for each bundled tool (best-effort)
                await secureToolService.NotifyProviderInstallAsync(bundledItem, childPurchase.SecureInstallId!, bundleInstallId);
            }
        }

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
                if (item.IsSecureExecution && !string.IsNullOrEmpty(sub.SecureInstallId))
                    await secureToolService.NotifyProviderUninstallAsync(item, sub.SecureInstallId);
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

    /// <summary>
    /// Upgrade a provider to Premium tier. Debits credits for the first month.
    /// </summary>
    public async Task<(bool Success, string? Error, ProviderProfile? Profile)> UpgradeToPremiumAsync(string accountId)
    {
        var profile = await cosmo.GetProviderProfileAsync(accountId);
        if (profile is null)
            return (false, "Provider profile not found", null);

        if (profile.Status != ProviderStatus.Approved)
            return (false, "Only approved providers can upgrade to Premium", null);

        if (profile.IsPremium)
            return (false, "Provider is already Premium", null);

        // Mark as premium first to prevent race conditions (double-click/concurrent calls)
        profile.IsPremium = true;
        profile.PremiumExpiresAt = DateTime.UtcNow.AddDays(30);
        await cosmo.UpdateProviderProfileAsync(profile);

        var settings = await cosmo.GetMarketplaceSettingsAsync();
        var transaction = await creditService.SpendPremiumCreditsAsync(accountId, settings.PremiumMonthlyCreditCost);
        if (transaction is null)
        {
            // Roll back premium if payment fails
            profile.IsPremium = false;
            profile.PremiumExpiresAt = null;
            await cosmo.UpdateProviderProfileAsync(profile);
            return (false, "Insufficient credits", null);
        }

        profile.PremiumTransactionId = transaction.Id;
        await cosmo.UpdateProviderProfileAsync(profile);

        return (true, null, profile);
    }

    /// <summary>
    /// Cancel a provider's Premium subscription. Issues a 50% prorated refund and clears featured items.
    /// </summary>
    public async Task<(ProviderProfile? Profile, long CreditsRefunded)> CancelPremiumAsync(string accountId)
    {
        var profile = await cosmo.GetProviderProfileAsync(accountId);
        if (profile is null)
            return (null, 0);

        // Calculate prorated refund: 50% of remaining days
        long refundAmount = 0;
        if (profile.PremiumExpiresAt.HasValue)
        {
            var remaining = (profile.PremiumExpiresAt.Value - DateTime.UtcNow).TotalDays;
            if (remaining > 0)
            {
                var settings = await cosmo.GetMarketplaceSettingsAsync();
                refundAmount = (long)(settings.PremiumMonthlyCreditCost * (remaining / 30.0) * 0.5);
            }
        }

        profile.IsPremium = false;
        profile.PremiumExpiresAt = null;
        profile.PremiumTransactionId = null;
        await cosmo.UpdateProviderProfileAsync(profile);

        if (refundAmount > 0)
            await creditService.RefundPremiumCreditsAsync(accountId, refundAmount);

        await cosmo.ClearFeaturedItemsByAccountAsync(accountId);

        return (profile, refundAmount);
    }

    /// <summary>
    /// Toggle the featured flag on a marketplace item. Only Premium providers can feature items.
    /// </summary>
    public async Task<(bool Success, string? Error, MarketplaceItem? Item)> SetItemFeaturedAsync(string accountId, string itemId, bool isFeatured)
    {
        var profile = await cosmo.GetProviderProfileAsync(accountId);
        if (profile is null)
            return (false, "Provider profile not found", null);

        if (!profile.IsPremium)
            return (false, "Only Premium providers can feature items", null);

        var item = await cosmo.GetMarketplaceItemByIdAsync(itemId);
        if (item is null)
            return (false, "Item not found", null);

        if (item.AccountId != accountId)
            return (false, "You can only feature your own items", null);

        if (item.Status != MarketplaceItemStatus.Approved)
            return (false, "Only approved items can be featured", null);

        if (isFeatured)
        {
            var settings = await cosmo.GetMarketplaceSettingsAsync();
            var currentCount = await cosmo.GetFeaturedItemCountByAccountAsync(accountId);
            if (currentCount >= settings.MaxFeaturedItemsPerProvider)
                return (false, $"Maximum {settings.MaxFeaturedItemsPerProvider} featured items allowed", null);
        }

        item.IsFeatured = isFeatured;
        await cosmo.UpdateMarketplaceItemAsync(item);

        return (true, null, item);
    }

    /// <summary>
    /// Process premium provider subscription renewals. Called by background service.
    /// </summary>
    public async Task<int> ProcessPremiumRenewalsAsync()
    {
        var expiringProviders = await cosmo.GetExpiringPremiumProvidersAsync(DateTime.UtcNow.AddHours(24));
        int renewed = 0;

        foreach (var profile in expiringProviders)
        {
            var settings = await cosmo.GetMarketplaceSettingsAsync();
            var transaction = await creditService.SpendPremiumCreditsAsync(profile.AccountId, settings.PremiumMonthlyCreditCost);

            if (transaction is null)
            {
                // Insufficient credits — cancel premium
                profile.IsPremium = false;
                profile.PremiumExpiresAt = null;
                await cosmo.UpdateProviderProfileAsync(profile);
                await cosmo.ClearFeaturedItemsByAccountAsync(profile.AccountId);
                continue;
            }

            // Extend premium
            profile.PremiumExpiresAt = (profile.PremiumExpiresAt ?? DateTime.UtcNow).AddDays(30);
            profile.PremiumTransactionId = transaction.Id;
            await cosmo.UpdateProviderProfileAsync(profile);
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
