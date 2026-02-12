using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Orc.Grpc.Background;
using Daisi.Orc.Grpc.RPCServices.V1;
using Daisi.Protos.V1;
using Daisi.SDK.Clients.V1;
using Daisi.SDK.Extensions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading.Channels;
using static Azure.Core.HttpHeader;

namespace Daisi.Orc.Grpc.CommandServices.Containers
{
    public class HostContainer
    {
        /// <summary>
        /// Dictionary of all of the Hosts that are online based on the Client Key.
        /// </summary>
        public static ConcurrentDictionary<string, HostOnline> HostsOnline = new ConcurrentDictionary<string, HostOnline>();


        public async static Task<Core.Data.Models.Host> RegisterHostAsync(string hostId, Cosmo cosmo, ServerCallContext context, IConfiguration configuration)
        {
            if (HostsOnline.ContainsKey(hostId))
            {
                await UnregisterHostAsync(hostId, cosmo, configuration);
            }          

            var host = await cosmo.GetHostAsync(hostId);

            if (host is not null)
            {
                HostsOnline.TryAdd(hostId, new HostOnline(Program.App.Logger) { Host = host });

                host.DateStarted = DateTime.UtcNow;
                host.DateStopped = null;
                host.Status = HostStatus.Online;
                host.IpAddress = context.GetRemoteIpAddress() ?? string.Empty;

                await cosmo.PatchHostForConnectionAsync(host);

                await UpdateOrcConnectionCountInDb(cosmo, configuration);

                // Notify File Manager that host came online
                await NotifyDriveHostOnlineAsync(hostId, host.AccountId, configuration);

                Program.App.Logger.LogCritical($"Registered Host {host.Name}");
            }

            return host;
        }

        private static async Task UpdateOrcConnectionCountInDb(Cosmo cosmo, IConfiguration configuration)
        {
            var orcId = configuration.GetValue<string>("Daisi:OrcId")!;
            var accountId = configuration.GetValue<string>("Daisi:AccountId")!;
            await cosmo.PatchOrcConnectionCountAsync(orcId, HostsOnline.Count, accountId);
        }

        public async static Task UnregisterHostAsync(string hostId, Cosmo cosmo, IConfiguration configuration)
        {
            var host = await cosmo.GetHostAsync(hostId);
            await UnregisterHostAsync(host, cosmo, configuration);
        }
        public async static Task UnregisterHostAsync(Core.Data.Models.Host host, Cosmo cosmo, IConfiguration configuration)
        {
            if (host is not null)
            {
                if (HostsOnline.TryRemove(host.Id, out var hostToRemove))
                {
                    foreach (var sessionId in hostToRemove.SessionIncomingQueues.Keys)
                    {
                        SessionContainer.Close(sessionId);
                    }

                    // Award partial uptime credits before going offline
                    try
                    {
                        var creditService = Program.App.Services.CreateScope()
                            .ServiceProvider.GetRequiredService<CreditService>();
                        await UptimeCreditService.AwardPartialUptimeCreditsAsync(
                            host.Id, host.AccountId, creditService, Program.App.Logger);
                    }
                    catch (Exception ex)
                    {
                        Program.App.Logger.LogError(ex, $"Error awarding partial uptime credits for host {host.Name}");
                    }

                    host.DateStopped = DateTime.UtcNow;
                    host.Status = HostStatus.Offline;
                    host.ConnectedOrc = null;
                    await cosmo.PatchHostForConnectionAsync(host);

                    await UpdateOrcConnectionCountInDb(cosmo, configuration);

                    // Notify File Manager that host went offline
                    await NotifyDriveHostOfflineAsync(host.Id, host.AccountId, configuration);

                    Program.App.Logger.LogCritical($"Unregistered Host {host.Name}");
                }
            }

        }

        public static Protos.V1.Host GetNextHost(CreateSessionRequest request, ServerCallContext context, Cosmo cosmo)
        {
            var accountId = context.GetAccountId();

            var q = HostsOnline.Values.Where(h =>
                   (string.IsNullOrEmpty(request.HostId) || (accountId is not null && h.Host.AccountId == accountId && h.Host.Id == request.HostId))
                //&& (request.NetworkName is null || (accountId is not null && h.Host.AccountId == accountId && (h.Host.ConnectedOrc?.Networks.Any(n=>n.Name == request.NetworkName) ?? false)))
                && (!request.PreferredHostNames.Any() || request.PreferredHostNames.Contains(h.Host.Name))
                && (!request.DirectConnectRequired || h.Host.DirectConnect)
                && (request.PreferredRegion is null || h.Host.Region == System.Enum.Parse<HostRegions>(request.PreferredRegion))
            );

            if (!q.Any())
            {
                // TODO: Find a host in the DB that meets the requirements

                throw new Exception("DAISI: No host is online meeting specified requirements.");
            }

            var hostOnline = q.OrderBy(h => h.Host.DateLastSession).First();

            hostOnline.Host.DateLastSession = DateTime.UtcNow;

            return new Protos.V1.Host()
            {
                Id = hostOnline.Host.Id,
                Name = hostOnline.Host.Name,
                IpAddress = hostOnline.Host.IpAddress,
                Port = hostOnline.Host.Port,
                DirectConnect = hostOnline.Host.DirectConnect,
            };
        }

        public static async Task<ResponseT> SendToHostAndWaitAsync<RequestT, ResponseT>(DaisiSession session, RequestT request, int millisecondsToWait = 10000)
            where RequestT : IMessage<RequestT>, new()
            where ResponseT : IMessage<ResponseT>, new()
        {
            if (!HostsOnline.TryGetValue(session.CreateResponse.Host.Id, out var hostOnline))
                throw new Exception($"DAISI: Host isn't online.");

            if (!hostOnline.SessionOutgoingQueues.TryGetValue(session.Id, out var outQueue))
                throw new Exception($"DAISI: Could not find host's outgoing command queue for {session.Id}");

            if (!hostOnline.SessionIncomingQueues.TryGetValue(session.Id, out var inQueue))
                throw new Exception($"DAISI: Could not find host's incoming command queue for {session.Id}");

            string id = $"req-{StringExtensions.Random()}";
            Program.App.Logger.LogInformation($"Enqueued request {typeof(RequestT).Name} with ReqID {id} to {session?.CreateResponse?.Host?.Name}");

            Command command = new()
            {
                Name = typeof(RequestT).Name,
                Payload = Any.Pack(request),
                SessionId = session.Id,
                RequestId = id
            };
            outQueue.Writer.TryWrite(command);

            // Listen for the response using ChannelReader with timeout.
            using var cts = new CancellationTokenSource(millisecondsToWait);

            try
            {
                await foreach (var inCommand in inQueue.Reader.ReadAllAsync(cts.Token))
                {
                    if (inCommand is null || inCommand.RequestId != command.RequestId)
                        continue;

                    cts.CancelAfter(millisecondsToWait); // Reset timeout on each relevant message

                    if (inCommand.Name == typeof(ResponseT).Name)
                    {
                        if (!inCommand.Payload.TryUnpack<ResponseT>(out var response))
                            Program.App.Logger.LogError($"DAISI: Could not unpack {inCommand.Name} payload to {typeof(ResponseT).Name}");

                        return response;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Program.App.Logger.LogError($"DAISI ORC: Timeout waiting for response from host \"{session.CreateResponse.Host.Name}\": {typeof(ResponseT).Name}");
            }
            catch (Exception exc)
            {
                Program.App.Logger.LogError($"SendToHostAndWaitAsync ERROR: {exc.Message}");
            }

            return default;
        }

        internal static async Task SendToHostAndStreamAsync<RequestT, ResponseT>(DaisiSession session, RequestT request, IServerStreamWriter<ResponseT> responseStream, CancellationToken cancellationToken = default, int millisecondsToWaitBetweenResponses = 60000)
            where RequestT : IMessage<RequestT>, new()
            where ResponseT : IMessage<ResponseT>, new()
        {
            if (!HostsOnline.TryGetValue(session.CreateResponse.Host.Id, out var hostOnline))
                throw new Exception($"DAISI: Host isn't online.");

            if (!hostOnline.SessionOutgoingQueues.TryGetValue(session.Id, out var outQueue))
                throw new Exception($"DAISI: Could not find host's outgoing command queue for {session.Id}");

            if (!hostOnline.SessionIncomingQueues.TryGetValue(session.Id, out var inQueue))
                throw new Exception($"DAISI: Could not find host's incoming command queue for {session.Id}");

            // Queue the HostManager to send the command to the Host
            Command command = new()
            {
                Name = typeof(RequestT).Name,
                Payload = Any.Pack(request),
                SessionId = session.Id,
                RequestId = $"req-{StringExtensions.Random()}"
            };
            outQueue.Writer.TryWrite(command);

            // Listen for the response using ChannelReader with timeout.
            using var timeoutCts = new CancellationTokenSource(millisecondsToWaitBetweenResponses);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await foreach (var inCommand in inQueue.Reader.ReadAllAsync(linkedCts.Token))
                {
                    if (inCommand is null || inCommand.RequestId != command.RequestId)
                        continue;

                    timeoutCts.CancelAfter(millisecondsToWaitBetweenResponses); // Reset timeout on each relevant message

                    if (inCommand.Name == typeof(ResponseT).Name)
                    {
                        if (inCommand.Payload.TryUnpack<ResponseT>(out var response))
                        {
                            await responseStream.WriteAsync(response);
                        }
                        else
                            Program.App.Logger.LogError($"ORC Could not unpack {inCommand.Name} payload into {typeof(ResponseT).Name}");
                    }
                    else if (inCommand.Name == "ENDSTREAM")
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Command cancelCommand = new()
                {
                    Name = nameof(CancelStreamRequest),
                    Payload = Any.Pack(new CancelStreamRequest { }),
                    SessionId = session.Id,
                    RequestId = command.RequestId
                };
                outQueue.Writer.TryWrite(cancelCommand);
            }
            catch (OperationCanceledException)
            {
                Program.App.Logger.LogError($"DAISI ORC: Timeout waiting for response from host \"{session.CreateResponse.Host.Name}\": {typeof(ResponseT).Name}");
            }
            catch (Exception exc)
            {
                Program.App.Logger.LogError($"ORC SendToHostAndStreamAsync ERROR: {exc.Message}");
            }
        }

        internal static async Task UnregisterAllAsync(Cosmo cosmo, IConfiguration configuration)
        {
            for (int i = 0; i < HostsOnline.Count; i++)
            {
                var host = HostsOnline.Values.ElementAt(i);
                await UnregisterHostAsync(host.Host.Id, cosmo, configuration);
                i--;
            }
        }

        internal static async Task UpdateConfigurableWebSettingsAsync(Core.Data.Models.Host host)
        {
            if (HostsOnline.ContainsKey(host.Id))
            {
                var inmem = HostsOnline[host.Id].Host;
                inmem.PeerConnect = host.PeerConnect;
                inmem.DirectConnect = host.DirectConnect;
                inmem.Name = host.Name;
            }
        }

        private static async Task NotifyDriveHostOfflineAsync(string hostId, string accountId, IConfiguration configuration)
        {
            try
            {
                var fileManagerUrl = configuration.GetValue<string>("Daisi:FileManagerUrl");
                if (string.IsNullOrEmpty(fileManagerUrl)) return;

                var client = new DriveNotificationClient(fileManagerUrl);
                await client.HostWentOfflineAsync(new HostOfflineNotification
                {
                    HostId = hostId,
                    AccountId = accountId
                });
            }
            catch (Exception ex)
            {
                Program.App.Logger.LogError(ex, "Failed to notify File Manager of host offline: {HostId}", hostId);
            }
        }

        private static async Task NotifyDriveHostOnlineAsync(string hostId, string accountId, IConfiguration configuration)
        {
            try
            {
                var fileManagerUrl = configuration.GetValue<string>("Daisi:FileManagerUrl");
                if (string.IsNullOrEmpty(fileManagerUrl)) return;

                var client = new DriveNotificationClient(fileManagerUrl);
                await client.HostCameOnlineAsync(new HostOnlineNotification
                {
                    HostId = hostId,
                    AccountId = accountId
                });
            }
            catch (Exception ex)
            {
                Program.App.Logger.LogError(ex, "Failed to notify File Manager of host online: {HostId}", hostId);
            }
        }

        internal static void SendSessionCloseRequest(string id, string sessionId)
        {
            if (HostsOnline.TryGetValue(id, out var hostOnline))
            {
                if (hostOnline.SessionOutgoingQueues.TryGetValue(sessionId, out var outQueue))
                {
                    Command command = new()
                    {
                        Name = nameof(CloseSessionRequest),
                        Payload = Any.Pack(new CloseSessionRequest() { Id = sessionId }),
                        SessionId = sessionId
                    };
                    outQueue.Writer.TryWrite(command);
                }
            }
        }

    }

    public class HostOnline(ILogger logger)
    {
        /// <summary>
        /// This is the Host that is online.
        /// </summary>
        public Core.Data.Models.Host Host { get; set; }

        /// <summary>
        /// Key is the ID for the session that will receive these outgoing commands.
        /// These going to consumers in a session.
        /// </summary>
        public ConcurrentDictionary<string, Channel<Command>> SessionOutgoingQueues = new();

        /// <summary>
        /// Key is the ID for the session that is sending the Orc commands.
        /// These come from consumers in a session.
        /// </summary>
        public ConcurrentDictionary<string, Channel<Command>> SessionIncomingQueues = new();

        /// <summary>
        /// These are commands coming in from the Host.
        /// </summary>
        public ConcurrentQueue<Command> IncomingQueue { get; set; } = new ConcurrentQueue<Command>();

        /// <summary>
        /// These are commands going out to the Host.
        /// </summary>
        public Channel<Command> OutgoingQueue { get; set; } = Channel.CreateUnbounded<Command>();

        public void AddSession(DaisiSession session)
        {
            SessionOutgoingQueues.TryAdd(session.Id, Channel.CreateUnbounded<Command>());
            SessionIncomingQueues.TryAdd(session.Id, Channel.CreateUnbounded<Command>());
        }

        internal void CloseSession(string sessionId)
        {
            if (SessionOutgoingQueues.TryRemove(sessionId, out var removedOutQ))
                removedOutQ.Writer.Complete();
            if (SessionIncomingQueues.TryRemove(sessionId, out var removedInQ))
                removedInQ.Writer.Complete();
        }

        internal async Task SendOutgoingCommandsAsync(IServerStreamWriter<Command> responseStream, CancellationToken cancellationToken)
        {
            // Merge all outgoing channels into a single stream to the host.
            // Use ChannelReader.ReadAllAsync to await commands without polling.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Track which sessions already have forwarding tasks
            var forwardedSessions = new ConcurrentDictionary<string, bool>();

            void StartForwardingTask(string sessionId, Channel<Command> sessionChannel)
            {
                if (!forwardedSessions.TryAdd(sessionId, true))
                    return;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var cmd in sessionChannel.Reader.ReadAllAsync(cts.Token))
                        {
                            OutgoingQueue.Writer.TryWrite(cmd);
                        }
                    }
                    catch (OperationCanceledException) { }
                }, cts.Token);
            }

            // Start forwarding tasks for existing sessions
            foreach (var sessionOut in SessionOutgoingQueues)
            {
                StartForwardingTask(sessionOut.Key, sessionOut.Value);
            }

            // Monitor for new sessions and start forwarding tasks for them
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        foreach (var sessionOut in SessionOutgoingQueues)
                        {
                            StartForwardingTask(sessionOut.Key, sessionOut.Value);
                        }
                        await Task.Delay(50, cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
            }, cts.Token);

            // Read from the merged outgoing channel and send to host
            try
            {
                await foreach (var command in OutgoingQueue.Reader.ReadAllAsync(cts.Token))
                {
                    logger.LogInformation($"COMMAND {command.Name} SENT OUT TO {Host.Name}:{command.SessionId}");
                    await responseStream.WriteAsync(command);
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}