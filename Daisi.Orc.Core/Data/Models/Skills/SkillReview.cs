using Newtonsoft.Json;

namespace Daisi.Orc.Core.Data.Models.Skills;

public class SkillReview
{
    [JsonProperty(PropertyName = "id")]
    public string Id { get; set; } = string.Empty;
    public string type { get; set; } = "SkillReview";
    public string SkillId { get; set; } = string.Empty;
    public string ReviewerEmail { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
