using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Orc.Grpc.CommandServices.Handlers;
using Daisi.Orc.Grpc.CommandServices.HostCommandHandlers;
using Daisi.Orc.Grpc.CommandServices.Interfaces;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Concurrent;
using System.Configuration;
using System.Threading.Channels;
using System.Threading.Tasks;
using Type = System.Type;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    [Authorize, HostsOnly]
    public class HostCommandsRPC(ILogger<HostCommandsRPC> logger, IServiceProvider serviceProvider) : HostCommandsProto.HostCommandsProtoBase
    {

        /// <summary>
        /// List of handlers for the incoming commands that aren't the SessionIncomingQueueHandler.
        /// </summary>
        public static ConcurrentDictionary<string, Type> Handlers = new ConcurrentDictionary<string, Type>(
            new Dictionary<string, Type>(){
                { nameof(HeartbeatRequest), typeof(HeartbeatRequestCommandHandler) },
                { nameof(EnvironmentRequest), typeof(EnvironmentRequestCommandHandler) },
                { nameof(InferenceReceipt), typeof(InferenceReceiptCommandHandler) },
                { nameof(ExecuteToolRequest), typeof(ToolExecutionCommandHandler) },
            }
        );

        private bool CanAcceptHostConnection()
        {
            var maxHostsSetting = Program.App.Configuration.GetValue<string>("Daisi:MaxHosts");
            if (int.TryParse(maxHostsSetting, out var maxHostCount))
            {
                return maxHostCount > HostContainer.HostsOnline.Count;
            }

            return true;
        }
        private async Task SendHostToNextOrc(IServerStreamWriter<Command> responseStream, Cosmo cosmo)
        {
            var nextOrcId = Program.App.Configuration.GetValue<string>("Daisi:NextOrcId");
            if (!string.IsNullOrWhiteSpace(nextOrcId))
            {
                var accountId = Program.App.Configuration.GetValue<string>("Daisi:AccountId");
                var orcs = await cosmo.GetOrcsAsync(null, nextOrcId, accountId);

                if (orcs.TotalCount > 0)
                {
                    var orc = orcs.Items.FirstOrDefault()!;

                    await responseStream.WriteAsync(new Command()
                    {
                        Name = nameof(MoveOrcRequest),
                        Payload = Any.Pack(new MoveOrcRequest()
                        {
                            Domain = orc.Domain,
                            Port = orc.Port,
                            UseSSL = true
                        })
                    });
                }
            }
        }
        /// <summary>
        /// Browser-compatible: server-stream for pushing commands to the host.
        /// The host calls this once and receives commands continuously.
        /// </summary>
        public async override Task ListenForCommands(ListenForCommandsRequest request, IServerStreamWriter<Command> responseStream, ServerCallContext context)
        {
            Cosmo cosmo = serviceProvider.GetService<Cosmo>()!;

            if (!CanAcceptHostConnection())
            {
                await SendHostToNextOrc(responseStream, cosmo);
                return;
            }

            string clientKey = context.GetClientKey()!;
            var key = await cosmo.GetKeyAsync(clientKey, KeyTypes.Client);

            var host = await HostContainer.RegisterHostAsync(key.Owner.Id, cosmo, context, Program.App.Configuration);
            HostContainer.HostsOnline.TryGetValue(host.Id, out var hostOnline);
            hostOnline.ClientKeyId = key.Id;

            // Mark as browser host — skips update checks and server-driven model sync
            host.OperatingSystem = "Browser";
            host.AppVersion = "1.0.0.0";
            await cosmo.PatchHostEnvironmentAsync(host);

            logger.LogInformation($"Browser host \"{host.Name}\" connected via ListenForCommands");

            // Block and send outgoing commands to the host until disconnected
            await hostOnline.SendOutgoingCommandsAsync(responseStream, context.CancellationToken);

            if (HostContainer.HostsOnline.TryGetValue(host.Id, out var currentOnline) && currentOnline == hostOnline)
            {
                await HostContainer.UnregisterHostAsync(host.Id, cosmo, Program.App.Configuration);
            }
        }

        /// <summary>
        /// Browser-compatible: unary RPC for the host to send commands (heartbeat, inference responses, etc).
        /// </summary>
        public async override Task<SendCommandResponse> SendCommand(SendCommandRequest request, ServerCallContext context)
        {
            Cosmo cosmo = serviceProvider.GetService<Cosmo>()!;
            string clientKey = context.GetClientKey()!;
            var key = await cosmo.GetKeyAsync(clientKey, KeyTypes.Client);

            // Find the host's online state
            var hostId = key.Owner.Id;
            // The owner of a host client key is the host itself — find which host is online with this key
            var onlineHost = HostContainer.HostsOnline.Values.FirstOrDefault(h => h.ClientKeyId == key.Id);
            if (onlineHost == null)
            {
                return new SendCommandResponse { Success = false };
            }

            var command = request.Command;
            var sessionHandler = serviceProvider.GetService<SessionIncomingQueueHandler>()!;
            sessionHandler.CallContext = context;

            try
            {
                IOrcCommandHandler? handler = null;

                if (Handlers.TryGetValue(command.Name, out Type? commandHandlerType))
                {
                    handler = (IOrcCommandHandler?)serviceProvider.GetService(commandHandlerType);
                    handler.CallContext = context;
                    await handler.HandleAsync(onlineHost.Host.Id, command, onlineHost.OutgoingQueue.Writer, context.CancellationToken);
                }
                else if (!string.IsNullOrWhiteSpace(command.SessionId))
                {
                    await sessionHandler.HandleAsync(onlineHost.Host.Id, command, onlineHost.OutgoingQueue.Writer, context.CancellationToken);
                }
                else if (!string.IsNullOrEmpty(command.RequestId)
                    && onlineHost.RequestChannels.TryGetValue(command.RequestId, out var requestChannel))
                {
                    requestChannel.Writer.TryWrite(command);
                }
            }
            catch (Exception exc)
            {
                logger.LogError($"SendCommand error: {exc.GetBaseException().Message}");
                return new SendCommandResponse { Success = false };
            }

            return new SendCommandResponse { Success = true };
        }

        public async override Task Open(IAsyncStreamReader<Command> requestStream, IServerStreamWriter<Command> responseStream, ServerCallContext context)
        {
            Cosmo cosmo = serviceProvider.GetService<Cosmo>()!;

            if (!CanAcceptHostConnection())
            {
                await SendHostToNextOrc(responseStream, cosmo);
                return;
            }

            string clientKey = context.GetClientKey()!;
            var key = await cosmo.GetKeyAsync(clientKey, KeyTypes.Client);

            var host = await HostContainer.RegisterHostAsync(key.Owner.Id, cosmo, context, Program.App.Configuration);
            HostContainer.HostsOnline.TryGetValue(host.Id, out var hostOnline);
            hostOnline.ClientKeyId = key.Id;

            var sessionHandler = serviceProvider.GetService<SessionIncomingQueueHandler>()!;
            sessionHandler.CallContext = context;

            // Commands received from the Host
            _ = Task.Run(async () =>
            {
                while (!context.CancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await foreach (var command in requestStream.ReadAllAsync(context.CancellationToken))
                        {
                            try
                            {
                                IOrcCommandHandler? handler = null;

                                // Process command with specific handler
                                if (Handlers.TryGetValue(command.Name, out Type? commandHandlerType))
                                {
                                    logger.LogInformation($"INCOMING ORC COMMAND \"{command.Name}\" FROM {host.Name}");
                                    handler = (IOrcCommandHandler?)serviceProvider.GetService(commandHandlerType);
                                    handler.CallContext = context;
                                    await handler.HandleAsync(host.Id, command, hostOnline.OutgoingQueue.Writer, context.CancellationToken);
                                }
                                else if (!string.IsNullOrWhiteSpace(command.SessionId))
                                {
                                    logger.LogInformation($"INCOMING SESSION COMMAND \"{command.Name}\" from \"{host.Name}\" on SessionID {command.SessionId}");
                                    await sessionHandler.HandleAsync(host.Id, command, hostOnline.OutgoingQueue.Writer, context.CancellationToken);
                                }
                                else if (!string.IsNullOrEmpty(command.RequestId)
                                    && hostOnline.RequestChannels.TryGetValue(command.RequestId, out var requestChannel))
                                {
                                    // Route per-request responses (e.g. ExecuteToolResponse from tools-only hosts)
                                    requestChannel.Writer.TryWrite(command);
                                }
                                else
                                {
                                    logger.LogWarning($"Unhandled command {command.Name}");
                                }
                            }
                            catch (Exception exc)
                            {
                                logger.LogError($"ERROR HANDLING \"{command.Name}\": {exc.GetBaseException().Message}");
                            }


                        }
                    }
                    catch (Exception exc)
                    {
                        logger.LogError($"ERROR PROCESSING INCOMING COMMANDS:{exc.GetBaseException().Message}");
                    }
                }
            });

            // Send messages to the Host (blocks asynchronously via ChannelReader)
            await hostOnline.SendOutgoingCommandsAsync(responseStream, context.CancellationToken);

            // Only unregister if this connection's hostOnline is still the current one.
            // If the host reconnected, a new HostOnline was created and we must not remove it.
            if (HostContainer.HostsOnline.TryGetValue(host.Id, out var currentOnline) && currentOnline == hostOnline)
            {
                await HostContainer.UnregisterHostAsync(host.Id, cosmo, Program.App.Configuration);
            }

        }
    }
}
