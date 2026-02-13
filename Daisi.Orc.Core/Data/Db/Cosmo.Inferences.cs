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

        public async Task RecordInferenceMessageAsync(string hostId, string hostAccountId, string inferenceId, string sessionId, int tokenCount, float tokenProcessingSeconds)
        {
            var container = await GetContainerAsync(InferencesContainerName);

            var inference = new Inference
            {
                AccountId = hostAccountId,
                DateCreated = DateTime.UtcNow,
                DateLastMessage = DateTime.UtcNow,
                CreatedSessionId = sessionId,
                TotalTokenCount = tokenCount,
                TokenProcessingSeconds = tokenProcessingSeconds,
                Messages = new List<InferenceMessage>
                {
                    new InferenceMessage
                    {
                        Id = GenerateId(InferenceIdPrefix),
                        InferenceId = inferenceId,
                        SessionId = sessionId,
                        HostId = hostId,
                        TokenCount = tokenCount,
                        TokenProcessingSeconds = tokenProcessingSeconds,
                        DateCreated = DateTime.UtcNow,
                        Author = "Assistant"
                    }
                }
            };

            await container.CreateItemAsync(inference, new PartitionKey(hostAccountId));
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
