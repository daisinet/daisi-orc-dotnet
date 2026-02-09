using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Orc.Grpc.CommandServices.Handlers;
using Daisi.Protos.V1;
using System.Collections.Concurrent;

namespace Daisi.Orc.Grpc.CommandServices.HostCommandHandlers
{
    public class InferenceReceiptCommandHandler(
        CreditService creditService,
        ILogger<InferenceReceiptCommandHandler> logger) : OrcCommandHandlerBase
    {
        public override async Task HandleAsync(
            string hostId,
            Command command,
            ConcurrentQueue<Command> responseQueue,
            CancellationToken cancellationToken = default)
        {
            if (!HostContainer.HostsOnline.TryGetValue(hostId, out var hostOnline))
            {
                logger.LogWarning($"InferenceReceipt received from unknown host {hostId}");
                return;
            }

            var receipt = command.Payload.Unpack<InferenceReceipt>();

            var hostAccountId = hostOnline.Host.AccountId;

            logger.LogInformation(
                $"Processing InferenceReceipt from host {hostOnline.Host.Name}: " +
                $"InferenceId={receipt.InferenceId}, Tokens={receipt.TokenCount}, " +
                $"Consumer={receipt.ConsumerAccountId}");

            try
            {
                await creditService.ProcessInferenceReceiptAsync(receipt, hostAccountId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error processing InferenceReceipt for host {hostId}");
            }
        }
    }
}
