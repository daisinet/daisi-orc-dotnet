using Daisi.Orc.Grpc.CommandServices.Handlers;
using Daisi.Protos.V1;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Daisi.Orc.Grpc.CommandServices.HostCommandHandlers
{
    public class InferenceCommandHandler(ILogger<InferenceCommandHandler> logger)
        : OrcCommandHandlerBase
    {
        public async override Task HandleAsync(string hostId, Command command, ChannelWriter<Command> responseQueue, CancellationToken cancellationToken = default)
        {
            switch (command.Name)
            {
                case nameof(CloseInferenceRequest):
                    await CloseInferenceSession(command, responseQueue);
                    break;
            }
        }

        public async Task CloseInferenceSession(Command command, ChannelWriter<Command> responseQueue)
        {
            var request = command.Payload.Unpack<CloseInferenceRequest>();

        }
    }
}
