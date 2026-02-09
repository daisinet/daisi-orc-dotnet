using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string CreditAccountIdPrefix = "cred";
        public const string CreditTransactionIdPrefix = "ctx";
        public const string UptimePeriodIdPrefix = "upt";
        public const string CreditsContainerName = "Credits";
        public const string CreditsPartitionKeyName = "AccountId";

        public virtual async Task<CreditAccount> GetOrCreateCreditAccountAsync(string accountId)
        {
            var existing = await GetCreditAccountAsync(accountId);
            if (existing is not null)
                return existing;

            var account = new CreditAccount
            {
                AccountId = accountId,
                Balance = 0,
                TotalEarned = 0,
                TotalSpent = 0,
                TotalPurchased = 0
            };

            var container = await GetContainerAsync(CreditsContainerName);
            var item = await container.CreateItemAsync(account, new PartitionKey(accountId));
            return item.Resource;
        }

        public virtual async Task<CreditAccount?> GetCreditAccountAsync(string accountId)
        {
            var container = await GetContainerAsync(CreditsContainerName);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.AccountId = @accountId AND IS_DEFINED(c.Balance)")
                .WithParameter("@accountId", accountId);

            using FeedIterator<CreditAccount> iterator = container.GetItemQueryIterator<CreditAccount>(query);
            while (iterator.HasMoreResults)
            {
                FeedResponse<CreditAccount> response = await iterator.ReadNextAsync();
                if (response.Count > 0)
                    return response.First();
            }

            return null;
        }

        public virtual async Task<CreditAccount> UpdateCreditAccountBalanceAsync(CreditAccount creditAccount)
        {
            List<PatchOperation> patchOperations = new()
            {
                PatchOperation.Replace("/Balance", creditAccount.Balance),
                PatchOperation.Replace("/TotalEarned", creditAccount.TotalEarned),
                PatchOperation.Replace("/TotalSpent", creditAccount.TotalSpent),
                PatchOperation.Replace("/TotalPurchased", creditAccount.TotalPurchased),
                PatchOperation.Replace("/DateLastUpdated", DateTime.UtcNow)
            };

            var container = await GetContainerAsync(CreditsContainerName);
            var response = await container.PatchItemAsync<CreditAccount>(
                creditAccount.Id, new PartitionKey(creditAccount.AccountId), patchOperations);
            return response.Resource;
        }

        public virtual async Task<CreditAccount> PatchCreditAccountMultipliersAsync(string accountId, double? tokenMultiplier, double? uptimeMultiplier)
        {
            var account = await GetOrCreateCreditAccountAsync(accountId);

            List<PatchOperation> patchOperations = new();

            if (tokenMultiplier.HasValue)
                patchOperations.Add(PatchOperation.Replace("/TokenEarnMultiplier", tokenMultiplier.Value));

            if (uptimeMultiplier.HasValue)
                patchOperations.Add(PatchOperation.Replace("/UptimeEarnMultiplier", uptimeMultiplier.Value));

            patchOperations.Add(PatchOperation.Replace("/DateLastUpdated", DateTime.UtcNow));

            var container = await GetContainerAsync(CreditsContainerName);
            var response = await container.PatchItemAsync<CreditAccount>(
                account.Id, new PartitionKey(accountId), patchOperations);
            return response.Resource;
        }

        public virtual async Task<CreditTransaction> CreateCreditTransactionAsync(CreditTransaction transaction)
        {
            var container = await GetContainerAsync(CreditsContainerName);
            var item = await container.CreateItemAsync(transaction, new PartitionKey(transaction.AccountId));
            return item.Resource;
        }

        public virtual async Task<PagedResult<CreditTransaction>> GetCreditTransactionsAsync(string accountId, int? pageSize = 20, int? pageIndex = 0)
        {
            var container = await GetContainerAsync(CreditsContainerName);
            var query = container.GetItemLinqQueryable<CreditTransaction>()
                .Where(t => t.AccountId == accountId && t.Amount != 0)
                .OrderByDescending(t => t.DateCreated);

            var results = await query.ToPagedResultAsync(pageSize, pageIndex);
            return results;
        }

        public virtual async Task<UptimePeriod> CreateUptimePeriodAsync(UptimePeriod uptimePeriod)
        {
            var container = await GetContainerAsync(CreditsContainerName);
            var item = await container.CreateItemAsync(uptimePeriod, new PartitionKey(uptimePeriod.AccountId));
            return item.Resource;
        }

        public virtual async Task<List<UptimePeriod>> GetUptimePeriodsAsync(string accountId, string? hostId = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            var container = await GetContainerAsync(CreditsContainerName);

            var queryText = "SELECT * FROM c WHERE c.AccountId = @accountId AND IS_DEFINED(c.HostId)";
            if (hostId is not null)
                queryText += " AND c.HostId = @hostId";
            if (startDate.HasValue)
                queryText += " AND c.DateStarted >= @startDate";
            if (endDate.HasValue)
                queryText += " AND c.DateStarted <= @endDate";

            var queryDef = new QueryDefinition(queryText)
                .WithParameter("@accountId", accountId);

            if (hostId is not null)
                queryDef = queryDef.WithParameter("@hostId", hostId);
            if (startDate.HasValue)
                queryDef = queryDef.WithParameter("@startDate", startDate.Value);
            if (endDate.HasValue)
                queryDef = queryDef.WithParameter("@endDate", endDate.Value);

            List<UptimePeriod> results = new();
            using FeedIterator<UptimePeriod> iterator = container.GetItemQueryIterator<UptimePeriod>(queryDef);
            while (iterator.HasMoreResults)
            {
                FeedResponse<UptimePeriod> response = await iterator.ReadNextAsync();
                results.AddRange(response);
            }

            return results;
        }
    }
}
