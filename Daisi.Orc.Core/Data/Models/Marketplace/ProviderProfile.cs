using Daisi.Orc.Core.Data.Db;
using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models.Marketplace;

public enum ProviderStatus
{
    Pending,
    Approved,
    Suspended
}

public class ProviderProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Cosmo.GenerateId(Cosmo.ProviderIdPrefix);
    public string type { get; set; } = "ProviderProfile";

    public string AccountId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string LogoSvg { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProviderStatus Status { get; set; } = ProviderStatus.Pending;

    /// <summary>
    /// Admin-configurable per provider. Default 70%.
    /// </summary>
    public double RevenueSharePercent { get; set; } = 70.0;

    public long TotalEarnings { get; set; }
    public long PendingPayout { get; set; }
    public int ItemCount { get; set; }

    public bool IsPremium { get; set; }
    public DateTime? PremiumExpiresAt { get; set; }
    public string? PremiumTransactionId { get; set; }

    /// <summary>
    /// Provider's markdown profile content (about, goals, business info).
    /// </summary>
    public string ProfileMarkdown { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
