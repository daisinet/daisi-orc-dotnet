using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string InferenceIdPrefix = "inf";
        public const string InferencesContainerName = "Inferences";
        public const string InferencesPartitionKeyName = "AccountId";

        public PartitionKey GetPartitianKey(Inference inference)
        {
            return new PartitionKey(inference.AccountId);
        }

        public async Task<Inference> Create(Inference inference)
        {
            var container = await GetContainerAsync(InferencesContainerName);
            var item = await container.CreateItemAsync(inference);
            return item.Resource;
        }

        public async Task<List<InferenceMessageStat>> GetInferenceMessageStatsForHostAsync(
            string hostId, DateTime? startDate = null)
        {
            var container = await GetContainerAsync(InferencesContainerName);

            var queryText = "SELECT m.TokenCount, m.TokenProcessingSeconds, m.DateCreated " +
                            "FROM c JOIN m IN c.Messages " +
                            "WHERE m.HostId = @hostId";

            if (startDate.HasValue)
                queryText += " AND m.DateCreated >= @startDate";

            queryText += " ORDER BY m.DateCreated ASC";

            var queryDef = new QueryDefinition(queryText)
                .WithParameter("@hostId", hostId);

            if (startDate.HasValue)
                queryDef = queryDef.WithParameter("@startDate", startDate.Value);

            var results = new List<InferenceMessageStat>();
            using var iterator = container.GetItemQueryIterator<InferenceMessageStat>(queryDef);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response);
            }

            return results;
        }
    }
}
