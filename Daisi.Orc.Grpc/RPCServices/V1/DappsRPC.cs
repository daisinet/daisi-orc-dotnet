using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Protos.V1;
using Daisi.SDK.Extensions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    public class DappsRPC(ILogger<DappsRPC> logger, Cosmo cosmo)
        : DappsProto.DappsProtoBase
    {
        public async override Task<GetDappResponse> Get(GetDappRequest request, ServerCallContext context)
        {
            GetDappResponse response = new GetDappResponse();

            var accountId = context.GetAccountId();

            if (string.IsNullOrWhiteSpace(request.DappId))
            {
                var result = await cosmo.GetDappsAsync(accountId, request.Paging);
                response.Dapps.AddRange(result.Items.Select(i => i.ConvertToRpc()));
                response.TotalCount = result.TotalCount;
            }
            else
            {
                var result = await cosmo.GetDappAsync(request.DappId);
                response.Dapps.Add(result.ConvertToRpc());

            }


            return response;
        }

        public async override Task<GetDappSecretKeyResponse> GetSecretKey(GetDappSecretKeyRequest request, ServerCallContext context)
        {
            GetDappSecretKeyResponse response = new GetDappSecretKeyResponse();

            var accountId = context.GetAccountId();
            var keys = await cosmo.GetKeysByOwnerIdAsync(request.DappId);
            var key = keys.FirstOrDefault(k => k.Type == KeyTypes.Secret.Name);

            if (key == null) throw new Exception("DAISI: Key not found.");
            if (key.Owner.AccountId != accountId) throw new Exception("DAISI: Invalid Account.");

            response.SecretKey = key.Key;

            return response;
        }

        public async override Task<ResetDappResponse> Reset(ResetDappRequest request, ServerCallContext context)
        {
            ResetDappResponse response = new ResetDappResponse();
            var accountId = context.GetAccountId();
            var keys = await cosmo.GetKeysByOwnerIdAsync(request.DappId);
            var key = keys.FirstOrDefault(k => k.Type == KeyTypes.Secret.Name);

            if (key == null) throw new Exception("DAISI: Key not found.");
            if (key.Owner.AccountId != accountId) throw new Exception("DAISI: Invalid Account.");

            key.Key = $"secret-{DateTime.UtcNow.ToString("yyMMddhhmmss")}-{StringExtensions.Random(13, false, true).ToLower()}";
            await cosmo.UpsertKeyAsync(key);

            response.SecretKey = key.Key;
            return response;
        }
        public async override Task<CreateDappResponse> Create(CreateDappRequest request, ServerCallContext context)
        {
            CreateDappResponse response = new CreateDappResponse();

            var accountId = context.GetAccountId();
            if (accountId is null) throw new Exception("DAISI: Invalid Account.");

            Core.Data.Models.Dapp dapp = new Core.Data.Models.Dapp();
            dapp.Name = request.Name;
            dapp.Type = request.Type;
            dapp.AccountId = accountId;
            dapp.DateApproved = DateTime.UtcNow;

            var dappResponse = await cosmo.CreateDappAsync(dapp);
            
            response.Dapp = dappResponse.ConvertToRpc();
            return response;
        }

        public async override Task<DeleteDappResponse> Delete(DeleteDappRequest request, ServerCallContext context)
        {
            DeleteDappResponse response = new();

            try
            {
                var accountId = context.GetAccountId(); 
                var dappResponse = await cosmo.DeleteDappAsync(request.DappId, accountId);
                response.Success = dappResponse;                
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = ex.Message;
            }

            return response;
        }
        public async override Task<UpdateDappResponse> Update(UpdateDappRequest request, ServerCallContext context)
        {
            UpdateDappResponse response = new();

            try
            {
                var accountId = context.GetAccountId();
                await cosmo.PatchDappForWebUpdateAsync(request.Dapp.ConvertToDb(), accountId);
                response.Success = true;
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = ex.Message;
            }

            return response;
        }
    }

    public static class DappExtensions
    {
        extension(Core.Data.Models.Dapp dapp)
        {
            public Protos.V1.Dapp ConvertToRpc()
            {
                var drpc = new Protos.V1.Dapp()
                {
                    Id = dapp.Id,
                    Name = dapp.Name,
                    AccountId = dapp.AccountId,
                    AccountName = dapp.AccountName,
                    Type = dapp.Type                    
                };

                if (dapp.DateApproved.HasValue)
                    drpc.DateApproved = Timestamp.FromDateTime(dapp.DateApproved.Value);

                return drpc;

            }
        }

        extension(Protos.V1.Dapp dapp)
        {
            public Core.Data.Models.Dapp ConvertToDb()
            {
                var db = new Core.Data.Models.Dapp();
                db.Id = dapp.Id;
                db.Name = dapp.Name;
                db.AccountId = dapp.AccountId;
                db.AccountName = dapp.AccountName;
                db.Type = dapp.Type;
                db.DateApproved = dapp.DateApproved?.ToDateTime();
                return db;
            }
        }
    }
}
