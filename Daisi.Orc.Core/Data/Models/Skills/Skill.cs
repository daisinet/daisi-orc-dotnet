using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models.Skills;

public class Skill
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    public string type { get; set; } = "Skill";
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string IconUrl { get; set; } = string.Empty;
    public List<string> RequiredToolGroups { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public string Visibility { get; set; } = "Private";
    public string Status { get; set; } = "Draft";
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int DownloadCount { get; set; }
    public string SystemPromptTemplate { get; set; } = string.Empty;
    public bool IsRequired { get; set; } = false;
}
