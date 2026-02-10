using Daisi.Orc.Grpc.CommandServices.Interfaces;
using Daisi.Protos.V1;
using Grpc.Core;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Daisi.Orc.Grpc.CommandServices.Handlers
{
    public abstract class OrcCommandHandlerBase : IOrcCommandHandler
    {
        public ServerCallContext CallContext { get; set; }
        public abstract Task HandleAsync(string hostId, Command command, ChannelWriter<Command> responseQueue, CancellationToken cancellationToken = default);

    }
}
