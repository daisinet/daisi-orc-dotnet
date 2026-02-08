using Daisi.Orc.Grpc.CommandServices.Handlers;
using Daisi.Protos.V1;
using System.Collections.Concurrent;

namespace Daisi.Orc.Grpc.CommandServices.HostCommandHandlers
{
    public class InferenceCommandHandler(ILogger<InferenceCommandHandler> logger)
        : OrcCommandHandlerBase
    {
        public async override Task HandleAsync(string hostId, Command command, ConcurrentQueue<Command> responseQueue, CancellationToken cancellationToken = default)
        {
            switch (command.Name)
            {
                case nameof(CloseInferenceRequest):
                    await CloseInferenceSession(command, responseQueue);
                    break;
            }            
        }

        public async Task CloseInferenceSession(Command command, ConcurrentQueue<Command> responseQueue)
        {
            var request = command.Payload.Unpack<CloseInferenceRequest>();

        }
    }
}
