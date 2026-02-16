using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string CreditAnomalyIdPrefix = "can";
        public const string CreditAnomaliesContainerName = "CreditAnomalies";
        public const string CreditAnomaliesPartitionKeyName = "AccountId";

        public virtual async Task<CreditAnomaly> CreateCreditAnomalyAsync(CreditAnomaly anomaly)
        {
            var container = await GetContainerAsync(CreditAnomaliesContainerName);
            var item = await container.CreateItemAsync(anomaly, new PartitionKey(anomaly.AccountId));
            return item.Resource;
        }

        public virtual async Task<PagedResult<CreditAnomaly>> GetCreditAnomaliesAsync(
            string? accountId = null,
            AnomalyType? type = null,
            AnomalyStatus? status = null,
            int? pageSize = 20,
            int? pageIndex = 0)
        {
            var container = await GetContainerAsync(CreditAnomaliesContainerName);

            var queryText = "SELECT * FROM c WHERE 1=1";

            if (!string.IsNullOrEmpty(accountId))
                queryText += " AND c.AccountId = @accountId";
            if (type.HasValue)
                queryText += " AND c.Type = @type";
            if (status.HasValue)
                queryText += " AND c.Status = @status";

            queryText += " ORDER BY c.DateCreated DESC";

            var queryDef = new QueryDefinition(queryText);

            if (!string.IsNullOrEmpty(accountId))
                queryDef = queryDef.WithParameter("@accountId", accountId);
            if (type.HasValue)
                queryDef = queryDef.WithParameter("@type", (int)type.Value);
            if (status.HasValue)
                queryDef = queryDef.WithParameter("@status", (int)status.Value);

            // Get total count first
            var countQuery = new QueryDefinition(
                queryText.Replace("SELECT *", "SELECT VALUE COUNT(1)").Replace(" ORDER BY c.DateCreated DESC", ""));
            if (!string.IsNullOrEmpty(accountId))
                countQuery = countQuery.WithParameter("@accountId", accountId);
            if (type.HasValue)
                countQuery = countQuery.WithParameter("@type", (int)type.Value);
            if (status.HasValue)
                countQuery = countQuery.WithParameter("@status", (int)status.Value);

            int totalCount = 0;
            using (var countIterator = container.GetItemQueryIterator<int>(countQuery))
            {
                while (countIterator.HasMoreResults)
                {
                    var countResponse = await countIterator.ReadNextAsync();
                    totalCount = countResponse.FirstOrDefault();
                }
            }

            // Apply paging via OFFSET/LIMIT
            var size = pageSize ?? 20;
            var index = pageIndex ?? 0;
            queryText += " OFFSET @offset LIMIT @limit";
            queryDef = new QueryDefinition(queryText);

            if (!string.IsNullOrEmpty(accountId))
                queryDef = queryDef.WithParameter("@accountId", accountId);
            if (type.HasValue)
                queryDef = queryDef.WithParameter("@type", (int)type.Value);
            if (status.HasValue)
                queryDef = queryDef.WithParameter("@status", (int)status.Value);

            queryDef = queryDef
                .WithParameter("@offset", index * size)
                .WithParameter("@limit", size);

            var results = new List<CreditAnomaly>();
            using var iterator = container.GetItemQueryIterator<CreditAnomaly>(queryDef);
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response);
            }

            return new PagedResult<CreditAnomaly>
            {
                Items = results,
                TotalCount = totalCount
            };
        }

        public virtual async Task<CreditAnomaly> UpdateCreditAnomalyStatusAsync(
            string anomalyId, string accountId, AnomalyStatus newStatus, string? reviewedBy)
        {
            var container = await GetContainerAsync(CreditAnomaliesContainerName);

            List<PatchOperation> patchOperations = new()
            {
                PatchOperation.Replace("/Status", (int)newStatus),
                PatchOperation.Replace("/DateReviewed", DateTime.UtcNow),
                PatchOperation.Replace("/ReviewedBy", reviewedBy)
            };

            var response = await container.PatchItemAsync<CreditAnomaly>(
                anomalyId, new PartitionKey(accountId), patchOperations);
            return response.Resource;
        }
    }
}
