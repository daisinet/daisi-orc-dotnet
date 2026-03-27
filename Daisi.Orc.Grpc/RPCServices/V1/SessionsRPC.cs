using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    [Authorize]
    public class SessionsRPC(ILogger<SessionsRPC> logger, Cosmo cosmo, PipelineGroupManager pipelineGroupManager) : SessionsProto.SessionsProtoBase
    {

        public async override Task<CreateSessionResponse> Create(CreateSessionRequest request, ServerCallContext context)
        {
            var clientKey = context.GetClientKey();

            // Check if the requested model is pipeline-enabled
            var enabledModels = await cosmo.GetEnabledModelsAsync();
            var model = enabledModels.FirstOrDefault(m =>
                string.Equals(m.Name, request.ModelName, StringComparison.OrdinalIgnoreCase));

            if (model is not null && model.PipelineEnabled && model.TotalLayers > 0)
            {
                return await CreatePipelineSession(request, context, clientKey, model);
            }

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

        /// <summary>
        /// DaisiChain: Create a pipeline session that distributes model layers across multiple hosts.
        /// </summary>
        private async Task<CreateSessionResponse> CreatePipelineSession(
            CreateSessionRequest request, ServerCallContext context, string clientKey,
            Daisi.Orc.Core.Data.Models.DaisiModel model)
        {
            var accountId = context.GetAccountId();

            // Find available hosts for the pipeline
            int minHosts = model.MinPipelineHosts > 0 ? model.MinPipelineHosts : 2;
            var availableHosts = HostContainer.HostsOnline.Values
                .Where(h => !h.Host.ToolsOnly)
                .OrderBy(h => h.Host.DateLastSession)
                .Take(minHosts)
                .ToList();

            if (availableHosts.Count < minHosts)
                throw new RpcException(new Status(StatusCode.Unavailable,
                    $"DaisiChain: Need {minHosts} hosts for pipeline model '{model.Name}', but only {availableHosts.Count} are online."));

            // Create pipeline group
            // HiddenDim and VocabSize will be reported by hosts after loading — use 0 as placeholder
            var group = pipelineGroupManager.CreateGroup(
                model.Name, model.TotalLayers, hiddenDim: 0, vocabSize: 0, availableHosts);

            // Load pipeline stages on all hosts
            uint contextSize = model.Backend?.ContextSize ?? 4096;
            bool loaded = await pipelineGroupManager.LoadGroupAsync(group, model.FileName, model.Url, contextSize);
            if (!loaded)
                throw new RpcException(new Status(StatusCode.Internal,
                    $"DaisiChain: Failed to load pipeline stages for model '{model.Name}'."));

            // Create the session — the primary host is the first stage
            var firstStageHost = availableHosts[0];

            DaisiSession daisiSession = new();
            daisiSession.Id = "session-" + Guid.NewGuid().ToString();
            daisiSession.CreateRequest = request;
            daisiSession.CreateClientKey = clientKey;
            daisiSession.ConsumerAccountId = accountId;
            daisiSession.PipelineGroup = group;

            daisiSession.CreateResponse = new CreateSessionResponse()
            {
                Id = daisiSession.Id,
                Host = new Protos.V1.Host()
                {
                    Id = firstStageHost.Host.Id,
                    Name = firstStageHost.Host.Name,
                    IpAddress = firstStageHost.Host.IpAddress,
                    Port = firstStageHost.Host.Port,
                },
            };

            SessionContainer.Add(daisiSession);
            firstStageHost.AddSession(daisiSession);

            logger.LogInformation("DaisiChain: Created pipeline session {SessionId} for model '{Model}' with {Stages} stages",
                daisiSession.Id, model.Name, group.Stages.Count);

            return daisiSession.CreateResponse;
        }
        public async override Task<CloseSessionResponse> Close(CloseSessionRequest request, ServerCallContext context)
        {
            if (SessionContainer.TryGet(request.Id, out var session))
            {
                HostContainer.HostsOnline.TryGetValue(session.CreateResponse.Host.Id, out var hostOnline);

                SessionContainer.Close(session.Id);
                hostOnline.CloseSession(session.Id);

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
