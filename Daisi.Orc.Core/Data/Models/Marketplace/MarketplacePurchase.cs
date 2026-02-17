using Daisi.Orc.Core.Data.Db;
using Daisi.Protos.V1;
using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models.Marketplace;

public class MarketplacePurchase
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Cosmo.GenerateId(Cosmo.PurchaseIdPrefix);
    public string type { get; set; } = "MarketplacePurchase";

    public string AccountId { get; set; } = string.Empty;
    public string MarketplaceItemId { get; set; } = string.Empty;
    public string MarketplaceItemName { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MarketplaceItemType ItemType { get; set; }

    public long CreditsPaid { get; set; }

    /// <summary>
    /// Links to CreditTransaction.
    /// </summary>
    public string? TransactionId { get; set; }

    public bool IsSubscription { get; set; }
    public DateTime? SubscriptionExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Opaque identifier shared with the provider for this installation.
    /// Generated on purchase for secure execution tools. Never contains AccountId.
    /// </summary>
    public string? SecureInstallId { get; set; }

    /// <summary>
    /// Shared bundle identifier for OAuth. All tools in a plugin bundle share this ID
    /// so users only need to OAuth-connect once per provider.
    /// </summary>
    public string? BundleInstallId { get; set; }
}
