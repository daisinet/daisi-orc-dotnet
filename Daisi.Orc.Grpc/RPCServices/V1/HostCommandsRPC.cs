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
