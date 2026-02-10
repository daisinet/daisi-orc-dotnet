using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models.Skills;

public class InstalledSkill
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    public string type { get; set; } = "InstalledSkill";
    public string SkillId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public DateTime EnabledAt { get; set; } = DateTime.UtcNow;
}
