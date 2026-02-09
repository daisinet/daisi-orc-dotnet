using Daisi.Orc.Core.Data.Db;
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
