using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    [Authorize]
    public class CreditsRPC(
        ILogger<CreditsRPC> logger,
        CreditService creditService,
        Cosmo cosmo) : CreditsProto.CreditsProtoBase
    {
        public override async Task<GetCreditAccountResponse> GetCreditAccount(
            GetCreditAccountRequest request, ServerCallContext context)
        {
            var accountId = ResolveAccountId(request.AccountId, context);
            var account = await creditService.GetBalanceAsync(accountId);

            return new GetCreditAccountResponse
            {
                Account = MapToProto(account)
            };
        }

        public override async Task<GetCreditTransactionsResponse> GetCreditTransactions(
            GetCreditTransactionsRequest request, ServerCallContext context)
        {
            var accountId = ResolveAccountId(request.AccountId, context);
            var pageSize = request.PageSize > 0 ? request.PageSize : 20;
            var pageIndex = request.PageIndex >= 0 ? request.PageIndex : 0;

            var result = await cosmo.GetCreditTransactionsAsync(accountId, pageSize, pageIndex);

            var response = new GetCreditTransactionsResponse
            {
                TotalCount = result.TotalCount
            };

            foreach (var tx in result.Items)
            {
                response.Transactions.Add(new CreditTransactionInfo
                {
                    Id = tx.Id,
                    AccountId = tx.AccountId,
                    Type = tx.Type.ToString(),
                    Amount = tx.Amount,
                    Balance = tx.Balance,
                    Description = tx.Description ?? string.Empty,
                    RelatedEntityId = tx.RelatedEntityId ?? string.Empty,
                    Multiplier = tx.Multiplier,
                    DateCreated = Timestamp.FromDateTime(DateTime.SpecifyKind(tx.DateCreated, DateTimeKind.Utc))
                });
            }

            return response;
        }

        public override async Task<SetMultipliersResponse> SetMultipliers(
            SetMultipliersRequest request, ServerCallContext context)
        {
            var account = await creditService.SetMultipliersAsync(
                request.AccountId,
                request.HasTokenEarnMultiplier ? request.TokenEarnMultiplier : null,
                request.HasUptimeEarnMultiplier ? request.UptimeEarnMultiplier : null);

            logger.LogInformation($"Multipliers updated for account {request.AccountId}");

            return new SetMultipliersResponse
            {
                Account = MapToProto(account)
            };
        }

        public override async Task<PurchaseCreditsResponse> PurchaseCredits(
            PurchaseCreditsRequest request, ServerCallContext context)
        {
            await creditService.PurchaseCreditsAsync(
                request.AccountId, request.Amount, request.Description);

            var account = await creditService.GetBalanceAsync(request.AccountId);

            logger.LogInformation($"Purchased {request.Amount} credits for account {request.AccountId}");

            return new PurchaseCreditsResponse
            {
                Account = MapToProto(account)
            };
        }

        public override async Task<AdjustCreditsResponse> AdjustCredits(
            AdjustCreditsRequest request, ServerCallContext context)
        {
            await creditService.AdjustCreditsAsync(
                request.AccountId, request.Amount, request.Description);

            var account = await creditService.GetBalanceAsync(request.AccountId);

            logger.LogInformation($"Adjusted {request.Amount} credits for account {request.AccountId}");

            return new AdjustCreditsResponse
            {
                Account = MapToProto(account)
            };
        }

        public override async Task<GetCreditAnomaliesResponse> GetCreditAnomalies(
            GetCreditAnomaliesRequest request, ServerCallContext context)
        {
            await EnsureAdminAsync(context);

            var pageSize = request.PageSize > 0 ? request.PageSize : 20;
            var pageIndex = request.PageIndex >= 0 ? request.PageIndex : 0;

            var result = await cosmo.GetCreditAnomaliesAsync(
                string.IsNullOrWhiteSpace(request.AccountId) ? null : request.AccountId,
                request.HasType ? MapAnomalyType(request.Type) : null,
                request.HasStatus ? MapAnomalyStatus(request.Status) : null,
                pageSize,
                pageIndex);

            var response = new GetCreditAnomaliesResponse
            {
                TotalCount = result.TotalCount
            };

            foreach (var anomaly in result.Items)
            {
                response.Anomalies.Add(MapAnomalyToProto(anomaly));
            }

            return response;
        }

        public override async Task<ReviewCreditAnomalyResponse> ReviewCreditAnomaly(
            ReviewCreditAnomalyRequest request, ServerCallContext context)
        {
            await EnsureAdminAsync(context);

            var reviewerUserId = context.GetUserId();
            var updated = await cosmo.UpdateCreditAnomalyStatusAsync(
                request.AnomalyId,
                request.AccountId,
                MapAnomalyStatus(request.NewStatus),
                reviewerUserId);

            logger.LogInformation(
                $"Anomaly {request.AnomalyId} reviewed by {reviewerUserId}: {request.NewStatus}");

            return new ReviewCreditAnomalyResponse
            {
                Anomaly = MapAnomalyToProto(updated)
            };
        }

        private async Task EnsureAdminAsync(ServerCallContext context)
        {
            var userId = context.GetUserId();
            var accountId = context.GetAccountId();
            if (userId is null || accountId is null)
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Authentication required."));

            var user = await cosmo.GetUserAsync(userId, accountId);
            if (user.Role < UserRoles.Admin)
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Admin access required."));
        }

        private static CreditAnomalyInfo MapAnomalyToProto(Orc.Core.Data.Models.CreditAnomaly anomaly)
        {
            var info = new CreditAnomalyInfo
            {
                Id = anomaly.Id,
                AccountId = anomaly.AccountId,
                HostId = anomaly.HostId ?? string.Empty,
                Type = (Protos.V1.AnomalyType)anomaly.Type,
                Severity = (Protos.V1.AnomalySeverity)anomaly.Severity,
                Description = anomaly.Description ?? string.Empty,
                Details = anomaly.Details ?? string.Empty,
                Status = (Protos.V1.AnomalyStatus)anomaly.Status,
                DateCreated = Timestamp.FromDateTime(DateTime.SpecifyKind(anomaly.DateCreated, DateTimeKind.Utc)),
                ReviewedBy = anomaly.ReviewedBy ?? string.Empty
            };

            if (anomaly.DateReviewed.HasValue)
                info.DateReviewed = Timestamp.FromDateTime(
                    DateTime.SpecifyKind(anomaly.DateReviewed.Value, DateTimeKind.Utc));

            return info;
        }

        private static Orc.Core.Data.Models.AnomalyType MapAnomalyType(Protos.V1.AnomalyType type) => type switch
        {
            Protos.V1.AnomalyType.ReceiptReplay => Orc.Core.Data.Models.AnomalyType.ReceiptReplay,
            Protos.V1.AnomalyType.InflatedTokens => Orc.Core.Data.Models.AnomalyType.InflatedTokens,
            Protos.V1.AnomalyType.ReceiptVolumeSpike => Orc.Core.Data.Models.AnomalyType.ReceiptVolumeSpike,
            Protos.V1.AnomalyType.ZeroWorkUptime => Orc.Core.Data.Models.AnomalyType.ZeroWorkUptime,
            Protos.V1.AnomalyType.CircularCreditFlow => Orc.Core.Data.Models.AnomalyType.CircularCreditFlow,
            _ => Orc.Core.Data.Models.AnomalyType.ReceiptReplay
        };

        private static Orc.Core.Data.Models.AnomalyStatus MapAnomalyStatus(Protos.V1.AnomalyStatus status) => status switch
        {
            Protos.V1.AnomalyStatus.Open => Orc.Core.Data.Models.AnomalyStatus.Open,
            Protos.V1.AnomalyStatus.Reviewed => Orc.Core.Data.Models.AnomalyStatus.Reviewed,
            Protos.V1.AnomalyStatus.Dismissed => Orc.Core.Data.Models.AnomalyStatus.Dismissed,
            Protos.V1.AnomalyStatus.ActionTaken => Orc.Core.Data.Models.AnomalyStatus.ActionTaken,
            _ => Orc.Core.Data.Models.AnomalyStatus.Open
        };

        private static string ResolveAccountId(string requestAccountId, ServerCallContext context)
        {
            if (!string.IsNullOrWhiteSpace(requestAccountId))
                return requestAccountId;

            return context.GetAccountId() ?? throw new RpcException(
                new Status(StatusCode.InvalidArgument, "AccountId is required."));
        }

        private static CreditAccountInfo MapToProto(Orc.Core.Data.Models.CreditAccount account)
        {
            return new CreditAccountInfo
            {
                AccountId = account.AccountId,
                Balance = account.Balance,
                TotalEarned = account.TotalEarned,
                TotalSpent = account.TotalSpent,
                TotalPurchased = account.TotalPurchased,
                TokenEarnMultiplier = account.TokenEarnMultiplier,
                UptimeEarnMultiplier = account.UptimeEarnMultiplier,
                DateCreated = Timestamp.FromDateTime(DateTime.SpecifyKind(account.DateCreated, DateTimeKind.Utc)),
                DateLastUpdated = Timestamp.FromDateTime(DateTime.SpecifyKind(account.DateLastUpdated, DateTimeKind.Utc))
            };
        }
    }
}
