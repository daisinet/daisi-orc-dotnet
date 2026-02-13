using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Orc.Grpc.CommandServices.Interfaces;
using Daisi.Protos.V1;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Daisi.Orc.Grpc.CommandServices.Handlers
{
    public class SessionIncomingQueueHandler(ILogger<SessionIncomingQueueHandler> logger) : OrcCommandHandlerBase
    {
        public override async Task HandleAsync(string hostId, Command command, ChannelWriter<Command> responseQueue, CancellationToken cancellationToken = default)
        {
            if (!HostContainer.HostsOnline.TryGetValue(hostId, out var hostOnline))
            {
                logger.LogWarning("SessionIncomingQueueHandler: host {HostId} not found online", hostId);
                return;
            }

            // Try per-request channel first (preferred — no message loss)
            if (!string.IsNullOrEmpty(command.RequestId)
                && hostOnline.RequestChannels.TryGetValue(command.RequestId, out var requestChannel))
            {
                requestChannel.Writer.TryWrite(command);
                return;
            }

            // Fallback: session-level channel (legacy path)
            if (hostOnline.SessionIncomingQueues.TryGetValue(command.SessionId, out var sessionChannel))
            {
                sessionChannel.Writer.TryWrite(command);
            }
            else
            {
                logger.LogWarning("SessionIncomingQueueHandler: no queue for session {SessionId} on host {HostId}", command.SessionId, hostId);
            }
        }
    }
}
