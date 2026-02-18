using System.Text.RegularExpressions;
using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models.Marketplace;
using Daisi.Orc.Core.Services;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Daisi.Orc.Grpc.RPCServices.V1;

[Authorize]
public partial class MarketplaceRPC(ILogger<MarketplaceRPC> logger, Cosmo cosmo, MarketplaceService marketplaceService)
    : MarketplaceProto.MarketplaceProtoBase
{
    private static readonly Regex ReservedNamePattern = new(@"\bdaisi(net)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void ValidateProviderDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Display name is required."));

        if (displayName.Length < 3 || displayName.Length > 50)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Display name must be between 3 and 50 characters."));

        if (ReservedNamePattern.IsMatch(displayName))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Display name cannot contain 'daisi' or 'daisinet'. These names are reserved."));
    }

    // --- Catalog ---

    public override async Task<BrowseMarketplaceResponse> BrowseMarketplace(BrowseMarketplaceRequest request, ServerCallContext context)
    {
        MarketplaceItemType? typeFilter = request.FilterByType ? request.ItemType : null;

        var items = await cosmo.GetPublicApprovedMarketplaceItemsAsync(
            request.Search,
            request.Tag,
            typeFilter);

        var response = new BrowseMarketplaceResponse();
        foreach (var item in items)
        {
            response.Items.Add(MapToProto(item));
        }
        return response;
    }

    public override async Task<GetMarketplaceItemResponse> GetMarketplaceItem(GetMarketplaceItemRequest request, ServerCallContext context)
    {
        var item = await cosmo.GetMarketplaceItemByIdAsync(request.Id);
        return new GetMarketplaceItemResponse
        {
            Item = item is not null ? MapToProto(item) : null
        };
    }

    // --- Provider item management ---

    public override async Task<CreateMarketplaceItemResponse> CreateMarketplaceItem(CreateMarketplaceItemRequest request, ServerCallContext context)
    {
        // Enforce premium requirement for paid items
        if (request.Item.PricingModel != MarketplacePricingModel.MarketplacePricingFree)
        {
            var provider = await cosmo.GetProviderProfileAsync(request.Item.AccountId);
            if (provider is null || !provider.IsPremium)
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Only Premium providers can set a price on items"));
        }

        var item = MapFromProto(request.Item);
        item = await cosmo.CreateMarketplaceItemAsync(item);
        logger.LogInformation("Created marketplace item {ItemId} for account {AccountId}", item.Id, item.AccountId);

        if (item.Status == MarketplaceItemStatus.PendingReview)
        {
            _ = NotifyAdminAsync(
                "Marketplace Item Submitted for Review",
                $"A marketplace item has been submitted for review.\n\nName: {item.Name}\nType: {item.ItemType}\nAuthor: {item.Author}\nAccount ID: {item.AccountId}\n\nReview at: https://manager.daisinet.com/admin");
        }

        return new CreateMarketplaceItemResponse
        {
            Item = MapToProto(item)
        };
    }

    public override async Task<UpdateMarketplaceItemResponse> UpdateMarketplaceItem(UpdateMarketplaceItemRequest request, ServerCallContext context)
    {
        var existing = await cosmo.GetMarketplaceItemByIdAsync(request.Item.Id);
        if (existing is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Marketplace item not found"));

        // Enforce premium requirement for paid items
        if (request.Item.PricingModel != MarketplacePricingModel.MarketplacePricingFree)
        {
            var provider = await cosmo.GetProviderProfileAsync(existing.AccountId);
            if (provider is null || !provider.IsPremium)
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Only Premium providers can set a price on items"));
        }

        existing.Name = request.Item.Name;
        existing.Description = request.Item.Description;
        existing.ShortDescription = request.Item.ShortDescription;
        existing.Author = request.Item.Author;
        existing.Version = request.Item.Version;
        existing.IconUrl = request.Item.IconUrl;
        existing.Tags = request.Item.Tags.ToList();
        existing.Screenshots = request.Item.Screenshots.ToList();
        existing.Visibility = request.Item.Visibility;
        var previousStatus = existing.Status;
        existing.Status = System.Enum.Parse<MarketplaceItemStatus>(request.Item.Status);
        existing.PricingModel = request.Item.PricingModel;
        existing.CreditPrice = request.Item.CreditPrice;
        existing.SubscriptionCreditPrice = request.Item.SubscriptionCreditPrice;
        existing.SubscriptionPeriodDays = request.Item.SubscriptionPeriodDays;
        existing.SkillId = request.Item.SkillId;
        existing.ToolClassName = request.Item.ToolClassName;
        existing.BundledItemIds = request.Item.BundledItemIds.ToList();
        existing.RejectionReason = request.Item.RejectionReason;

        // Secure execution fields
        existing.IsSecureExecution = request.Item.IsSecureExecution;
        existing.SecureEndpointUrl = request.Item.SecureEndpointUrl;
        existing.SecureAuthKey = request.Item.SecureAuthKey;
        existing.ExecutionCreditCost = request.Item.ExecutionCreditCost;

        if (request.Item.SetupParameters.Count > 0)
        {
            existing.SetupParameters = request.Item.SetupParameters.Select(sp => new SetupParameterData
            {
                Name = sp.Name,
                Description = sp.Description,
                Type = sp.Type,
                IsRequired = sp.IsRequired,
                AuthUrl = sp.AuthUrl,
                ServiceLabel = sp.ServiceLabel
            }).ToList();
        }
        else
        {
            existing.SetupParameters = [];
        }

        if (request.Item.SecureToolDefinition is not null)
        {
            existing.SecureToolDefinition = new SecureToolDefinitionData
            {
                ToolId = request.Item.SecureToolDefinition.ToolId,
                Name = request.Item.SecureToolDefinition.Name,
                UseInstructions = request.Item.SecureToolDefinition.UseInstructions,
                ToolGroup = request.Item.SecureToolDefinition.ToolGroup,
                Parameters = request.Item.SecureToolDefinition.Parameters.Select(p => new SecureToolParameterData
                {
                    Name = p.Name,
                    Description = p.Description,
                    IsRequired = p.IsRequired
                }).ToList()
            };
        }
        else if (!request.Item.IsSecureExecution)
        {
            existing.SecureToolDefinition = null;
        }

        if (!string.IsNullOrEmpty(request.Item.ReviewedBy))
        {
            existing.ReviewedBy = request.Item.ReviewedBy;
            existing.ReviewedAt = request.Item.ReviewedAt?.ToDateTime();
        }

        await cosmo.UpdateMarketplaceItemAsync(existing);
        logger.LogInformation("Updated marketplace item {ItemId}", existing.Id);

        if (existing.Status == MarketplaceItemStatus.PendingReview && previousStatus != MarketplaceItemStatus.PendingReview)
        {
            _ = NotifyAdminAsync(
                "Marketplace Item Submitted for Review",
                $"A marketplace item has been submitted for review.\n\nName: {existing.Name}\nType: {existing.ItemType}\nAuthor: {existing.Author}\nAccount ID: {existing.AccountId}\n\nReview at: https://manager.daisinet.com/admin");
        }

        return new UpdateMarketplaceItemResponse
        {
            Item = MapToProto(existing)
        };
    }

    public override async Task<UploadPackageResponse> UploadPackage(IAsyncStreamReader<UploadPackageRequest> requestStream, ServerCallContext context)
    {
        // Package upload will be fully implemented in Phase 2 with PackageService
        string? itemId = null;
        using var ms = new MemoryStream();

        await foreach (var chunk in requestStream.ReadAllAsync())
        {
            if (itemId is null)
                itemId = chunk.MarketplaceItemId;

            if (!chunk.ChunkData.IsEmpty)
                chunk.ChunkData.WriteTo(ms);
        }

        if (string.IsNullOrEmpty(itemId))
            return new UploadPackageResponse { Success = false, Error = "No item ID provided" };

        // TODO: Phase 2 — validate package, store in blob storage
        logger.LogInformation("Received package upload for item {ItemId}, size {Size} bytes", itemId, ms.Length);

        return new UploadPackageResponse
        {
            Success = true,
            PackageBlobUrl = $"pending://{itemId}"
        };
    }

    // --- Purchase & Entitlement ---

    public override async Task<PurchaseMarketplaceItemResponse> PurchaseMarketplaceItem(PurchaseMarketplaceItemRequest request, ServerCallContext context)
    {
        var (success, error, purchase) = await marketplaceService.PurchaseItemAsync(request.AccountId, request.MarketplaceItemId);

        var response = new PurchaseMarketplaceItemResponse
        {
            Success = success,
            Error = error ?? string.Empty
        };

        if (purchase is not null)
            response.Purchase = MapPurchaseToProto(purchase);

        return response;
    }

    public override async Task<GetMyPurchasesResponse> GetMyPurchases(GetMyPurchasesRequest request, ServerCallContext context)
    {
        var purchases = await cosmo.GetPurchasesByAccountAsync(request.AccountId);
        var response = new GetMyPurchasesResponse();
        foreach (var purchase in purchases)
        {
            response.Purchases.Add(MapPurchaseToProto(purchase));
        }
        return response;
    }

    public override async Task<CheckEntitlementResponse> CheckEntitlement(CheckEntitlementRequest request, ServerCallContext context)
    {
        var isEntitled = await marketplaceService.CheckEntitlementAsync(request.AccountId, request.MarketplaceItemId);
        return new CheckEntitlementResponse { IsEntitled = isEntitled };
    }

    // --- Provider profiles ---

    public override async Task<GetProviderProfileResponse> GetProviderProfile(GetProviderProfileRequest request, ServerCallContext context)
    {
        var profile = await cosmo.GetProviderProfileAsync(request.AccountId);
        return new GetProviderProfileResponse
        {
            Profile = profile is not null ? MapProviderToProto(profile) : null
        };
    }

    public override async Task<CreateProviderProfileResponse> CreateProviderProfile(CreateProviderProfileRequest request, ServerCallContext context)
    {
        ValidateProviderDisplayName(request.Profile.DisplayName);

        if (await cosmo.IsProviderDisplayNameTakenAsync(request.Profile.DisplayName))
            throw new RpcException(new Status(StatusCode.AlreadyExists, "A provider with this display name already exists. Please choose a different name."));

        var profile = new ProviderProfile
        {
            AccountId = request.Profile.AccountId,
            DisplayName = request.Profile.DisplayName,
            Bio = request.Profile.Bio,
            AvatarUrl = request.Profile.AvatarUrl,
            WebsiteUrl = request.Profile.WebsiteUrl
        };

        profile = await cosmo.CreateProviderProfileAsync(profile);
        logger.LogInformation("Created provider profile {ProfileId} for account {AccountId}", profile.Id, profile.AccountId);

        _ = NotifyAdminAsync(
            "New Marketplace Provider Application",
            $"A new provider profile needs approval.\n\nDisplay Name: {profile.DisplayName}\nAccount ID: {profile.AccountId}\n\nReview at: https://manager.daisinet.com/admin");

        return new CreateProviderProfileResponse
        {
            Profile = MapProviderToProto(profile)
        };
    }

    public override async Task<UpdateProviderProfileResponse> UpdateProviderProfile(UpdateProviderProfileRequest request, ServerCallContext context)
    {
        var existing = await cosmo.GetProviderProfileAsync(request.Profile.AccountId);
        if (existing is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Provider profile not found"));

        // Validate display name if it changed
        if (!string.Equals(existing.DisplayName, request.Profile.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            ValidateProviderDisplayName(request.Profile.DisplayName);

            if (await cosmo.IsProviderDisplayNameTakenAsync(request.Profile.DisplayName, existing.AccountId))
                throw new RpcException(new Status(StatusCode.AlreadyExists, "A provider with this display name already exists. Please choose a different name."));
        }

        existing.DisplayName = request.Profile.DisplayName;
        existing.Bio = request.Profile.Bio;
        existing.AvatarUrl = request.Profile.AvatarUrl;
        existing.WebsiteUrl = request.Profile.WebsiteUrl;
        existing.ProfileMarkdown = request.Profile.ProfileMarkdown;

        await cosmo.UpdateProviderProfileAsync(existing);
        logger.LogInformation("Updated provider profile for account {AccountId}", existing.AccountId);

        return new UpdateProviderProfileResponse
        {
            Profile = MapProviderToProto(existing)
        };
    }

    // --- Admin review ---

    public override async Task<GetPendingReviewItemsResponse> GetPendingReviewItems(GetPendingReviewItemsRequest request, ServerCallContext context)
    {
        var items = await cosmo.GetPendingReviewMarketplaceItemsAsync();
        var response = new GetPendingReviewItemsResponse();
        foreach (var item in items)
        {
            response.Items.Add(MapToProto(item));
        }
        return response;
    }

    public override async Task<ReviewMarketplaceItemResponse> ReviewMarketplaceItem(ReviewMarketplaceItemRequest request, ServerCallContext context)
    {
        var item = await cosmo.GetMarketplaceItemByIdAsync(request.MarketplaceItemId);
        if (item is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Marketplace item not found"));

        item.Status = request.Approved ? MarketplaceItemStatus.Approved : MarketplaceItemStatus.Rejected;
        item.ReviewedBy = request.ReviewerEmail;
        item.ReviewedAt = DateTime.UtcNow;

        if (!request.Approved)
            item.RejectionReason = request.RejectionReason;

        await cosmo.UpdateMarketplaceItemAsync(item);
        logger.LogInformation("Reviewed marketplace item {ItemId}: {Status}", item.Id, item.Status);

        return new ReviewMarketplaceItemResponse
        {
            Item = MapToProto(item)
        };
    }

    public override async Task<GetPendingProviderProfilesResponse> GetPendingProviderProfiles(GetPendingProviderProfilesRequest request, ServerCallContext context)
    {
        var profiles = await cosmo.GetPendingProviderProfilesAsync();
        var response = new GetPendingProviderProfilesResponse();
        foreach (var profile in profiles)
        {
            response.Profiles.Add(MapProviderToProto(profile));
        }
        return response;
    }

    public override async Task<ReviewProviderProfileResponse> ReviewProviderProfile(ReviewProviderProfileRequest request, ServerCallContext context)
    {
        var profile = await cosmo.GetProviderProfileAsync(request.AccountId);
        if (profile is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Provider profile not found"));

        profile.Status = request.Approved ? ProviderStatus.Approved : ProviderStatus.Suspended;
        await cosmo.UpdateProviderProfileAsync(profile);
        logger.LogInformation("Reviewed provider profile for account {AccountId}: {Status}", profile.AccountId, profile.Status);

        return new ReviewProviderProfileResponse
        {
            Profile = MapProviderToProto(profile)
        };
    }

    // --- Premium providers ---

    public override async Task<UpgradeToPremiumResponse> UpgradeToPremium(UpgradeToPremiumRequest request, ServerCallContext context)
    {
        var (success, error, profile) = await marketplaceService.UpgradeToPremiumAsync(request.AccountId);
        var response = new UpgradeToPremiumResponse
        {
            Success = success,
            Error = error ?? string.Empty
        };
        if (profile is not null)
            response.Profile = MapProviderToProto(profile);
        return response;
    }

    public override async Task<CancelPremiumResponse> CancelPremium(CancelPremiumRequest request, ServerCallContext context)
    {
        var (profile, creditsRefunded) = await marketplaceService.CancelPremiumAsync(request.AccountId);
        var response = new CancelPremiumResponse
        {
            Success = profile is not null,
            CreditsRefunded = creditsRefunded
        };
        if (profile is not null)
            response.Profile = MapProviderToProto(profile);
        return response;
    }

    public override async Task<GetPremiumProvidersResponse> GetPremiumProviders(GetPremiumProvidersRequest request, ServerCallContext context)
    {
        var providers = await cosmo.GetPremiumProvidersAsync();
        var response = new GetPremiumProvidersResponse();
        foreach (var provider in providers)
        {
            response.Providers.Add(MapProviderToProto(provider));
        }
        return response;
    }

    public override async Task<SetItemFeaturedResponse> SetItemFeatured(SetItemFeaturedRequest request, ServerCallContext context)
    {
        // Extract accountId from the item to identify the provider
        var item = await cosmo.GetMarketplaceItemByIdAsync(request.MarketplaceItemId);
        if (item is null)
            return new SetItemFeaturedResponse { Success = false, Error = "Item not found" };

        var (success, error, updatedItem) = await marketplaceService.SetItemFeaturedAsync(item.AccountId, request.MarketplaceItemId, request.IsFeatured);
        var response = new SetItemFeaturedResponse
        {
            Success = success,
            Error = error ?? string.Empty
        };
        if (updatedItem is not null)
            response.Item = MapToProto(updatedItem);
        return response;
    }

    public override async Task<GetMarketplaceSettingsResponse> GetMarketplaceSettings(GetMarketplaceSettingsRequest request, ServerCallContext context)
    {
        var settings = await cosmo.GetMarketplaceSettingsAsync();
        return new GetMarketplaceSettingsResponse
        {
            PremiumMonthlyCreditCost = settings.PremiumMonthlyCreditCost,
            MaxFeaturedItemsPerProvider = settings.MaxFeaturedItemsPerProvider
        };
    }

    public override async Task<UploadProviderLogoResponse> UploadProviderLogo(UploadProviderLogoRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.SvgContent))
            return new UploadProviderLogoResponse { Success = false, Error = "SVG content is required" };

        var svgContent = request.SvgContent.Trim();

        if (!svgContent.Contains("<svg", StringComparison.OrdinalIgnoreCase))
            return new UploadProviderLogoResponse { Success = false, Error = "Invalid SVG: content must contain an <svg> element" };

        if (svgContent.Length > 102400) // 100KB
            return new UploadProviderLogoResponse { Success = false, Error = "SVG content exceeds 100KB limit" };

        // Strip everything before the <svg tag (XML declarations, DOCTYPEs, etc.)
        var svgStart = svgContent.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        if (svgStart > 0)
            svgContent = svgContent[svgStart..];

        // Sanitize: strip script tags, event handlers, foreignObject, and external references
        svgContent = Regex.Replace(svgContent, @"<script[^>]*>[\s\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
        svgContent = Regex.Replace(svgContent, @"<foreignObject[^>]*>[\s\S]*?</foreignObject>", string.Empty, RegexOptions.IgnoreCase);
        svgContent = Regex.Replace(svgContent, @"\s+on\w+\s*=\s*""[^""]*""", string.Empty, RegexOptions.IgnoreCase);
        svgContent = Regex.Replace(svgContent, @"\s+on\w+\s*=\s*'[^']*'", string.Empty, RegexOptions.IgnoreCase);
        svgContent = Regex.Replace(svgContent, @"href\s*=\s*""javascript:[^""]*""", @"href=""""", RegexOptions.IgnoreCase);
        svgContent = Regex.Replace(svgContent, @"href\s*=\s*'javascript:[^']*'", @"href=''", RegexOptions.IgnoreCase);

        var profile = await cosmo.GetProviderProfileAsync(request.AccountId);
        if (profile is null)
            return new UploadProviderLogoResponse { Success = false, Error = "Provider profile not found" };

        profile.LogoSvg = svgContent;
        await cosmo.UpdateProviderProfileAsync(profile);

        return new UploadProviderLogoResponse
        {
            Success = true,
            Profile = MapProviderToProto(profile)
        };
    }

    // --- Admin provider management ---

    public override async Task<GetAllProvidersResponse> GetAllProviders(GetAllProvidersRequest request, ServerCallContext context)
    {
        var providers = await cosmo.GetAllProvidersAsync();
        var response = new GetAllProvidersResponse();
        foreach (var provider in providers)
        {
            response.Providers.Add(MapProviderToProto(provider));
        }
        return response;
    }

    public override async Task<AdminSetProviderStatusResponse> AdminSetProviderStatus(AdminSetProviderStatusRequest request, ServerCallContext context)
    {
        var profile = await cosmo.GetProviderProfileAsync(request.AccountId);
        if (profile is null)
            return new AdminSetProviderStatusResponse { Success = false, Error = "Provider not found" };

        if (!System.Enum.TryParse<ProviderStatus>(request.Status, out var status))
            return new AdminSetProviderStatusResponse { Success = false, Error = "Invalid status" };

        var previousStatus = profile.Status;
        profile.Status = status;

        // If suspended and was premium, cancel premium
        if (status == ProviderStatus.Suspended && profile.IsPremium)
        {
            profile.IsPremium = false;
            profile.PremiumExpiresAt = null;
            await cosmo.ClearFeaturedItemsByAccountAsync(profile.AccountId);
        }

        await cosmo.UpdateProviderProfileAsync(profile);
        logger.LogInformation("Admin set provider {AccountId} status from {OldStatus} to {NewStatus}", profile.AccountId, previousStatus, status);

        return new AdminSetProviderStatusResponse
        {
            Success = true,
            Profile = MapProviderToProto(profile)
        };
    }

    public override async Task<AdminSetProviderPremiumResponse> AdminSetProviderPremium(AdminSetProviderPremiumRequest request, ServerCallContext context)
    {
        var profile = await cosmo.GetProviderProfileAsync(request.AccountId);
        if (profile is null)
            return new AdminSetProviderPremiumResponse { Success = false, Error = "Provider not found" };

        if (profile.Status != ProviderStatus.Approved)
            return new AdminSetProviderPremiumResponse { Success = false, Error = "Provider must be Approved to change premium status" };

        if (request.IsPremium)
        {
            profile.IsPremium = true;
            profile.PremiumExpiresAt = DateTime.UtcNow.AddDays(30);
        }
        else
        {
            profile.IsPremium = false;
            profile.PremiumExpiresAt = null;
            await cosmo.ClearFeaturedItemsByAccountAsync(profile.AccountId);
        }

        await cosmo.UpdateProviderProfileAsync(profile);
        logger.LogInformation("Admin set provider {AccountId} premium to {IsPremium}", profile.AccountId, request.IsPremium);

        return new AdminSetProviderPremiumResponse
        {
            Success = true,
            Profile = MapProviderToProto(profile)
        };
    }

    public override async Task<AdminSetRevenueShareResponse> AdminSetRevenueShare(AdminSetRevenueShareRequest request, ServerCallContext context)
    {
        var profile = await cosmo.GetProviderProfileAsync(request.AccountId);
        if (profile is null)
            return new AdminSetRevenueShareResponse { Success = false };

        profile.RevenueSharePercent = Math.Clamp(request.RevenueSharePercent, 0, 100);
        await cosmo.UpdateProviderProfileAsync(profile);
        logger.LogInformation("Admin set provider {AccountId} revenue share to {Percent}%", profile.AccountId, profile.RevenueSharePercent);

        return new AdminSetRevenueShareResponse
        {
            Success = true,
            Profile = MapProviderToProto(profile)
        };
    }

    public override async Task<AdminUpdateMarketplaceSettingsResponse> AdminUpdateMarketplaceSettings(AdminUpdateMarketplaceSettingsRequest request, ServerCallContext context)
    {
        var settings = await cosmo.GetMarketplaceSettingsAsync();
        settings.PremiumMonthlyCreditCost = request.PremiumMonthlyCreditCost;
        settings.MaxFeaturedItemsPerProvider = request.MaxFeaturedItemsPerProvider;
        await cosmo.UpdateMarketplaceSettingsAsync(settings);
        logger.LogInformation("Admin updated marketplace settings: PremiumCost={Cost}, MaxFeatured={Max}", settings.PremiumMonthlyCreditCost, settings.MaxFeaturedItemsPerProvider);

        return new AdminUpdateMarketplaceSettingsResponse { Success = true };
    }

    public override async Task<GetProviderItemsResponse> GetProviderItems(GetProviderItemsRequest request, ServerCallContext context)
    {
        var items = await cosmo.GetMarketplaceItemsByAccountAsync(request.AccountId);
        var response = new GetProviderItemsResponse();
        foreach (var item in items)
        {
            response.Items.Add(MapToProto(item));
        }
        return response;
    }

    public override async Task<AdminResetProviderPayoutResponse> AdminResetProviderPayout(AdminResetProviderPayoutRequest request, ServerCallContext context)
    {
        var profile = await cosmo.GetProviderProfileAsync(request.AccountId);
        if (profile is null)
            return new AdminResetProviderPayoutResponse { Success = false };

        profile.PendingPayout = 0;
        await cosmo.UpdateProviderProfileAsync(profile);
        logger.LogInformation("Admin reset pending payout for provider {AccountId}", profile.AccountId);

        return new AdminResetProviderPayoutResponse
        {
            Success = true,
            Profile = MapProviderToProto(profile)
        };
    }

    // --- Email notifications ---

    private async Task NotifyAdminAsync(string subject, string body)
    {
        try
        {
            await EmailSender.Instance.Value.SendEmailAsync("info@daisi.ai", subject, body);
            logger.LogInformation("Admin notification sent: {Subject}", subject);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send admin notification: {Subject}", subject);
        }
    }

    // --- Mapping helpers ---

    private static MarketplaceItemInfo MapToProto(MarketplaceItem item)
    {
        var info = new MarketplaceItemInfo
        {
            Id = item.Id ?? string.Empty,
            AccountId = item.AccountId ?? string.Empty,
            ProviderId = item.ProviderId ?? string.Empty,
            Name = item.Name ?? string.Empty,
            Description = item.Description ?? string.Empty,
            ShortDescription = item.ShortDescription ?? string.Empty,
            Author = item.Author ?? string.Empty,
            Version = item.Version ?? string.Empty,
            IconUrl = item.IconUrl ?? string.Empty,
            ItemType = item.ItemType,
            SkillId = item.SkillId ?? string.Empty,
            ToolClassName = item.ToolClassName ?? string.Empty,
            PackageBlobUrl = item.PackageBlobUrl ?? string.Empty,
            PricingModel = item.PricingModel,
            CreditPrice = item.CreditPrice,
            SubscriptionCreditPrice = item.SubscriptionCreditPrice,
            SubscriptionPeriodDays = item.SubscriptionPeriodDays,
            Status = item.Status.ToString(),
            Visibility = item.Visibility ?? string.Empty,
            ReviewedBy = item.ReviewedBy ?? string.Empty,
            RejectionReason = item.RejectionReason ?? string.Empty,
            DownloadCount = item.DownloadCount,
            PurchaseCount = item.PurchaseCount,
            AverageRating = item.AverageRating,
            RatingCount = item.RatingCount,
            IsFeatured = item.IsFeatured,
            IsSecureExecution = item.IsSecureExecution,
            SecureEndpointUrl = item.SecureEndpointUrl ?? string.Empty,
            SecureAuthKey = item.SecureAuthKey ?? string.Empty,
            ExecutionCreditCost = item.ExecutionCreditCost,
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc)),
            UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(item.UpdatedAt, DateTimeKind.Utc))
        };

        if (item.ReviewedAt.HasValue)
            info.ReviewedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(item.ReviewedAt.Value, DateTimeKind.Utc));

        info.Tags.AddRange(item.Tags ?? []);
        info.Screenshots.AddRange(item.Screenshots ?? []);
        info.BundledItemIds.AddRange(item.BundledItemIds ?? []);

        // Map setup parameters
        if (item.SetupParameters?.Count > 0)
        {
            foreach (var sp in item.SetupParameters)
            {
                info.SetupParameters.Add(new SecureToolSetupParameter
                {
                    Name = sp.Name,
                    Description = sp.Description,
                    Type = sp.Type,
                    IsRequired = sp.IsRequired,
                    AuthUrl = sp.AuthUrl,
                    ServiceLabel = sp.ServiceLabel
                });
            }
        }

        // Map secure tool definition
        if (item.SecureToolDefinition is not null)
        {
            var def = new SecureToolDefinitionInfo
            {
                MarketplaceItemId = item.Id ?? string.Empty,
                ToolId = item.SecureToolDefinition.ToolId,
                Name = item.SecureToolDefinition.Name,
                UseInstructions = item.SecureToolDefinition.UseInstructions,
                ToolGroup = item.SecureToolDefinition.ToolGroup
            };
            foreach (var p in item.SecureToolDefinition.Parameters)
            {
                def.Parameters.Add(new SecureToolParameterInfo
                {
                    Name = p.Name,
                    Description = p.Description,
                    IsRequired = p.IsRequired
                });
            }
            info.SecureToolDefinition = def;
        }

        return info;
    }

    private static MarketplaceItem MapFromProto(MarketplaceItemInfo info)
    {
        var item = new MarketplaceItem
        {
            AccountId = info.AccountId,
            ProviderId = info.ProviderId,
            Name = info.Name,
            Description = info.Description,
            ShortDescription = info.ShortDescription,
            Author = info.Author,
            Version = info.Version,
            IconUrl = info.IconUrl,
            Tags = info.Tags.ToList(),
            Screenshots = info.Screenshots.ToList(),
            ItemType = info.ItemType,
            SkillId = info.SkillId,
            ToolClassName = info.ToolClassName,
            BundledItemIds = info.BundledItemIds.ToList(),
            PricingModel = info.PricingModel,
            CreditPrice = info.CreditPrice,
            SubscriptionCreditPrice = info.SubscriptionCreditPrice,
            SubscriptionPeriodDays = info.SubscriptionPeriodDays,
            Visibility = info.Visibility,
            IsSecureExecution = info.IsSecureExecution,
            SecureEndpointUrl = info.SecureEndpointUrl,
            SecureAuthKey = info.SecureAuthKey,
            ExecutionCreditCost = info.ExecutionCreditCost
        };

        if (info.SetupParameters.Count > 0)
        {
            item.SetupParameters = info.SetupParameters.Select(sp => new SetupParameterData
            {
                Name = sp.Name,
                Description = sp.Description,
                Type = sp.Type,
                IsRequired = sp.IsRequired,
                AuthUrl = sp.AuthUrl,
                ServiceLabel = sp.ServiceLabel
            }).ToList();
        }

        if (info.SecureToolDefinition is not null)
        {
            item.SecureToolDefinition = new SecureToolDefinitionData
            {
                ToolId = info.SecureToolDefinition.ToolId,
                Name = info.SecureToolDefinition.Name,
                UseInstructions = info.SecureToolDefinition.UseInstructions,
                ToolGroup = info.SecureToolDefinition.ToolGroup,
                Parameters = info.SecureToolDefinition.Parameters.Select(p => new SecureToolParameterData
                {
                    Name = p.Name,
                    Description = p.Description,
                    IsRequired = p.IsRequired
                }).ToList()
            };
        }

        return item;
    }

    private static MarketplacePurchaseInfo MapPurchaseToProto(MarketplacePurchase purchase)
    {
        var info = new MarketplacePurchaseInfo
        {
            Id = purchase.Id ?? string.Empty,
            AccountId = purchase.AccountId ?? string.Empty,
            MarketplaceItemId = purchase.MarketplaceItemId ?? string.Empty,
            MarketplaceItemName = purchase.MarketplaceItemName ?? string.Empty,
            ItemType = purchase.ItemType,
            CreditsPaid = purchase.CreditsPaid,
            TransactionId = purchase.TransactionId ?? string.Empty,
            IsSubscription = purchase.IsSubscription,
            IsActive = purchase.IsActive,
            PurchasedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(purchase.PurchasedAt, DateTimeKind.Utc)),
            SecureInstallId = purchase.SecureInstallId ?? string.Empty,
            BundleInstallId = purchase.BundleInstallId ?? string.Empty
        };

        if (purchase.SubscriptionExpiresAt.HasValue)
            info.SubscriptionExpiresAt = Timestamp.FromDateTime(DateTime.SpecifyKind(purchase.SubscriptionExpiresAt.Value, DateTimeKind.Utc));

        return info;
    }

    private static ProviderProfileInfo MapProviderToProto(ProviderProfile profile)
    {
        var info = new ProviderProfileInfo
        {
            Id = profile.Id ?? string.Empty,
            AccountId = profile.AccountId ?? string.Empty,
            DisplayName = profile.DisplayName ?? string.Empty,
            Bio = profile.Bio ?? string.Empty,
            AvatarUrl = profile.AvatarUrl ?? string.Empty,
            WebsiteUrl = profile.WebsiteUrl ?? string.Empty,
            Status = profile.Status.ToString(),
            RevenueSharePercent = profile.RevenueSharePercent,
            TotalEarnings = profile.TotalEarnings,
            PendingPayout = profile.PendingPayout,
            ItemCount = profile.ItemCount,
            IsPremium = profile.IsPremium,
            LogoSvg = profile.LogoSvg ?? string.Empty,
            ProfileMarkdown = profile.ProfileMarkdown ?? string.Empty,
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(profile.CreatedAt, DateTimeKind.Utc)),
            UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(profile.UpdatedAt, DateTimeKind.Utc))
        };

        if (profile.PremiumExpiresAt.HasValue)
            info.PremiumExpiresAt = Timestamp.FromDateTime(DateTime.SpecifyKind(profile.PremiumExpiresAt.Value, DateTimeKind.Utc));

        return info;
    }

}
