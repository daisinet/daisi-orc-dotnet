using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Grpc.Core;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    public class RelaySettingsRPC(ILogger<RelaySettingsRPC> logger) : SettingsProto.SettingsProtoBase
    {
        public async override Task<GetAllSettingsResponse> GetAll(GetAllSettingsRequest request, ServerCallContext context)
        {
            if (!SessionContainer.TryGet(request.SessionId, out var session))
                throw new Exception("DAISI: Invalid Session ID");

            var response = await HostContainer.SendToHostAndWaitAsync<GetAllSettingsRequest, GetAllSettingsResponse>(session, request);
            return response;
        }
        public async override Task<SetAllSettingsResponse> SetAll(SetAllSettingsRequest request, ServerCallContext context)
        {
            if (!SessionContainer.TryGet(request.SessionId, out var session))
                throw new Exception("DAISI: Invalid Session ID");

            if(request.Settings.Host.Id !=  session.CreateRequest.HostId)
                throw new Exception($"DAISI: Invalid Session ID for Specified Host ID \"{request.Settings.Host.Id}\". Use SettingsClientFactory.Create(hostId) instead.");


            var response = await HostContainer.SendToHostAndWaitAsync<SetAllSettingsRequest, SetAllSettingsResponse>(session, request);
            return response;
        }
    }
}
