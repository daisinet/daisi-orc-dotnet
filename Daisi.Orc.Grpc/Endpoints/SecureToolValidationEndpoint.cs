using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.CommandServices.Containers;

namespace Daisi.Orc.Grpc.Endpoints;

public static class SecureToolValidationEndpoint
{
    public static void MapSecureToolValidation(this WebApplication app)
    {
        app.MapPost("/api/secure-tools/validate", HandleValidateAsync);
    }

    private static async Task<IResult> HandleValidateAsync(
        HttpContext httpContext,
        Cosmo cosmo,
        MarketplaceService marketplaceService)
    {
        // Read the request body
        var request = await httpContext.Request.ReadFromJsonAsync<ValidateRequest>();
        if (request is null || string.IsNullOrEmpty(request.SessionId) || string.IsNullOrEmpty(request.ToolId))
            return Results.BadRequest(new ValidateResponse { Valid = false, Error = "SessionId and ToolId are required." });

        // Validate the session exists (also extends TTL)
        if (!SessionContainer.TryGet(request.SessionId, out var session))
            return Results.Json(new ValidateResponse { Valid = false, Error = "Invalid or expired session." }, statusCode: 403);

        var consumerAccountId = session.ConsumerAccountId;
        if (string.IsNullOrEmpty(consumerAccountId))
            return Results.Json(new ValidateResponse { Valid = false, Error = "Session has no consumer identity." }, statusCode: 403);

        // Look up the marketplace item by ToolId
        var item = await cosmo.GetSecureToolMarketplaceItemByToolIdAsync(request.ToolId);
        if (item is null)
            return Results.Json(new ValidateResponse { Valid = false, Error = "Tool not found." }, statusCode: 404);

        // Validate X-Daisi-Auth header matches the item's SecureAuthKey
        var authHeader = httpContext.Request.Headers["X-Daisi-Auth"].FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || authHeader != item.SecureAuthKey)
            return Results.Json(new ValidateResponse { Valid = false, Error = "Invalid authentication." }, statusCode: 403);

        // Validate consumer entitlement
        var isEntitled = await marketplaceService.CheckEntitlementAsync(consumerAccountId, item.Id);
        if (!isEntitled)
        {
            // Also check if it's a free tool (free tools are available to all)
            if (item.PricingModel != Daisi.Protos.V1.MarketplacePricingModel.MarketplacePricingFree)
                return Results.Json(new ValidateResponse { Valid = false, Error = "Consumer is not entitled to this tool." }, statusCode: 403);
        }

        // Look up InstallId and BundleInstallId for this consumer + item
        var purchases = await cosmo.GetPurchasesByAccountAsync(consumerAccountId);
        var purchase = purchases.FirstOrDefault(p => p.MarketplaceItemId == item.Id && p.IsActive);
        var installId = purchase?.SecureInstallId ?? GenerateDeterministicInstallId(consumerAccountId, item.Id);
        var bundleInstallId = purchase?.BundleInstallId ?? string.Empty;

        // Record tool execution with cost snapshot
        var record = new ToolExecutionRecord
        {
            SessionId = request.SessionId,
            ConsumerAccountId = consumerAccountId,
            ToolId = request.ToolId,
            MarketplaceItemId = item.Id,
            ProviderAccountId = item.AccountId,
            ExecutionCost = item.ExecutionCreditCost
        };
        await cosmo.RecordToolExecutionAsync(record);

        return Results.Ok(new ValidateResponse
        {
            Valid = true,
            InstallId = installId,
            BundleInstallId = bundleInstallId
        });
    }

    private static string GenerateDeterministicInstallId(string accountId, string itemId)
    {
        var input = System.Text.Encoding.UTF8.GetBytes($"{accountId}:{itemId}");
        var hash = System.Security.Cryptography.SHA256.HashData(input);
        return $"inst-{Convert.ToHexString(hash)[..24].ToLower()}";
    }

    private class ValidateRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public string ToolId { get; set; } = string.Empty;
    }

    private class ValidateResponse
    {
        public bool Valid { get; set; }
        public string InstallId { get; set; } = string.Empty;
        public string BundleInstallId { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }
}
