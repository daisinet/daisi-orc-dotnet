using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Protos.V1;
using Daisi.SDK.Extensions;
using Grpc.Core;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    public class NetworksRPC(ILogger<NetworksRPC> logger, Cosmo cosmo) : NetworksProto.NetworksProtoBase
    {
        #region Networks
        public async override Task<ArchiveNetworkResponse> Archive(ArchiveNetworkRequest request, ServerCallContext context)
        {
            ArchiveNetworkResponse response = new ArchiveNetworkResponse();

            var accountId = context.GetAccountId();
            if(accountId == null) return new ArchiveNetworkResponse() { Success = false , Message = "DAISI: Invalid Account ID"};

            try
            {
                var result = await cosmo.ArchiveNetworkAsync(request.NetworkId, accountId);
                response.Success = true;
            }
            catch(Exception exc)
            {
                response.Success = false;
                response.Message = exc.Message; 
            }
            return response;
        }
        public async override Task<CreateNetworkResponse> Create(CreateNetworkRequest request, ServerCallContext context)
        {
            CreateNetworkResponse response = new CreateNetworkResponse();

            var accountId = context.GetAccountId();
            if(accountId == null || (!string.IsNullOrWhiteSpace(request.Network.AccountId) && accountId != request.Network.AccountId))
            {
                throw new Exception("DAISI: Invalid Account ID.");
            }
            request.Network.AccountId = accountId;
            var result = await cosmo.CreateNetworkAsync(request.Network.ConvertToDb());
            response.Network = result.ConvertToRpc();
            return response;
        }
        public async override Task<GetNetworksResponse> Get(GetNetworksRequest request, ServerCallContext context)
        {
            GetNetworksResponse response = new GetNetworksResponse();

            var paged = await cosmo.GetNetworksAsync(request.Paging,
                                                        request.IsPublic,
                                                        context.GetAccountId(),
                                                        request.NetworkId
                                                        );

            response.TotalCount = paged.TotalCount;
            response.Networks.AddRange(paged.Items.Select(i => i.ConvertToRpc()));
            
            return response;
        }

        public async override Task<UpdateNetworkResponse> Update(UpdateNetworkRequest request, ServerCallContext context)
        {
            UpdateNetworkResponse response = new UpdateNetworkResponse();

            try
            {
                var accountId = context.GetAccountId();
               
                var result = await cosmo.PatchNetworkForWebAsync(request.Network.ConvertToDb(), accountId);
                response.Success = true;                
            }
            catch (Exception exc)
            {
                response.Success = false;
                response.Message = exc.Message;
            }

            return response;
        }
        #endregion

   
    } 

    public static class NetworkExtensions
    {
        extension(Core.Data.Models.Network net)
        {
            public Network ConvertToRpc() => net.CopyTo(new Network());
        }

        extension(Network net)
        {
            public Core.Data.Models.Network ConvertToDb() => net.CopyTo(new Core.Data.Models.Network());
        }
    }
}
