using Daisi.Orc.Core.Data.Db;
using Daisi.Protos.V1;
using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models.Marketplace;

public enum MarketplaceItemStatus
{
    Draft,
    PendingReview,
    AutoTested,
    Approved,
    Rejected,
    Suspended
}

public class MarketplaceItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Cosmo.GenerateId(Cosmo.MarketplaceIdPrefix);
    public string type { get; set; } = "MarketplaceItem";

    public string AccountId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string IconUrl { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];
    public List<string> Screenshots { get; set; } = [];

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MarketplaceItemType ItemType { get; set; } = MarketplaceItemType.Skill;


    /// <summary>
    /// For skills — links to existing Skill entity.
    /// </summary>
    public string? SkillId { get; set; }

    /// <summary>
    /// For tools — fully qualified class name.
    /// </summary>
    public string? ToolClassName { get; set; }

    /// <summary>
    /// For plugins — child marketplace item IDs.
    /// </summary>
    public List<string> BundledItemIds { get; set; } = [];

    /// <summary>
    /// URL to uploaded ZIP/DLL in blob storage.
    /// </summary>
    public string? PackageBlobUrl { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MarketplacePricingModel PricingModel { get; set; } = MarketplacePricingModel.MarketplacePricingFree;

    /// <summary>
    /// One-time cost in credits.
    /// </summary>
    public long CreditPrice { get; set; }

    /// <summary>
    /// Recurring cost per period in credits.
    /// </summary>
    public long SubscriptionCreditPrice { get; set; }

    /// <summary>
    /// Subscription period in days (30 = monthly, etc.)
    /// </summary>
    public int SubscriptionPeriodDays { get; set; } = 30;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MarketplaceItemStatus Status { get; set; } = MarketplaceItemStatus.Draft;

    public string Visibility { get; set; } = "Private";

    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }

    public int DownloadCount { get; set; }
    public int PurchaseCount { get; set; }
    public double AverageRating { get; set; }
    public int RatingCount { get; set; }
    public bool IsFeatured { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
