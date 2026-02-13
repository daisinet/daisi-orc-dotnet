using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Components.Infrastructure;
using Microsoft.Azure.Cosmos.Linq;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    public class AccountsRPC(ILogger<AccountsRPC> logger, Cosmo cosmo, CreditService creditService) : AccountsProto.AccountsProtoBase
    {
        #region Accounts
        public async override Task<GetAccountResponse> Get(GetAccountRequest request, ServerCallContext context)
        {
            var accountId = context.GetAccountId();
            if (accountId is null) throw new RpcException(RPCStatuses.InvalidAccount, "DAISI: Invalid Account Authentication");

            var account = await cosmo.GetAccountAsync(accountId);

            return new GetAccountResponse()
            { 
                Account = new()
                {
                    Id = account.Id,
                    Name = account.Name,
                }
            };
        }
        public async override Task<UpdateAccountResponse> Update(UpdateAccountRequest request, ServerCallContext context)
        {
            try
            {
                var accountId = context.GetAccountId();
                if (accountId is null) throw new RpcException(RPCStatuses.InvalidAccount, "DAISI: Invalid Account Authentication");
                var account = await cosmo.GetAccountAsync(accountId);

                account.Name = request.Account.Name;

                if (!string.IsNullOrWhiteSpace(request.Account.TaxId))
                    account.TaxId = request.Account.TaxId;

                await cosmo.PatchAccountForWebUpdateAsync(account);
                return new UpdateAccountResponse()
                {
                    Success = true
                };
            }
            catch (Exception ex)
            {
                return new UpdateAccountResponse() { Success = false, Message = ex.Message };
            }

        }

        #endregion

        #region Admin

        /// <summary>
        /// Verifies the calling user has Admin role. Throws PermissionDenied if not.
        /// </summary>
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

        /// <summary>
        /// Lists all accounts with paging and search support.
        /// </summary>
        public async override Task<AdminGetAccountsResponse> AdminGetAccounts(AdminGetAccountsRequest request, ServerCallContext context)
        {
            await EnsureAdminAsync(context);

            var result = await cosmo.GetAllAccountsAsync(request.Paging);

            var response = new AdminGetAccountsResponse
            {
                TotalCount = result.TotalCount
            };

            response.Accounts.AddRange(result.Items.Select(a => new AdminAccountSummary
            {
                Id = a.Id,
                Name = a.Name ?? string.Empty
            }));

            return response;
        }

        /// <summary>
        /// Gets a single account with resource counts and credit info.
        /// </summary>
        public async override Task<AdminGetAccountResponse> AdminGetAccount(AdminGetAccountRequest request, ServerCallContext context)
        {
            await EnsureAdminAsync(context);

            var accountTask = cosmo.GetAccountAsync(request.AccountId);
            var userCountTask = cosmo.GetAccountUserCountAsync(request.AccountId);
            var hostCountTask = cosmo.GetHostCountAsync(request.AccountId);
            var appCountTask = cosmo.GetDappCountAsync(request.AccountId);
            var creditAccountTask = cosmo.GetCreditAccountAsync(request.AccountId);

            await Task.WhenAll(accountTask, userCountTask, hostCountTask, appCountTask, creditAccountTask);

            var account = await accountTask;
            var creditAccount = await creditAccountTask;

            var response = new AdminGetAccountResponse
            {
                Account = new Protos.V1.Account
                {
                    Id = account.Id,
                    Name = account.Name ?? string.Empty
                },
                UserCount = await userCountTask,
                HostCount = await hostCountTask,
                AppCount = await appCountTask
            };

            if (creditAccount is not null)
            {
                response.CreditInfo = new CreditAccountInfo
                {
                    AccountId = creditAccount.AccountId,
                    Balance = creditAccount.Balance,
                    TotalEarned = creditAccount.TotalEarned,
                    TotalSpent = creditAccount.TotalSpent,
                    TotalPurchased = creditAccount.TotalPurchased,
                    TokenEarnMultiplier = creditAccount.TokenEarnMultiplier,
                    UptimeEarnMultiplier = creditAccount.UptimeEarnMultiplier,
                    DateCreated = Timestamp.FromDateTime(DateTime.SpecifyKind(creditAccount.DateCreated, DateTimeKind.Utc)),
                    DateLastUpdated = Timestamp.FromDateTime(DateTime.SpecifyKind(creditAccount.DateLastUpdated, DateTimeKind.Utc))
                };
            }

            return response;
        }

        /// <summary>
        /// Updates an account's name by ID. Admin only.
        /// </summary>
        public async override Task<UpdateAccountResponse> AdminUpdateAccount(AdminUpdateAccountRequest request, ServerCallContext context)
        {
            try
            {
                await EnsureAdminAsync(context);

                var account = await cosmo.GetAccountAsync(request.AccountId);
                account.Name = request.Name;
                await cosmo.PatchAccountForWebUpdateAsync(account);

                return new UpdateAccountResponse { Success = true };
            }
            catch (Exception ex)
            {
                return new UpdateAccountResponse { Success = false, Message = ex.Message };
            }
        }

        /// <summary>
        /// Audits a credit account balance by recalculating from transaction history.
        /// If a mismatch is found, corrects the stored balance.
        /// </summary>
        public async override Task<AdminAuditCreditBalanceResponse> AdminAuditCreditBalance(AdminAuditCreditBalanceRequest request, ServerCallContext context)
        {
            await EnsureAdminAsync(context);

            var creditAccount = await cosmo.GetOrCreateCreditAccountAsync(request.AccountId);
            var transactions = await cosmo.GetAllCreditTransactionsAsync(request.AccountId);

            long calculatedBalance = 0;
            long calculatedTotalEarned = 0;
            long calculatedTotalSpent = 0;
            long calculatedTotalPurchased = 0;

            foreach (var tx in transactions)
            {
                calculatedBalance += tx.Amount;

                switch (tx.Type)
                {
                    case CreditTransactionType.TokenEarning:
                    case CreditTransactionType.UptimeEarning:
                    case CreditTransactionType.ProviderEarning:
                        calculatedTotalEarned += tx.Amount;
                        break;
                    case CreditTransactionType.InferenceSpend:
                    case CreditTransactionType.MarketplacePurchase:
                    case CreditTransactionType.SubscriptionRenewal:
                        calculatedTotalSpent += Math.Abs(tx.Amount);
                        break;
                    case CreditTransactionType.Purchase:
                        calculatedTotalPurchased += tx.Amount;
                        break;
                    case CreditTransactionType.AdminAdjustment:
                        if (tx.Amount > 0)
                            calculatedTotalEarned += tx.Amount;
                        else
                            calculatedTotalSpent += Math.Abs(tx.Amount);
                        break;
                }
            }

            bool balanceMatches = creditAccount.Balance == calculatedBalance
                && creditAccount.TotalEarned == calculatedTotalEarned
                && creditAccount.TotalSpent == calculatedTotalSpent
                && creditAccount.TotalPurchased == calculatedTotalPurchased;

            bool wasCorrected = false;
            string? message = null;

            if (!balanceMatches)
            {
                creditAccount.Balance = calculatedBalance;
                creditAccount.TotalEarned = calculatedTotalEarned;
                creditAccount.TotalSpent = calculatedTotalSpent;
                creditAccount.TotalPurchased = calculatedTotalPurchased;
                await cosmo.UpdateCreditAccountBalanceAsync(creditAccount);
                wasCorrected = true;
                message = $"Balance corrected. Previous: {creditAccount.Balance}, Recalculated: {calculatedBalance} from {transactions.Count} transactions.";
                logger.LogWarning("Credit audit corrected balance for account {AccountId}: stored={StoredBalance}, calculated={CalculatedBalance}",
                    request.AccountId, creditAccount.Balance, calculatedBalance);
            }

            var response = new AdminAuditCreditBalanceResponse
            {
                AccountId = request.AccountId,
                StoredBalance = creditAccount.Balance,
                CalculatedBalance = calculatedBalance,
                StoredTotalEarned = creditAccount.TotalEarned,
                CalculatedTotalEarned = calculatedTotalEarned,
                StoredTotalSpent = creditAccount.TotalSpent,
                CalculatedTotalSpent = calculatedTotalSpent,
                StoredTotalPurchased = creditAccount.TotalPurchased,
                CalculatedTotalPurchased = calculatedTotalPurchased,
                TransactionCount = transactions.Count,
                BalanceMatches = balanceMatches,
                WasCorrected = wasCorrected
            };

            if (message is not null)
                response.Message = message;

            return response;
        }

        /// <summary>
        /// Sets storage limits on an account. Admin only.
        /// </summary>
        public async override Task<UpdateAccountResponse> AdminSetStorageLimits(AdminSetStorageLimitsRequest request, ServerCallContext context)
        {
            try
            {
                await EnsureAdminAsync(context);

                var limits = new
                {
                    MaxFileSizeBytes = request.Limits.MaxFileSizeBytes,
                    MaxTotalStorageBytes = request.Limits.MaxTotalStorageBytes,
                    MaxFileCount = request.Limits.MaxFileCount
                };

                await cosmo.PatchAccountStorageLimitsAsync(request.AccountId, limits);

                return new UpdateAccountResponse { Success = true };
            }
            catch (Exception ex)
            {
                return new UpdateAccountResponse { Success = false, Message = ex.Message };
            }
        }

        /// <summary>
        /// Lists users for a specific account. Admin only.
        /// </summary>
        public async override Task<AdminGetAccountUsersResponse> AdminGetAccountUsers(AdminGetAccountUsersRequest request, ServerCallContext context)
        {
            await EnsureAdminAsync(context);

            var result = await cosmo.GetUsersAsync(request.AccountId, request.Paging);

            var response = new AdminGetAccountUsersResponse { TotalCount = result.TotalCount };
            response.Users.AddRange(result.Items.Select(u => u.ConvertToRpc()));
            return response;
        }

        /// <summary>
        /// Lists hosts for a specific account. Admin only.
        /// </summary>
        public async override Task<AdminGetAccountHostsResponse> AdminGetAccountHosts(AdminGetAccountHostsRequest request, ServerCallContext context)
        {
            await EnsureAdminAsync(context);

            var result = await cosmo.GetHostsAsync(request.AccountId, request.Paging.SearchTerm, request.Paging.PageSize, request.Paging.PageIndex);

            var response = new AdminGetAccountHostsResponse { TotalCount = result.TotalCount };
            response.Hosts.AddRange(result.Items.Select(host => new Protos.V1.Host
            {
                Id = host.Id,
                Name = host.Name,
                IpAddress = host.IpAddress,
                Port = host.Port,
                DateStarted = host.DateStarted.HasValue ? Timestamp.FromDateTime(host.DateStarted.Value) : null,
                DateLastHeartbeat = host.DateLastHeartbeat.HasValue ? Timestamp.FromDateTime(host.DateLastHeartbeat.Value) : null,
                DateStopped = host.DateStopped.HasValue ? Timestamp.FromDateTime(host.DateStopped.Value) : null,
                OperatingSystem = host.OperatingSystem,
                OperatingSystemVersion = host.OperatingSystemVersion,
                AppVersion = host.AppVersion,
                Region = host.Region.ToString(),
                DirectConnect = host.DirectConnect,
                PeerConnect = host.PeerConnect,
                Status = host.Status,
                ReleaseGroup = host.ReleaseGroup,
            }));
            return response;
        }

        /// <summary>
        /// Lists apps for a specific account. Admin only.
        /// </summary>
        public async override Task<AdminGetAccountAppsResponse> AdminGetAccountApps(AdminGetAccountAppsRequest request, ServerCallContext context)
        {
            await EnsureAdminAsync(context);

            var result = await cosmo.GetDappsAsync(request.AccountId, request.Paging);

            var response = new AdminGetAccountAppsResponse { TotalCount = result.TotalCount };
            response.Apps.AddRange(result.Items.Select(d => d.ConvertToRpc()));
            return response;
        }

        #endregion

        #region Users
        public async override Task<ArchiveUserResponse> ArchiveUser(ArchiveUserRequest request, ServerCallContext context)
        {
            ArchiveUserResponse response = new ArchiveUserResponse();

            try
            {
                var accountId = context.GetAccountId();
                var user = await cosmo.GetUserAsync(request.UserId, accountId);

                if(user is null)
                    return new ArchiveUserResponse() { Success = false, Message = "DAISI: Wrong Account ID or Invalid User ID" };

                user.Status = UserStatus.Archived;
                await cosmo.UpdateUserAsync(user);

                response.Success = true;
            }
            catch(Exception exc)
            {
                response.Success = false;
                response.Message = exc.Message;
            }

            return response;
        }
        public async override Task<CreateUserResponse> CreateUser(CreateUserRequest request, ServerCallContext context)
        {
            CreateUserResponse response = new CreateUserResponse();

            try
            {
                var exists = await cosmo.UserWithEmailExistsAsync(request.User.EmailAddress);

                if (exists)
                    return new() { Success = false, Message = "DAISI: User with that email already exists. Be sure they weren't archived previously." };
                
                var accountId = context.GetAccountId();

                request.AccountName = request.AccountName.Trim();
                

                if(!string.IsNullOrWhiteSpace(request.AccountName))
                {
                    var account = await cosmo.CreateAccountAsync(new Core.Data.Models.Account()
                    {
                        Name = request.AccountName
                    });
                    accountId = account.Id;                    
                }

                request.User.Name = request.User.Name.Trim();
                request.User.Phone = request.User.Phone.Trim();
                request.User.EmailAddress = request.User.EmailAddress.Trim();

                request.User.AllowedToLogin = request.User.AllowEmail = request.User.AllowSMS = true;

                var dbUser = request.User.ConvertToDb(accountId);                

                var user = await cosmo.CreateUserAsync(dbUser);
                if (user == null)
                    return new() { Success = false, Message = "DAISI: An error occurred while creating the user." };

                response.User = user.ConvertToRpc();
                response.Success = true;
            }
            catch(Exception exc)
            {
                response.Success = false;
                response.Message = exc.Message;
            }

            return response;
        }
        public async override Task<GetUserResponse> GetUser(GetUserRequest request, ServerCallContext context)
        {
            GetUserResponse response = new GetUserResponse();

            try
            {
                var accountId = context.GetAccountId();
                var user = await cosmo.GetUserAsync(request.UserId, accountId);

                if (user is null)
                {
                    return new GetUserResponse() { Success = false, Message = "DAISI: User not found" };
                }

                response.User = user.ConvertToRpc();

            }
            catch (Exception exc)
            {
                response.Success = false;
                response.Message = $"DAISI: {exc.Message}";
            }

            return response;
        }
        public async override Task<GetUsersResponse> GetUsers(GetUsersRequest request, ServerCallContext context)
        {
            GetUsersResponse response = new GetUsersResponse();

            var accountId = context.GetAccountId();

            var users = await cosmo.GetUsersAsync(accountId, request.Paging);

            response.TotalCount = users.TotalCount;
            response.Users.AddRange(users.Items.Select(u => u.ConvertToRpc()));

            return response;
        } 
        public async override Task<UpdateUserResponse> UpdateUser(UpdateUserRequest request, ServerCallContext context)
        {
            UpdateUserResponse response = new UpdateUserResponse();

            try
            {
                var accountId = context.GetAccountId();
                var user = await cosmo.GetUserAsync(request.User.Id, accountId);

                if (user is null) return new UpdateUserResponse() { Success = false, Message = "DAISI: User not found." };

                user.Status = request.User.Status;
                user.Role = request.User.Role;
                user.Name = request.User.Name;
                user.Phone = request.User.Phone;
                user.Email = request.User.EmailAddress;
                user.AllowSMS = request.User.AllowSMS;
                user.AllowEmail = request.User.AllowEmail;
                user.AllowedToLogin = request.User.AllowedToLogin;                

                await cosmo.PatchUserForWebUpdateAsync(user);

                response.Success = true;
            }
            catch(Exception exc)
            {
                response.Success = false;
                response.Message = exc.Message;
            }

            return response;
        }
        #endregion
    }

    public static class AccountExtensions
    {
        extension(Core.Data.Models.User user) {
            public Protos.V1.User ConvertToRpc()
            {
                var u = new Protos.V1.User()
                {
                    Id = user.Id,
                    AllowedToLogin = user.AllowedToLogin,
                    AllowEmail = user.AllowEmail,
                    AllowSMS = user.AllowSMS,
                    EmailAddress = user.Email,
                    Name = user.Name,
                    Phone = user.Phone,
                    Role = user.Role,
                    Source = user.Source,
                    Status = user.Status
                };

                if (user.DateEmailConfirmed.HasValue)
                   u.DateEmailConfirmed = Timestamp.FromDateTime(user.DateEmailConfirmed.Value);

                if (user.DatePhoneConfirmed.HasValue)
                   u.DatePhoneConfirmed = Timestamp.FromDateTime(user.DatePhoneConfirmed.Value);

                u.EmailLists.AddRange(user.EmailLists);

                return u;
            }
        }

        extension(Protos.V1.User user)
        {
            public Core.Data.Models.User ConvertToDb(string accountId)
            {
                var dbUser = new Core.Data.Models.User()
                {
                    Id = user.Id,
                    AccountId = accountId,
                    AllowedToLogin = user.AllowedToLogin,
                    AllowEmail = user.AllowEmail,
                    AllowSMS = user.AllowSMS,
                    DateEmailConfirmed = user.DateEmailConfirmed?.ToDateTime(),
                    DatePhoneConfirmed = user.DatePhoneConfirmed?.ToDateTime(),
                    Email = user.EmailAddress,
                    EmailLists = user.EmailLists.ToArray(),
                    Name = user.Name,
                    Phone = user.Phone,
                    Role = user.Role,
                    Source = user.Source,
                    Status = user.Status
                };

                return dbUser;
                    
            }
        }

    }
}
