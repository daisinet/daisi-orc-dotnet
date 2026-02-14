using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Protos.V1;
using Daisi.SDK.Extensions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Configuration;
using System.Runtime.CompilerServices;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    public class AuthRPC(ILogger<AuthRPC> logger, Cosmo cosmo) : AuthProto.AuthProtoBase
    {
        public async override Task<DeleteClientKeyResponse> DeleteClientKey(DeleteClientKeyRequest request, ServerCallContext context)
        {
            var result = await cosmo.DeleteClientKeyAsync(request.ClientKey, request.SecretKey);
            return new DeleteClientKeyResponse() { Success = result };
        }
        public async override Task<GetAuthenticatedUserResponse> GetAuthenticatedUser(GetAuthenticatedUserRequest request, ServerCallContext context)
        {
            var clientKey = context.GetClientKey();
            var key = await cosmo.GetKeyAsync(clientKey, KeyTypes.Client);
            if (key is null || key.Owner.SystemRole != SystemRoles.User)
                throw new Exception("DAISI: Invalid client key.");

            var user = await cosmo.GetUserAsync(context.GetUserId(), context.GetAccountId());
            if (user is null)
                throw new Exception("DAISI: Could not find user. Make sure the user is properly authenticated and you pass the user Client Key.");

            return new GetAuthenticatedUserResponse
            {
                User = user.ConvertToRpc()
            };
        }

        public async override Task<CreateClientKeyResponse> CreateClientKey(CreateClientKeyRequest request, ServerCallContext context)
        {
            var response = new CreateClientKeyResponse();

            //Get the secret key
            var secretKey = await cosmo.GetKeyAsync(request.SecretKey, Core.Data.Models.KeyTypes.Secret);

            //Verify that it exists
            if (secretKey is null)
                throw new Exception("DAISI: Invalid Secret Key");

            AccessKeyOwner owner = secretKey.Owner;

            if (!string.IsNullOrWhiteSpace(request.OwnerId))
            {
                owner = new AccessKeyOwner();
                owner.Id = request.OwnerId;
                owner.Name = request.OwnerName;
                owner.SystemRole = request.OwnerRole;
            }

            var clientKey = await cosmo.CreateClientKeyAsync(secretKey,
                                                                context.GetHttpContext()?.Connection?.RemoteIpAddress,
                                                                owner,
                                                                request.AccessToIds?.ToList() ?? []);
            response.ClientKey = clientKey.Key;
            response.OwnerId = clientKey.Owner.Id;

            if (clientKey.DateExpires.HasValue)
                response.KeyExpiration = Timestamp.FromDateTime(clientKey.DateExpires.Value);
            else
                response.KeyExpiration = null;

            logger.LogInformation($"Client Key Produced: {response.ClientKey}");

            return response;
        }

        [AllowAnonymous]
        public async override Task<ValidateClientKeyResponse> ValidateClientKey(ValidateClientKeyRequest request, ServerCallContext context)
        {
            (ValidateClientKeyResponse response, AccessKey key) response = (new ValidateClientKeyResponse(), null);

            response = await ValidateClientKeyWithSecretKey(request.ClientKey, request.SecretKey);

            if (response.response.IsValid
                && response.response.KeyExpiration is not null
                && response.response.KeyExpiration.ToDateTime() < DateTime.UtcNow.AddMinutes(30)
                && response.key is not null)
            {
                var keyRes = await cosmo.SetKeyTTLAsync(response.key, 60);
                response.response.KeyExpiration = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(60));


            }

            if (response.key is not null && response.response.IsValid && response.key.Owner.SystemRole == SystemRoles.User)
            {
                var owner = response.key.Owner;
                response.response.UserId = owner.Id;
                response.response.UserName = owner.Name;
                response.response.UserRole = owner.Role ?? UserRoles.Reader;
                response.response.UserAccountId = owner.AccountId;
                logger.LogInformation($"ValidateClientKey: User={owner.Name}, Role={owner.Role} ({(int)(owner.Role ?? UserRoles.Reader)})");
            }

            return response.response;
        }

        private async Task<(ValidateClientKeyResponse response, AccessKey key)> ValidateClientKeyWithSecretKey(string clientKey, string appSecretKey)
        {
            (ValidateClientKeyResponse response, AccessKey key) response = (new ValidateClientKeyResponse(), null);

            var result = await cosmo.GetKeyAsync(clientKey, KeyTypes.Client);
            if (result is null || result.DateExpires < DateTime.UtcNow)
            {
                response.response.IsValid = false;
                response.response.FailureReason = "DAISI: KEY NOT FOUND";
                return response;
            }

            var secretKey = await cosmo.GetKeyAsync(appSecretKey, KeyTypes.Secret);

            if (result.ParentKeyId != secretKey.Id)
            {
                response.response.IsValid = false;
                response.response.FailureReason = "DAISI: INVALID KEY ACCESS";
                return response;
            }

            response.key = result;
            response.response.IsValid = true;
            response.response.FailureReason = string.Empty;
            response.response.KeyExpiration = result.DateExpires.HasValue ? Timestamp.FromDateTime(result.DateExpires.Value) : null;
            return response;
        }


        [AllowAnonymous]
        public async override Task<SendAuthCodeResponse> SendAuthCode(SendAuthCodeRequest request, ServerCallContext context)
        {
            var response = new SendAuthCodeResponse();

            // Validate the request
            if (string.IsNullOrWhiteSpace(request.EmailOrPhone))
            {
                response.Success = false;
                response.ErrorMessage = "Invalid email or phone number.";
                return response;
            }

            // Simulate sending an authentication code
            var user = await cosmo.GetUserByEmailOrPhoneAsync(request.EmailOrPhone);
            if (user is null)
            {
                response.Success = false;
                response.ErrorMessage = "User not found.";
                return response;
            }

            if (!user.AllowedToLogin)
            {
                response.Success = false;
                response.ErrorMessage = "User not allowed to login.";
                return response;
            }

            user.AuthCode = StringExtensions.Random(5, false, true);
            user.DateAuthCodeSent = DateTime.UtcNow;

            await cosmo.UpdateUserAsync(user);

            _ = Task.Run(async () =>
            {
                if (request.EmailOrPhone.Contains("@"))
                    await EmailSender.Instance.Value.SendEmailAsync(request.EmailOrPhone, "DAISI Authentication Code", $"Your DAISI authentication code is: {user.AuthCode}", toName: user.Name);
                else
                    await SmsSender.Instance.Value.SendSmsAsync(request.EmailOrPhone, $"DAISI authentication code: {user.AuthCode}");

            });


            logger.LogInformation($"Auth code sent to {request.EmailOrPhone}: {user.AuthCode}");

            response.Success = true;
            return response;
        }

        [AllowAnonymous]
        public async override Task<ValidateAuthCodeResponse> ValidateAuthCode(ValidateAuthCodeRequest request, ServerCallContext context)
        {
            var response = new ValidateAuthCodeResponse();

            // Validate the request
            if (string.IsNullOrWhiteSpace(request.EmailOrPhone) || string.IsNullOrWhiteSpace(request.AuthCode))
            {
                response.Success = false;
                response.ErrorMessage = "Invalid email/phone number or auth code.";
                return response;
            }

            var user = await cosmo.GetUserByEmailOrPhoneAsync(request.EmailOrPhone);
            var authCodeValid = user is not null
                && !string.IsNullOrWhiteSpace(user.AuthCode)
                && user.AuthCode?.ToLower() == request.AuthCode.ToLower()
                && user.DateAuthCodeSent.HasValue
                && user.DateAuthCodeSent.Value.AddMinutes(2) > DateTime.UtcNow;

            if (!authCodeValid)
            {
                response.Success = false;
                response.ErrorMessage = "Invalid or expired authentication code.";
                return response;
            }
            logger.LogInformation($"Auth code validated for {request.EmailOrPhone}: {request.AuthCode}");

            /// Create the client key with the app key if provided
            if (!string.IsNullOrWhiteSpace(request.AppId))
            {
                var appClientKey = await cosmo.CreateClientKeyForAppAsync(request.AppId,
                    request.SecretKey,
                    context.GetHttpContext()?.Connection?.RemoteIpAddress,
                    new AccessKeyOwner
                    {
                        Id = user.Id,
                        Name = user.Name,
                        AccountId = user.AccountId,
                        SystemRole = SystemRoles.User,
                        AllowedToLogin = user.AllowedToLogin,
                        Role = user.Role
                    });

                if (appClientKey is null)
                {
                    response.Success = false;
                    response.ErrorMessage = "Invalid App ID.";
                    return response;
                }

                response.ClientKey = appClientKey.Key;
                response.Success = true;
                response.UserName = user.Name;
                response.UserRole = user.Role;
                response.AccountName = user.AccountName;
                response.AccountId = user.AccountId;

                if (appClientKey.DateExpires.HasValue)
                    response.KeyExpiration = Timestamp.FromDateTime(appClientKey.DateExpires.Value);

                return response;

            }
            else if (!string.IsNullOrWhiteSpace(request.SecretKey))
            {
                var secretKey = await cosmo.GetKeyAsync(request.SecretKey, Core.Data.Models.KeyTypes.Secret);

                if (secretKey is null)
                {
                    //response.ClientKey = string.Empty;
                    response.Success = false;
                    response.ErrorMessage = "Invalid Secret Key";
                    return response;
                }

                var clientKey = await cosmo.CreateClientKeyAsync(secretKey,
                                            context.GetHttpContext()?.Connection?.RemoteIpAddress,
                                            new AccessKeyOwner
                                            {
                                                Id = user.Id,
                                                Name = user.Name,
                                                AccountId = user.AccountId,
                                                SystemRole = SystemRoles.User,
                                                AllowedToLogin = user.AllowedToLogin,
                                                Role = user.Role
                                            },
                                            new() { secretKey.Owner.Id });

                response.UserName = user.Name;
                response.UserRole = user.Role;
                response.AccountName = user.AccountName;
                response.AccountId = user.AccountId;
                response.ClientKey = clientKey.Key;

                if (clientKey.DateExpires.HasValue)
                    response.KeyExpiration = Timestamp.FromDateTime(clientKey.DateExpires.Value);

                response.Success = true;
                return response;
            }



            response.ErrorMessage = "Unknown error occurred while validating auth code.";
            response.Success = false;
            return response;



        }
    }
}
