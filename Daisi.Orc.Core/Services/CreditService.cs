using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Protos.V1;

namespace Daisi.Orc.Core.Services
{
    public class CreditService(Cosmo cosmo)
    {
        /// <summary>
        /// Default flat credit rate per hour of uptime.
        /// </summary>
        public const long DefaultUptimeCreditsPerHour = 10;

        /// <summary>
        /// Award credits to a host account for processing tokens.
        /// </summary>
        public async Task<CreditTransaction> EarnTokenCreditsAsync(string accountId, int tokenCount, string? relatedEntityId = null)
        {
            var account = await cosmo.GetOrCreateCreditAccountAsync(accountId);
            var bonusTier = await CalculateUptimeBonusTierAsync(accountId);
            double uptimeMultiplier = GetUptimeBonusMultiplier(bonusTier);
            double effectiveMultiplier = account.TokenEarnMultiplier * uptimeMultiplier;

            long credits = (long)(tokenCount * effectiveMultiplier);

            account.Balance += credits;
            account.TotalEarned += credits;
            await cosmo.UpdateCreditAccountBalanceAsync(account);

            var transaction = new CreditTransaction
            {
                AccountId = accountId,
                Type = CreditTransactionType.TokenEarning,
                Amount = credits,
                Balance = account.Balance,
                Description = $"Earned {credits} credits for processing {tokenCount} tokens",
                RelatedEntityId = relatedEntityId,
                Multiplier = effectiveMultiplier
            };

            return await cosmo.CreateCreditTransactionAsync(transaction);
        }

        /// <summary>
        /// Debit credits from a consumer account. Returns false if insufficient balance.
        /// </summary>
        public async Task<bool> SpendCreditsAsync(string accountId, long amount, string? relatedEntityId = null)
        {
            var account = await cosmo.GetOrCreateCreditAccountAsync(accountId);

            if (account.Balance < amount)
                return false;

            account.Balance -= amount;
            account.TotalSpent += amount;
            await cosmo.UpdateCreditAccountBalanceAsync(account);

            var transaction = new CreditTransaction
            {
                AccountId = accountId,
                Type = CreditTransactionType.InferenceSpend,
                Amount = -amount,
                Balance = account.Balance,
                Description = $"Spent {amount} credits on inference",
                RelatedEntityId = relatedEntityId,
                Multiplier = 1.0
            };

            await cosmo.CreateCreditTransactionAsync(transaction);
            return true;
        }

        /// <summary>
        /// Quick balance check for pre-flight validation.
        /// </summary>
        public async Task<bool> HasSufficientCreditsAsync(string accountId, long estimatedCost)
        {
            var account = await cosmo.GetCreditAccountAsync(accountId);
            if (account is null)
                return false;

            return account.Balance >= estimatedCost;
        }

        /// <summary>
        /// Add purchased credits to an account.
        /// </summary>
        public async Task<CreditTransaction> PurchaseCreditsAsync(string accountId, long amount, string? description = null)
        {
            var account = await cosmo.GetOrCreateCreditAccountAsync(accountId);

            account.Balance += amount;
            account.TotalPurchased += amount;
            await cosmo.UpdateCreditAccountBalanceAsync(account);

            var transaction = new CreditTransaction
            {
                AccountId = accountId,
                Type = CreditTransactionType.Purchase,
                Amount = amount,
                Balance = account.Balance,
                Description = description ?? $"Purchased {amount} credits",
                Multiplier = 1.0
            };

            return await cosmo.CreateCreditTransactionAsync(transaction);
        }

        /// <summary>
        /// Award uptime credits based on minutes online.
        /// </summary>
        public async Task<CreditTransaction> AwardUptimeCreditsAsync(string accountId, string hostId, int minutes)
        {
            var account = await cosmo.GetOrCreateCreditAccountAsync(accountId);
            double effectiveMultiplier = account.UptimeEarnMultiplier;

            long credits = (long)(DefaultUptimeCreditsPerHour * (minutes / 60.0) * effectiveMultiplier);
            if (credits <= 0)
                credits = 1; // Minimum 1 credit for any uptime period

            account.Balance += credits;
            account.TotalEarned += credits;
            await cosmo.UpdateCreditAccountBalanceAsync(account);

            var uptimePeriod = new UptimePeriod
            {
                AccountId = accountId,
                HostId = hostId,
                DateStarted = DateTime.UtcNow.AddMinutes(-minutes),
                DateEnded = DateTime.UtcNow,
                TotalMinutes = minutes,
                CreditsPaid = credits,
                BonusTier = await CalculateUptimeBonusTierAsync(accountId, hostId)
            };
            await cosmo.CreateUptimePeriodAsync(uptimePeriod);

            var host = await cosmo.GetHostAsync(accountId, hostId);
            var hostLabel = host?.Name ?? hostId;

            var transaction = new CreditTransaction
            {
                AccountId = accountId,
                Type = CreditTransactionType.UptimeEarning,
                Amount = credits,
                Balance = account.Balance,
                Description = $"Earned {credits} credits for {minutes} minutes of uptime on {hostLabel}",
                RelatedEntityId = hostId,
                Multiplier = effectiveMultiplier
            };

            return await cosmo.CreateCreditTransactionAsync(transaction);
        }

        /// <summary>
        /// Admin operation to adjust multipliers for an account.
        /// </summary>
        public async Task<CreditAccount> SetMultipliersAsync(string accountId, double? tokenMultiplier = null, double? uptimeMultiplier = null)
        {
            return await cosmo.PatchCreditAccountMultipliersAsync(accountId, tokenMultiplier, uptimeMultiplier);
        }

        /// <summary>
        /// Admin operation to adjust credits (positive or negative).
        /// </summary>
        public async Task<CreditTransaction> AdjustCreditsAsync(string accountId, long amount, string? description = null)
        {
            var account = await cosmo.GetOrCreateCreditAccountAsync(accountId);

            account.Balance += amount;
            if (amount > 0)
                account.TotalEarned += amount;
            else
                account.TotalSpent += Math.Abs(amount);
            await cosmo.UpdateCreditAccountBalanceAsync(account);

            var transaction = new CreditTransaction
            {
                AccountId = accountId,
                Type = CreditTransactionType.AdminAdjustment,
                Amount = amount,
                Balance = account.Balance,
                Description = description ?? $"Admin adjustment of {amount} credits",
                Multiplier = 1.0
            };

            return await cosmo.CreateCreditTransactionAsync(transaction);
        }

        /// <summary>
        /// Get the credit account for an account.
        /// </summary>
        public async Task<CreditAccount> GetBalanceAsync(string accountId)
        {
            return await cosmo.GetOrCreateCreditAccountAsync(accountId);
        }

        /// <summary>
        /// Calculate uptime bonus tier from a rolling 30-day window.
        /// </summary>
        public async Task<UptimeBonusTier> CalculateUptimeBonusTierAsync(string accountId, string? hostId = null)
        {
            var startDate = DateTime.UtcNow.AddDays(-30);
            var periods = await cosmo.GetUptimePeriodsAsync(accountId, hostId, startDate, DateTime.UtcNow);

            int totalMinutesOnline = periods.Sum(p => p.TotalMinutes);
            int totalMinutesInWindow = 30 * 24 * 60; // 30 days in minutes
            double uptimePercentage = (double)totalMinutesOnline / totalMinutesInWindow * 100;

            return uptimePercentage switch
            {
                >= 99 => UptimeBonusTier.Gold,
                >= 95 => UptimeBonusTier.Silver,
                >= 90 => UptimeBonusTier.Bronze,
                _ => UptimeBonusTier.None
            };
        }

        /// <summary>
        /// Process an inference receipt from a host (used for DC proof-of-work).
        /// Awards credits to host, debits consumer if they are different accounts.
        /// </summary>
        public async Task ProcessInferenceReceiptAsync(InferenceReceipt receipt, string hostAccountId)
        {
            // If the host and consumer are the same account, no credits change
            if (hostAccountId == receipt.ConsumerAccountId)
                return;

            // Award credits to the host
            await EarnTokenCreditsAsync(hostAccountId, receipt.TokenCount, receipt.InferenceId);

            // Debit the consumer (best-effort; if they don't have balance, still award host)
            await SpendCreditsAsync(receipt.ConsumerAccountId, receipt.TokenCount, receipt.InferenceId);
        }

        /// <summary>
        /// Debit credits for a marketplace purchase. Returns the transaction or null if insufficient balance.
        /// </summary>
        public async Task<CreditTransaction?> SpendMarketplaceCreditsAsync(string accountId, long amount, string itemId, string itemName)
        {
            var account = await cosmo.GetOrCreateCreditAccountAsync(accountId);

            if (account.Balance < amount)
                return null;

            account.Balance -= amount;
            account.TotalSpent += amount;
            await cosmo.UpdateCreditAccountBalanceAsync(account);

            var transaction = new CreditTransaction
            {
                AccountId = accountId,
                Type = CreditTransactionType.MarketplacePurchase,
                Amount = -amount,
                Balance = account.Balance,
                Description = $"Purchased marketplace item: {itemName}",
                RelatedEntityId = itemId,
                Multiplier = 1.0
            };

            return await cosmo.CreateCreditTransactionAsync(transaction);
        }

        /// <summary>
        /// Credit a provider account with earnings from a marketplace sale.
        /// </summary>
        public async Task<CreditTransaction> EarnProviderCreditsAsync(string providerAccountId, long amount, string itemId)
        {
            var account = await cosmo.GetOrCreateCreditAccountAsync(providerAccountId);

            account.Balance += amount;
            account.TotalEarned += amount;
            await cosmo.UpdateCreditAccountBalanceAsync(account);

            var transaction = new CreditTransaction
            {
                AccountId = providerAccountId,
                Type = CreditTransactionType.ProviderEarning,
                Amount = amount,
                Balance = account.Balance,
                Description = $"Marketplace provider earning: {amount} credits",
                RelatedEntityId = itemId,
                Multiplier = 1.0
            };

            return await cosmo.CreateCreditTransactionAsync(transaction);
        }

        /// <summary>
        /// Debit credits for a subscription renewal. Returns the transaction or null if insufficient balance.
        /// </summary>
        public async Task<CreditTransaction?> SpendSubscriptionRenewalCreditsAsync(string accountId, long amount, string itemId, string itemName)
        {
            var account = await cosmo.GetOrCreateCreditAccountAsync(accountId);

            if (account.Balance < amount)
                return null;

            account.Balance -= amount;
            account.TotalSpent += amount;
            await cosmo.UpdateCreditAccountBalanceAsync(account);

            var transaction = new CreditTransaction
            {
                AccountId = accountId,
                Type = CreditTransactionType.SubscriptionRenewal,
                Amount = -amount,
                Balance = account.Balance,
                Description = $"Subscription renewal: {itemName}",
                RelatedEntityId = itemId,
                Multiplier = 1.0
            };

            return await cosmo.CreateCreditTransactionAsync(transaction);
        }

        /// <summary>
        /// Debit credits for a premium provider subscription. Returns the transaction or null if insufficient balance.
        /// </summary>
        public async Task<CreditTransaction?> SpendPremiumCreditsAsync(string accountId, long amount)
        {
            var account = await cosmo.GetOrCreateCreditAccountAsync(accountId);

            if (account.Balance < amount)
                return null;

            account.Balance -= amount;
            account.TotalSpent += amount;
            await cosmo.UpdateCreditAccountBalanceAsync(account);

            var transaction = new CreditTransaction
            {
                AccountId = accountId,
                Type = CreditTransactionType.PremiumSubscription,
                Amount = -amount,
                Balance = account.Balance,
                Description = "Premium provider subscription",
                Multiplier = 1.0
            };

            return await cosmo.CreateCreditTransactionAsync(transaction);
        }

        /// <summary>
        /// Refund prorated premium credits when a provider cancels.
        /// </summary>
        public async Task<CreditTransaction> RefundPremiumCreditsAsync(string accountId, long amount)
        {
            var account = await cosmo.GetOrCreateCreditAccountAsync(accountId);

            account.Balance += amount;
            await cosmo.UpdateCreditAccountBalanceAsync(account);

            var transaction = new CreditTransaction
            {
                AccountId = accountId,
                Type = CreditTransactionType.PremiumRefund,
                Amount = amount,
                Balance = account.Balance,
                Description = $"Premium cancellation refund ({amount} credits)",
                Multiplier = 1.0
            };

            return await cosmo.CreateCreditTransactionAsync(transaction);
        }

        private static double GetUptimeBonusMultiplier(UptimeBonusTier tier)
        {
            return tier switch
            {
                UptimeBonusTier.Gold => 1.5,
                UptimeBonusTier.Silver => 1.2,
                UptimeBonusTier.Bronze => 1.1,
                _ => 1.0
            };
        }
    }
}
