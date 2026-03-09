namespace Daisi.Orc.Core.Data.Models
{
    /// <summary>
    /// Represents a registered MCP server connection for an account.
    /// Stored in the McpServers Cosmos container, partitioned by AccountId.
    /// </summary>
    public class McpServerRecord
    {
        public string Id { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ServerUrl { get; set; } = string.Empty;
        public string AuthType { get; set; } = "NONE";
        public string AuthSecretEncrypted { get; set; } = string.Empty;
        public string Status { get; set; } = "PENDING";
        public List<McpResourceRecord> DiscoveredResources { get; set; } = [];
        public int SyncIntervalMinutes { get; set; } = 60;
        public string RepositoryId { get; set; } = string.Empty;
        public string CreatedByUserId { get; set; } = string.Empty;
        public string CreatedByUserName { get; set; } = string.Empty;
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public DateTime? DateLastSync { get; set; }
        public string? LastError { get; set; }
        public int ResourcesSynced { get; set; }
    }

    /// <summary>
    /// A resource discovered from an MCP server.
    /// </summary>
    public class McpResourceRecord
    {
        public string Uri { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool Enabled { get; set; } = true;
        public DateTime? DateLastSync { get; set; }
    }
}
