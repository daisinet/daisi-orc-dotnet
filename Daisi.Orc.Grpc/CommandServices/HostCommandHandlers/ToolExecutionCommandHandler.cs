using Daisi.Orc.Grpc.Authentication;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Orc.Grpc.CommandServices.Handlers;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using System.Threading.Channels;

namespace Daisi.Orc.Grpc.CommandServices.HostCommandHandlers
{
    /// <summary>
    /// Handles ExecuteToolRequest from an inference host — finds an available tools-only host
    /// and forwards the request, then returns the response to the requesting host.
    /// </summary>
    public class ToolExecutionCommandHandler(ILogger<ToolExecutionCommandHandler> logger)
        : OrcCommandHandlerBase
    {
        public override async Task HandleAsync(string hostId, Command command, ChannelWriter<Command> responseQueue, CancellationToken cancellationToken = default)
        {
            if (command.Name != nameof(ExecuteToolRequest))
                return;

            var request = command.Payload.Unpack<ExecuteToolRequest>();
            logger.LogInformation("Tool execution request from host {HostId}: tool {ToolId}", hostId, request.ToolId);

            // Find the requesting host's account to scope the tools-only host search
            if (!HostContainer.HostsOnline.TryGetValue(hostId, out var requestingHost))
            {
                logger.LogWarning("Requesting host {HostId} not found online", hostId);
                SendErrorResponse(responseQueue, command.RequestId, "Requesting host not found.");
                return;
            }

            var toolsOnlyHost = HostContainer.GetNextToolsOnlyHost(requestingHost.Host.AccountId);

            if (toolsOnlyHost == null)
            {
                logger.LogWarning("No tools-only host available for account {AccountId}", requestingHost.Host.AccountId);
                SendErrorResponse(responseQueue, command.RequestId, "No tools-only host available.");
                return;
            }

            logger.LogInformation("Delegating tool {ToolId} to tools-only host {HostName}", request.ToolId, toolsOnlyHost.Host.Name);
            toolsOnlyHost.Host.DateLastSession = DateTime.UtcNow;

            var response = await HostContainer.SendToolExecutionToHostAsync(toolsOnlyHost.Host.Id, request);

            if (response == null)
            {
                SendErrorResponse(responseQueue, command.RequestId, "Tool execution timed out or failed.");
                return;
            }

            responseQueue.TryWrite(new Command
            {
                Name = nameof(ExecuteToolResponse),
                Payload = Any.Pack(response),
                RequestId = command.RequestId
            });
        }

        private static void SendErrorResponse(ChannelWriter<Command> responseQueue, string? requestId, string errorMessage)
        {
            responseQueue.TryWrite(new Command
            {
                Name = nameof(ExecuteToolResponse),
                Payload = Any.Pack(new ExecuteToolResponse
                {
                    Success = false,
                    ErrorMessage = errorMessage
                }),
                RequestId = requestId
            });
        }
    }
}
