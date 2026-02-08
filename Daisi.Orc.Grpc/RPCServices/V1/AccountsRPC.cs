using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Components.Infrastructure;
using Microsoft.Azure.Cosmos.Linq;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    public class AccountsRPC(ILogger<AccountsRPC> logger, Cosmo cosmo) : AccountsProto.AccountsProtoBase
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
