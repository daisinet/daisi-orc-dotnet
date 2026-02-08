using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Grpc.Core;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    public class RelayInferenceRPC(ILogger<RelayInferenceRPC> logger) : InferencesProto.InferencesProtoBase
    {
        public async override Task<CreateInferenceResponse> Create(CreateInferenceRequest request, ServerCallContext context)
        {
            if (!SessionContainer.TryGet(request.SessionId, out var session))
                throw new Exception("DAISI: Invalid Session ID");


            return await HostContainer.SendToHostAndWaitAsync<CreateInferenceRequest, CreateInferenceResponse>(session, request);
        }
        public async override Task<CloseInferenceResponse> Close(CloseInferenceRequest request, ServerCallContext context)
        {
            if (!SessionContainer.TryGet(request.SessionId, out var session))
                throw new Exception("DAISI: Invalid Session ID");

            return await HostContainer.SendToHostAndWaitAsync<CloseInferenceRequest, CloseInferenceResponse>(session, request);

        }        

        public async override Task Send(SendInferenceRequest request, IServerStreamWriter<SendInferenceResponse> responseStream, ServerCallContext context)
        {
            if (!SessionContainer.TryGet(request.SessionId, out var session))
                throw new Exception("DAISI: Invalid Session ID");
            
            await HostContainer.SendToHostAndStreamAsync<SendInferenceRequest, SendInferenceResponse>(session, request, responseStream, context.CancellationToken);
           
        }

        public async override Task<InferenceStatsResponse> Stats(InferenceStatsRequest request, ServerCallContext context)
        {
            if (!SessionContainer.TryGet(request.SessionId, out var session))
                throw new Exception("DAISI: Invalid Session ID");

            return await HostContainer.SendToHostAndWaitAsync<InferenceStatsRequest, InferenceStatsResponse>(session, request);

        }
    }
}
