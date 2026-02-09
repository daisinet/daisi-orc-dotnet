using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Grpc.Core;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    public class RelayInferenceRPC(ILogger<RelayInferenceRPC> logger, CreditService creditService) : InferencesProto.InferencesProtoBase
    {
        public async override Task<CreateInferenceResponse> Create(CreateInferenceRequest request, ServerCallContext context)
        {
            if (!SessionContainer.TryGet(request.SessionId, out var session))
                throw new Exception("DAISI: Invalid Session ID");

            // Pre-flight credit check: if consumer != host account, verify balance
            var consumerAccountId = context.GetAccountId();
            var hostAccountId = GetHostAccountId(session);
            if (consumerAccountId is not null && hostAccountId is not null && consumerAccountId != hostAccountId)
            {
                var hasFunds = await creditService.HasSufficientCreditsAsync(consumerAccountId, 1);
                if (!hasFunds)
                    throw new RpcException(new Status(StatusCode.ResourceExhausted, "Insufficient credits."));
            }

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

            var stats = await HostContainer.SendToHostAndWaitAsync<InferenceStatsRequest, InferenceStatsResponse>(session, request);

            // Award/debit credits if consumer and host are different accounts
            var consumerAccountId = context.GetAccountId();
            var hostAccountId = GetHostAccountId(session);
            if (stats is not null && consumerAccountId is not null && hostAccountId is not null && consumerAccountId != hostAccountId)
            {
                try
                {
                    await creditService.EarnTokenCreditsAsync(hostAccountId, stats.LastMessageTokenCount, request.InferenceId);
                    await creditService.SpendCreditsAsync(consumerAccountId, stats.LastMessageTokenCount, request.InferenceId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing credits for inference stats");
                }
            }

            return stats;
        }

        private static string? GetHostAccountId(DaisiSession session)
        {
            if (session?.CreateResponse?.Host?.Id is null)
                return null;

            if (HostContainer.HostsOnline.TryGetValue(session.CreateResponse.Host.Id, out var hostOnline))
                return hostOnline.Host.AccountId;

            return null;
        }
    }
}
