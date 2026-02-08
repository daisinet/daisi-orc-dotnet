using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Orc.Grpc.CommandServices.Interfaces;
using Daisi.Protos.V1;
using System.Collections.Concurrent;

namespace Daisi.Orc.Grpc.CommandServices.Handlers
{
    public class SessionIncomingQueueHandler(ILogger<SessionIncomingQueueHandler> logger) : OrcCommandHandlerBase
    {
        public override async Task HandleAsync(string hostId, Command command, ConcurrentQueue<Command> responseQueue, CancellationToken cancellationToken = default)
        {
            var q = HostContainer.HostsOnline[hostId].SessionIncomingQueues[command.SessionId];
            q.Enqueue(command);
        }
    }
}
