using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    [Authorize]
    public class SessionsRPC(ILogger<SessionsRPC> logger, Cosmo cosmo) : SessionsProto.SessionsProtoBase
    {

        public async override Task<CreateSessionResponse> Create(CreateSessionRequest request, ServerCallContext context)
        {
            var clientKey = context.GetClientKey();

            DaisiSession daisiSession = new();
            daisiSession.Id = "session-" + Guid.NewGuid().ToString();
            daisiSession.CreateRequest = request;
            daisiSession.CreateResponse = new CreateSessionResponse() { Id = daisiSession.Id };
            daisiSession.CreateClientKey = clientKey;
            daisiSession.ConsumerAccountId = context.GetAccountId();

            daisiSession.CreateResponse.Host = HostContainer.GetNextHost(request, context, cosmo);

            SessionContainer.TryGetExistingSession(clientKey, daisiSession.CreateResponse.Host.Id, out var existingSession);

            if (existingSession != null)
            {
                logger.LogInformation($"DAISI: Reusing existing session {existingSession.Id} for client {clientKey} on host {daisiSession.CreateResponse.Host.Id}");
                return existingSession.CreateResponse;
            }
            else
            {
                HostContainer.HostsOnline.TryGetValue(daisiSession.CreateResponse.Host.Id, out var hostOnline);
                logger.LogInformation($"DAISI: Created NEW session {daisiSession.Id} for client {clientKey} on host {daisiSession.CreateResponse.Host.Id}");
                SessionContainer.Add(daisiSession);
                hostOnline.AddSession(daisiSession);

                return daisiSession.CreateResponse;
            }
        }
        public async override Task<CloseSessionResponse> Close(CloseSessionRequest request, ServerCallContext context)
        {
            if (SessionContainer.TryGet(request.Id, out var session))
            {
                HostContainer.HostsOnline.TryGetValue(session.CreateResponse.Host.Id, out var hostOnline);

                hostOnline.CloseSession(session.Id);
                SessionContainer.Close(session.Id);

                return new CloseSessionResponse() { Success = true };
            }

            return new CloseSessionResponse() { Success = false };
        }


        public async override Task<ClaimSessionResponse> Claim(ClaimSessionRequest request, ServerCallContext context)
        {
            if (!SessionContainer.TryGet(request.Id, out var session))
            {
                logger.LogWarning("Claim failed: session {SessionId} not found", request.Id);
                return new ClaimSessionResponse() { Success = false };
            }

            session.ClaimRequest = request;
            session.ClaimClientKey = context.GetClientKey();

            var response = new ClaimSessionResponse() { Success = true, ModelName = session.CreateRequest.ModelName };
            session.ClaimResponse = response;

            return response;
        }

        public async override Task<ConnectResponse> Connect(ConnectRequest request, ServerCallContext context)
        {
            if (!SessionContainer.TryGet(request.SessionId, out var session))
                throw new Exception("DAISI: Invalid Session ID");

            var response = await HostContainer.SendToHostAndWaitAsync<ConnectRequest, ConnectResponse>(session, request);

            if (response is null)
                throw new Exception("DAISI: Host did not respond to connection request.");

            if (string.IsNullOrWhiteSpace(response.Id))
                throw new Exception("DAISI: Host rejected the connection request.");

            return response;
        }


    }


}
