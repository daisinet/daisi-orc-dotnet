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
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc)),
            UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(item.UpdatedAt, DateTimeKind.Utc))
        };

        if (item.ReviewedAt.HasValue)
            info.ReviewedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(item.ReviewedAt.Value, DateTimeKind.Utc));

        info.Tags.AddRange(item.Tags ?? []);
        info.Screenshots.AddRange(item.Screenshots ?? []);
        info.BundledItemIds.AddRange(item.BundledItemIds ?? []);

        return info;
    }

    private static MarketplaceItem MapFromProto(MarketplaceItemInfo info)
    {
        return new MarketplaceItem
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
            Visibility = info.Visibility
        };
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
            PurchasedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(purchase.PurchasedAt, DateTimeKind.Utc))
        };

        if (purchase.SubscriptionExpiresAt.HasValue)
            info.SubscriptionExpiresAt = Timestamp.FromDateTime(DateTime.SpecifyKind(purchase.SubscriptionExpiresAt.Value, DateTimeKind.Utc));

        return info;
    }

    private static ProviderProfileInfo MapProviderToProto(ProviderProfile profile)
    {
        return new ProviderProfileInfo
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
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(profile.CreatedAt, DateTimeKind.Utc)),
            UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(profile.UpdatedAt, DateTimeKind.Utc))
        };
    }

}
