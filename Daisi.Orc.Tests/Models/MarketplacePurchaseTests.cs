using Daisi.Orc.Core.Data.Models.Marketplace;
using System.Text.Json;

namespace Daisi.Orc.Tests.Models;

public class MarketplacePurchaseTests
{
    [Fact]
    public void BundleInstallId_DefaultsToNull()
    {
        var purchase = new MarketplacePurchase();

        Assert.Null(purchase.BundleInstallId);
    }

    [Fact]
    public void BundleInstallId_CanBeSet()
    {
        var purchase = new MarketplacePurchase
        {
            BundleInstallId = "binst-abc123"
        };

        Assert.Equal("binst-abc123", purchase.BundleInstallId);
    }

    [Fact]
    public void BundleInstallId_SerializesToJson()
    {
        var purchase = new MarketplacePurchase
        {
            AccountId = "acct-1",
            MarketplaceItemId = "item-1",
            MarketplaceItemName = "Test Plugin",
            SecureInstallId = "inst-001",
            BundleInstallId = "binst-shared"
        };

        var json = JsonSerializer.Serialize(purchase);
        Assert.Contains("\"BundleInstallId\":\"binst-shared\"", json);
    }

    [Fact]
    public void BundleInstallId_DeserializesFromJson()
    {
        var json = """
        {
            "id": "pur-123",
            "type": "MarketplacePurchase",
            "AccountId": "acct-1",
            "MarketplaceItemId": "item-1",
            "MarketplaceItemName": "Test Plugin",
            "SecureInstallId": "inst-001",
            "BundleInstallId": "binst-shared",
            "IsActive": true
        }
        """;

        var purchase = JsonSerializer.Deserialize<MarketplacePurchase>(json);

        Assert.NotNull(purchase);
        Assert.Equal("binst-shared", purchase.BundleInstallId);
        Assert.Equal("inst-001", purchase.SecureInstallId);
    }

    [Fact]
    public void BundleInstallId_NullWhenNotInJson()
    {
        var json = """
        {
            "id": "pur-123",
            "type": "MarketplacePurchase",
            "AccountId": "acct-1",
            "MarketplaceItemId": "item-1",
            "MarketplaceItemName": "Test Tool",
            "SecureInstallId": "inst-001",
            "IsActive": true
        }
        """;

        var purchase = JsonSerializer.Deserialize<MarketplacePurchase>(json);

        Assert.NotNull(purchase);
        Assert.Null(purchase.BundleInstallId);
        Assert.Equal("inst-001", purchase.SecureInstallId);
    }
}
