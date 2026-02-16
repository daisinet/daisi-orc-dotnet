using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models.Marketplace;

public class MarketplaceSettings
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "marketplace-settings";
    public string type { get; set; } = "MarketplaceSettings";

    public string AccountId { get; set; } = "system";

    public long PremiumMonthlyCreditCost { get; set; } = 500;
    public int MaxFeaturedItemsPerProvider { get; set; } = 5;
}
