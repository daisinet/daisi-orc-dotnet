using Daisi.Orc.Core.Data.Db;
using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models;

public class ToolExecutionRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Cosmo.GenerateId(Cosmo.ToolExecutionIdPrefix);

    /// <summary>
    /// Partition key — the consumer who triggered the execution.
    /// </summary>
    public string ConsumerAccountId { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    public string MarketplaceItemId { get; set; } = string.Empty;
    public string ProviderAccountId { get; set; } = string.Empty;

    /// <summary>
    /// Snapshot of the item's ExecutionCreditCost at the time of execution.
    /// </summary>
    public long ExecutionCost { get; set; }

    /// <summary>
    /// Null = unprocessed; set to the billing transaction ID once billed.
    /// </summary>
    public string? TransactionId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
