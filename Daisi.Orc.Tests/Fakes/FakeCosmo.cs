using Daisi.Orc.Core.Data;
using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Daisi.Orc.Tests.Fakes
{
    /// <summary>
    /// In-memory fake of the Cosmo credit methods for unit testing.
    /// Stores all credit data in concurrent dictionaries instead of Cosmos DB.
    /// </summary>
    public class FakeCosmo : Cosmo
    {
        public ConcurrentDictionary<string, CreditAccount> CreditAccounts { get; } = new();
        public List<CreditTransaction> Transactions { get; } = new();
        public List<UptimePeriod> UptimePeriods { get; } = new();
        public List<HostRelease> Releases { get; } = new();
        public List<CreditAnomaly> CreditAnomalies { get; } = new();
        public List<Host> Hosts { get; } = new();

        public FakeCosmo() : base(new ConfigurationBuilder().Build(), "unused")
        {
        }

        public override Task<CreditAccount> GetOrCreateCreditAccountAsync(string accountId)
        {
            var account = CreditAccounts.GetOrAdd(accountId, _ => new CreditAccount
            {
                Id = CreditAccount.GetDeterministicId(accountId),
                AccountId = accountId,
                Balance = 0,
                TotalEarned = 0,
                TotalSpent = 0,
                TotalPurchased = 0
            });

            return Task.FromResult(account);
        }

        public override Task<CreditAccount?> GetCreditAccountAsync(string accountId)
        {
            CreditAccounts.TryGetValue(accountId, out var account);
            return Task.FromResult(account);
        }

        public override Task<CreditAccount> UpdateCreditAccountBalanceAsync(CreditAccount creditAccount)
        {
            creditAccount.DateLastUpdated = DateTime.UtcNow;
            CreditAccounts[creditAccount.AccountId] = creditAccount;
            return Task.FromResult(creditAccount);
        }

        public override Task PatchCreditAccountBonusTierAsync(string creditAccountId, string accountId, UptimeBonusTier tier)
        {
            if (CreditAccounts.TryGetValue(accountId, out var account))
            {
                account.CachedBonusTier = tier;
                account.BonusTierCalculatedAt = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        }

        public override Task<CreditAccount> PatchCreditAccountMultipliersAsync(
            string accountId, double? tokenMultiplier, double? uptimeMultiplier)
        {
            var account = CreditAccounts.GetOrAdd(accountId, _ => new CreditAccount
            {
                Id = CreditAccount.GetDeterministicId(accountId),
                AccountId = accountId
            });

            if (tokenMultiplier.HasValue)
                account.TokenEarnMultiplier = tokenMultiplier.Value;
            if (uptimeMultiplier.HasValue)
                account.UptimeEarnMultiplier = uptimeMultiplier.Value;

            account.DateLastUpdated = DateTime.UtcNow;
            return Task.FromResult(account);
        }

        public override Task<CreditTransaction> CreateCreditTransactionAsync(CreditTransaction transaction)
        {
            if (string.IsNullOrWhiteSpace(transaction.Id))
                transaction.Id = GenerateId(CreditTransactionIdPrefix);
            Transactions.Add(transaction);
            return Task.FromResult(transaction);
        }

        public override Task<PagedResult<CreditTransaction>> GetCreditTransactionsAsync(
            string accountId, int? pageSize = 20, int? pageIndex = 0)
        {
            var filtered = Transactions
                .Where(t => t.AccountId == accountId && t.Amount != 0)
                .OrderByDescending(t => t.DateCreated)
                .ToList();

            var size = pageSize ?? 20;
            var index = pageIndex ?? 0;

            return Task.FromResult(new PagedResult<CreditTransaction>
            {
                TotalCount = filtered.Count,
                Items = filtered.Skip(index * size).Take(size).ToList()
            });
        }

        public override Task<UptimePeriod> CreateUptimePeriodAsync(UptimePeriod uptimePeriod)
        {
            if (string.IsNullOrWhiteSpace(uptimePeriod.Id))
                uptimePeriod.Id = GenerateId(UptimePeriodIdPrefix);
            UptimePeriods.Add(uptimePeriod);
            return Task.FromResult(uptimePeriod);
        }

        public override Task<List<UptimePeriod>> GetUptimePeriodsAsync(
            string accountId, string? hostId = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            var query = UptimePeriods.Where(p => p.AccountId == accountId);

            if (hostId is not null)
                query = query.Where(p => p.HostId == hostId);
            if (startDate.HasValue)
                query = query.Where(p => p.DateStarted >= startDate.Value);
            if (endDate.HasValue)
                query = query.Where(p => p.DateStarted <= endDate.Value);

            return Task.FromResult(query.ToList());
        }

        /// <summary>
        /// Seed a credit account with a specific balance for testing.
        /// </summary>
        public CreditAccount SeedAccount(string accountId, long balance = 0,
            double tokenMultiplier = 1.0, double uptimeMultiplier = 1.0,
            UptimeBonusTier cachedBonusTier = UptimeBonusTier.None)
        {
            var account = new CreditAccount
            {
                Id = CreditAccount.GetDeterministicId(accountId),
                AccountId = accountId,
                Balance = balance,
                TotalEarned = 0,
                TotalSpent = 0,
                TotalPurchased = 0,
                TokenEarnMultiplier = tokenMultiplier,
                UptimeEarnMultiplier = uptimeMultiplier,
                CachedBonusTier = cachedBonusTier
            };

            CreditAccounts[accountId] = account;
            return account;
        }

        /// <summary>
        /// Seed uptime periods for a host to simulate uptime history.
        /// </summary>
        public void SeedUptimePeriods(string accountId, string hostId, int totalMinutes)
        {
            UptimePeriods.Add(new UptimePeriod
            {
                Id = GenerateId(UptimePeriodIdPrefix),
                AccountId = accountId,
                HostId = hostId,
                DateStarted = DateTime.UtcNow.AddMinutes(-totalMinutes),
                DateEnded = DateTime.UtcNow,
                TotalMinutes = totalMinutes,
                CreditsPaid = 0
            });
        }

        public override Task<Host?> GetHostAsync(string accountId, string hostId)
        {
            var host = Hosts.FirstOrDefault(h => h.AccountId == accountId && h.Id == hostId);
            return Task.FromResult(host);
        }

        public override Task<HostRelease?> GetActiveReleaseAsync(string releaseGroup)
        {
            var release = Releases.FirstOrDefault(r => r.ReleaseGroup == releaseGroup && r.IsActive);
            return Task.FromResult(release);
        }

        public override Task<CreditAnomaly> CreateCreditAnomalyAsync(CreditAnomaly anomaly)
        {
            if (string.IsNullOrWhiteSpace(anomaly.Id))
                anomaly.Id = GenerateId(CreditAnomalyIdPrefix);
            CreditAnomalies.Add(anomaly);
            return Task.FromResult(anomaly);
        }

        public override Task<PagedResult<CreditAnomaly>> GetCreditAnomaliesAsync(
            string? accountId = null,
            AnomalyType? type = null,
            AnomalyStatus? status = null,
            int? pageSize = 20,
            int? pageIndex = 0)
        {
            var query = CreditAnomalies.AsEnumerable();

            if (!string.IsNullOrEmpty(accountId))
                query = query.Where(a => a.AccountId == accountId);
            if (type.HasValue)
                query = query.Where(a => a.Type == type.Value);
            if (status.HasValue)
                query = query.Where(a => a.Status == status.Value);

            var filtered = query.OrderByDescending(a => a.DateCreated).ToList();
            var size = pageSize ?? 20;
            var index = pageIndex ?? 0;

            return Task.FromResult(new PagedResult<CreditAnomaly>
            {
                TotalCount = filtered.Count,
                Items = filtered.Skip(index * size).Take(size).ToList()
            });
        }

        public override Task<CreditAnomaly> UpdateCreditAnomalyStatusAsync(
            string anomalyId, string accountId, AnomalyStatus newStatus, string? reviewedBy)
        {
            var anomaly = CreditAnomalies.FirstOrDefault(a => a.Id == anomalyId && a.AccountId == accountId);
            if (anomaly is null)
                throw new Exception($"Anomaly {anomalyId} not found for account {accountId}");

            anomaly.Status = newStatus;
            anomaly.DateReviewed = DateTime.UtcNow;
            anomaly.ReviewedBy = reviewedBy;
            return Task.FromResult(anomaly);
        }
    }
}
