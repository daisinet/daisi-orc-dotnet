using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Cosmos;

namespace Daisi.Orc.Core.Data.Db;

public partial class Cosmo
{
    public const string ToolExecutionIdPrefix = "texe";
    public const string ToolExecutionContainerName = "ToolExecutions";
    public const string ToolExecutionPartitionKeyName = "ConsumerAccountId";

    public async Task<ToolExecutionRecord> RecordToolExecutionAsync(ToolExecutionRecord record)
    {
        var container = await GetContainerAsync(ToolExecutionContainerName);
        var response = await container.CreateItemAsync(record, new PartitionKey(record.ConsumerAccountId));
        return response.Resource;
    }

    /// <summary>
    /// Get unprocessed tool execution records (TransactionId is null), ordered by Timestamp.
    /// Cross-partition query — used by the background billing processor.
    /// </summary>
    public async Task<List<ToolExecutionRecord>> GetUnprocessedToolExecutionsAsync(int limit = 500)
    {
        var container = await GetContainerAsync(ToolExecutionContainerName);
        var query = new QueryDefinition(
            "SELECT TOP @limit * FROM c WHERE NOT IS_DEFINED(c.TransactionId) OR c.TransactionId = null ORDER BY c.Timestamp ASC")
            .WithParameter("@limit", limit);

        var options = new QueryRequestOptions { MaxItemCount = limit };
        var records = new List<ToolExecutionRecord>();
        using var resultSet = container.GetItemQueryIterator<ToolExecutionRecord>(query, requestOptions: options);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            records.AddRange(response);
        }
        return records;
    }

    /// <summary>
    /// Mark a batch of tool execution records as processed by setting the TransactionId.
    /// All records must belong to the same ConsumerAccountId (partition key).
    /// </summary>
    public async Task MarkToolExecutionsProcessedAsync(List<string> ids, string accountId, string transactionId)
    {
        var container = await GetContainerAsync(ToolExecutionContainerName);
        foreach (var id in ids)
        {
            var patchOps = new List<PatchOperation>
            {
                PatchOperation.Set("/TransactionId", transactionId)
            };
            await container.PatchItemAsync<ToolExecutionRecord>(id, new PartitionKey(accountId), patchOps);
        }
    }
}
