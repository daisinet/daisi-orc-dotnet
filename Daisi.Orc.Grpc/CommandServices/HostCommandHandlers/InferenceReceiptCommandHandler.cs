using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Orc.Grpc.CommandServices.Handlers;
using Daisi.Protos.V1;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Daisi.Orc.Grpc.CommandServices.HostCommandHandlers
{
    public class InferenceReceiptCommandHandler(
        CreditService creditService,
        Cosmo cosmo,
        ILogger<InferenceReceiptCommandHandler> logger) : OrcCommandHandlerBase
    {
        /// <summary>
        /// Cache mapping consumer client keys to their account IDs.
        /// A consumer's account never changes, so this is safe to cache for the process lifetime.
        /// </summary>
        private static readonly ConcurrentDictionary<string, string> _consumerAccountCache = new();

        public override async Task HandleAsync(
            string hostId,
            Command command,
            ChannelWriter<Command> responseQueue,
            CancellationToken cancellationToken = default)
        {
            if (!HostContainer.HostsOnline.TryGetValue(hostId, out var hostOnline))
            {
                logger.LogWarning($"InferenceReceipt received from unknown host {hostId}");
                return;
            }

            var receipt = command.Payload.Unpack<InferenceReceipt>();
            var hostAccountId = hostOnline.Host.AccountId;

            // Always persist token stats so the host dashboard works
            try
            {
                float tokenProcessingSeconds = receipt.ComputeTimeMs / 1000f;
                await cosmo.RecordInferenceMessageAsync(
                    hostId,
                    hostAccountId,
                    receipt.InferenceId,
                    receipt.SessionId,
                    receipt.TokenCount,
                    tokenProcessingSeconds);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error recording inference message for host {hostId}");
            }

            // Resolve consumer account from client key (cached to avoid repeated DB reads)
            string? consumerAccountId = null;
            if (!string.IsNullOrEmpty(receipt.ConsumerClientKey))
            {
                if (_consumerAccountCache.TryGetValue(receipt.ConsumerClientKey, out var cached))
                {
                    consumerAccountId = cached;
                }
                else
                {
                    try
                    {
                        var key = await cosmo.GetKeyAsync(receipt.ConsumerClientKey, KeyTypes.Client);
                        consumerAccountId = key?.Owner?.AccountId;
                        if (consumerAccountId is not null)
                            _consumerAccountCache.TryAdd(receipt.ConsumerClientKey, consumerAccountId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, $"Could not resolve consumer client key for host {hostId}");
                    }
                }
            }

            // Only process credits when consumer is a different account
            if (!string.IsNullOrEmpty(consumerAccountId) && consumerAccountId != hostAccountId)
            {
                logger.LogInformation(
                    $"Processing credits for InferenceReceipt from host {hostOnline.Host.Name}: " +
                    $"InferenceId={receipt.InferenceId}, Tokens={receipt.TokenCount}, " +
                    $"Consumer={consumerAccountId}");

                try
                {
                    receipt.ConsumerAccountId = consumerAccountId;
                    await creditService.ProcessInferenceReceiptAsync(receipt, hostAccountId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Error processing credits for InferenceReceipt from host {hostId}");
                }
            }
        }
    }
}
