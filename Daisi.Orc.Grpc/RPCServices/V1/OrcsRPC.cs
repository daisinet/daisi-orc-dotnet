using Daisi.Orc.Core.Data;
using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Protos.V1;
using Daisi.SDK.Extensions;
using Grpc.Core;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    public class OrcsRPC(ILogger<OrcsRPC> logger, Cosmo cosmo) : OrcsProto.OrcsProtoBase
    {
        #region Orcs
        public async override Task<ArchiveOrcResponse> Archive(ArchiveOrcRequest request, ServerCallContext context)
        {
            ArchiveOrcResponse response = new ArchiveOrcResponse();

            try
            {
                var accountId = context.GetAccountId();

                var result = await cosmo.PatchOrcStatusAsync(request.OrcId, OrcStatus.Archived, accountId);
                response.Success = result is not null;

            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = ex.Message;
            }
            return response;
        }
        public async override Task<CreateOrcResponse> Create(CreateOrcRequest request, ServerCallContext context)
        {
            CreateOrcResponse response = new CreateOrcResponse();

            var accountId = context.GetAccountId();
            
            var result = await cosmo.CreateOrcAsync(request.Orc.ConvertToDb(), accountId);
            response.Orc = result.ConvertToRpc();
            return response;
        }
        public async override Task<GetOrcsResponse> Get(GetOrcsRequest request, ServerCallContext context)
        {
            GetOrcsResponse response = new GetOrcsResponse();
            
            var accountId = context.GetAccountId();            

            PagedResult<Core.Data.Models.Orchestrator> paged = await cosmo.GetOrcsAsync(request.Paging, request.OrcId, accountId);
            response.Orcs.AddRange(paged.Items.Select(i => i.ConvertToRpc()));
            response.TotalCount = paged.TotalCount;

            return response;
        }
        public async override Task<UpdateOrcResponse> Update(UpdateOrcRequest request, ServerCallContext context)
        {
            UpdateOrcResponse response = new UpdateOrcResponse();

            try
            {
                var accountId = context.GetAccountId();

                var result = await cosmo.PatchOrcForWebUpdateAsync(request.Orc.ConvertToDb(), accountId);
                
                response.Success = true;

            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = ex.Message;
            }

            return response;
        }

        #endregion
    }

    public static class OrcExtensions
    {
        extension(Core.Data.Models.Orchestrator orc)
        {
            public Orchestrator ConvertToRpc()
            {
                var o = orc.CopyTo(new Orchestrator());
                o.Networks.AddRange(orc.Networks.Select(i => i.ConvertToRpc()));
                return o;
            }
            
        }
        extension(Core.Data.Models.OrcNetwork orcNet)
        {
            public OrcNetwork ConvertToRpc() => orcNet.CopyTo(new OrcNetwork());
        }
        extension(Orchestrator orc)
        {
            public Core.Data.Models.Orchestrator ConvertToDb() {
                var o = orc.CopyTo(new Core.Data.Models.Orchestrator());
                o.Networks = orc.Networks.Select(on => on.ConvertToDb()).ToArray();
                return o;
            }
        }
        extension(OrcNetwork orcNet)
        {
            public Core.Data.Models.OrcNetwork ConvertToDb() => orcNet.CopyTo(new Core.Data.Models.OrcNetwork());
        }
    }
}
