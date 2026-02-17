using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models.Marketplace;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace Daisi.Orc.Core.Services;

/// <summary>
/// Manages secure tool lifecycle: install/uninstall notifications to providers
/// and tool discovery with InstallId for direct host-to-provider execution.
/// The ORC is no longer in the execution hot path.
/// </summary>
public class SecureToolService(Cosmo cosmo, IHttpClientFactory httpClientFactory, ILogger<SecureToolService> logger)
{
    /// <summary>
    /// Notify the provider that a tool has been installed (purchased).
    /// Best-effort — logs and continues on failure.
    /// </summary>
    public async Task NotifyProviderInstallAsync(MarketplaceItem item, string installId, string? bundleInstallId = null)
    {
        if (string.IsNullOrEmpty(item.SecureEndpointUrl))
            return;

        try
        {
            var client = httpClientFactory.CreateClient();
            var requestBody = !string.IsNullOrEmpty(bundleInstallId)
                ? (object)new
                {
                    installId,
                    toolId = item.SecureToolDefinition?.ToolId ?? string.Empty,
                    bundleInstallId
                }
                : new
                {
                    installId,
                    toolId = item.SecureToolDefinition?.ToolId ?? string.Empty
                };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{item.SecureEndpointUrl.TrimEnd('/')}/install");
            request.Content = JsonContent.Create(requestBody);

            if (!string.IsNullOrEmpty(item.SecureAuthKey))
                request.Headers.Add("X-Daisi-Auth", item.SecureAuthKey);

            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Provider install notification failed for item {ItemId}: HTTP {StatusCode}", item.Id, response.StatusCode);
            else
                logger.LogInformation("Provider install notification sent for item {ItemId}, installId {InstallId}", item.Id, installId);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to reach provider for install notification on item {ItemId}", item.Id);
        }
    }

    /// <summary>
    /// Notify the provider that a tool has been uninstalled (deactivated).
    /// Best-effort — logs and continues on failure.
    /// </summary>
    public async Task NotifyProviderUninstallAsync(MarketplaceItem item, string installId)
    {
        if (string.IsNullOrEmpty(item.SecureEndpointUrl) || string.IsNullOrEmpty(installId))
            return;

        try
        {
            var client = httpClientFactory.CreateClient();
            var requestBody = new { installId };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{item.SecureEndpointUrl.TrimEnd('/')}/uninstall");
            request.Content = JsonContent.Create(requestBody);

            if (!string.IsNullOrEmpty(item.SecureAuthKey))
                request.Headers.Add("X-Daisi-Auth", item.SecureAuthKey);

            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Provider uninstall notification failed for item {ItemId}: HTTP {StatusCode}", item.Id, response.StatusCode);
            else
                logger.LogInformation("Provider uninstall notification sent for item {ItemId}, installId {InstallId}", item.Id, installId);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to reach provider for uninstall notification on item {ItemId}", item.Id);
        }
    }

    /// <summary>
    /// Get all installed secure tools for an account, including InstallId and EndpointUrl
    /// so hosts can call providers directly.
    /// </summary>
    public async Task<List<InstalledSecureTool>> GetInstalledToolsAsync(string accountId)
    {
        var purchases = await cosmo.GetPurchasesByAccountAsync(accountId);
        var tools = new List<InstalledSecureTool>();
        var seenToolIds = new HashSet<string>();

        foreach (var purchase in purchases)
        {
            if (!purchase.IsActive)
                continue;

            if (purchase.IsSubscription && purchase.SubscriptionExpiresAt.HasValue
                && purchase.SubscriptionExpiresAt.Value <= DateTime.UtcNow)
                continue;

            var item = await cosmo.GetMarketplaceItemByIdAsync(purchase.MarketplaceItemId);
            if (item is null || !item.IsSecureExecution || item.SecureToolDefinition is null)
                continue;

            if (seenToolIds.Add(item.SecureToolDefinition.ToolId))
            {
                tools.Add(new InstalledSecureTool
                {
                    MarketplaceItemId = item.Id,
                    Tool = item.SecureToolDefinition,
                    InstallId = purchase.SecureInstallId ?? string.Empty,
                    EndpointUrl = item.SecureEndpointUrl ?? string.Empty,
                    BundleInstallId = purchase.BundleInstallId ?? string.Empty
                });
            }
        }

        // Also include free secure tools that are approved
        var freeSecureItems = await cosmo.GetApprovedFreeSecureToolsAsync();
        foreach (var item in freeSecureItems)
        {
            if (item.SecureToolDefinition is not null && seenToolIds.Add(item.SecureToolDefinition.ToolId))
            {
                tools.Add(new InstalledSecureTool
                {
                    MarketplaceItemId = item.Id,
                    Tool = item.SecureToolDefinition,
                    InstallId = GenerateDeterministicInstallId(accountId, item.Id),
                    EndpointUrl = item.SecureEndpointUrl ?? string.Empty
                });
            }
        }

        return tools;
    }

    /// <summary>
    /// Generate a deterministic InstallId from accountId + itemId using a hash
    /// so AccountId is never exposed to the provider.
    /// </summary>
    private static string GenerateDeterministicInstallId(string accountId, string itemId)
    {
        var input = Encoding.UTF8.GetBytes($"{accountId}:{itemId}");
        var hash = SHA256.HashData(input);
        return $"inst-{Convert.ToHexString(hash)[..24].ToLower()}";
    }
}

/// <summary>
/// An installed secure tool with provider communication details.
/// </summary>
public class InstalledSecureTool
{
    public string MarketplaceItemId { get; set; } = string.Empty;
    public SecureToolDefinitionData Tool { get; set; } = new();
    public string InstallId { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public string BundleInstallId { get; set; } = string.Empty;
}
