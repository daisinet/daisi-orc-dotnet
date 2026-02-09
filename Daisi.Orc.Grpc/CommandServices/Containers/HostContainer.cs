using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Orc.Grpc.Background;
using Daisi.Orc.Grpc.RPCServices.V1;
using Daisi.Protos.V1;
using Daisi.SDK.Extensions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
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
            outQueue.Enqueue(command);

            // Listen for the response for a certain amount of time.
            ResponseT? outResponse = await Task.Run<ResponseT?>(async () =>
            {
                ResponseT? response = default;
                var start = DateTime.UtcNow;
                bool run = true;

                while (run)
                {
                    if ((DateTime.UtcNow - start).TotalMilliseconds > millisecondsToWait)
                    {
                        run = false;
                        Program.App.Logger.LogError($"DAISI ORC: Timeout waiting for response from host \"{session.CreateResponse.Host.Name}\": {typeof(ResponseT).Name}");
                        return response;
                    }
                    try
                    {
                        if (inQueue.TryDequeue(out var inCommand))
                        {
                            if (inCommand is null || inCommand.RequestId != command.RequestId)
                                continue;

                            start = DateTime.UtcNow;

                            if (inCommand.Name == typeof(ResponseT).Name)
                            {
                                if (!inCommand.Payload.TryUnpack<ResponseT>(out response))
                                    Program.App.Logger.LogError($"DAISI: Could not unpack {inCommand.Name} payload to {typeof(ResponseT).Name}");

                                run = false;
                                return response;
                            }
                        }
                    }
                    catch (Exception exc)
                    {
                        Program.App.Logger.LogError($"SendToHostAndWaitAsync ERROR: {exc.Message}");
                        run = false;
                        return response;
                    }
                }
                run = false;

                return response;
            });

            return outResponse;


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
            outQueue.Enqueue(command);

            // Listen for the response for a certain amount of time.

            await Task.Run(async () =>
            {
                ResponseT? response = default;
                var start = DateTime.UtcNow;
                bool run = true;

                while (run && !cancellationToken.IsCancellationRequested)
                {
                    if ((DateTime.UtcNow - start).TotalMilliseconds > millisecondsToWaitBetweenResponses)
                    {
                        run = false;
                        Program.App.Logger.LogError($"DAISI ORC: Timeout waiting for response from host \"{session.CreateResponse.Host.Name}\": {typeof(ResponseT).Name}");
                        return Task.CompletedTask;
                    }
                    try
                    {
                        if (inQueue.TryDequeue(out var inCommand))
                        {
                            if (inCommand is null || inCommand.RequestId != command.RequestId)
                                continue;

                            start = DateTime.UtcNow;

                            if (inCommand.Name == typeof(ResponseT).Name)
                            {
                                if (inCommand.Payload.TryUnpack<ResponseT>(out response))
                                {
                                    await responseStream.WriteAsync(response);
                                }
                                else
                                    Program.App.Logger.LogError($"ORC Could not unpack {inCommand.Name} payload into {typeof(ResponseT).Name}");
                            }
                            else if (inCommand.Name == "ENDSTREAM")
                            {
                                //remove the END command from the queue.
                                run = false;
                                return Task.CompletedTask;
                            }
                        }
                    }
                    catch (Exception exc)
                    {
                        Program.App.Logger.LogError($"ORC SendToHostAndStreamAsync ERROR: {exc.Message}");
                        run = false;
                        return Task.CompletedTask;
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    Command cancelCommand = new()
                    {
                        Name = nameof(CancelStreamRequest),
                        Payload = Any.Pack(new CancelStreamRequest { }),
                        SessionId = session.Id,
                        RequestId = command.RequestId
                    };
                    outQueue.Enqueue(cancelCommand);

                    run = false;
                }

                return Task.CompletedTask;
            }, cancellationToken );



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
                    outQueue.Enqueue(command);
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

        public ConcurrentDictionary<string, ConcurrentQueue<Command>> SessionOutgoingQueues = new ConcurrentDictionary<string, ConcurrentQueue<Command>>();

        /// <summary>
        /// Key is the ID for the session that is sending the Orc commands.
        /// These come from consumers in a session.
        /// </summary>
        public ConcurrentDictionary<string, ConcurrentQueue<Command>> SessionIncomingQueues = new ConcurrentDictionary<string, ConcurrentQueue<Command>>();


        /// <summary>
        /// These are commands coming in from the Host.
        /// </summary>
        public ConcurrentQueue<Command> IncomingQueue { get; set; } = new ConcurrentQueue<Command>();

        /// <summary>
        /// These are commands going out to the Host.
        /// </summary>
        public ConcurrentQueue<Command> OutgoingQueue { get; set; } = new ConcurrentQueue<Command>();

        public void AddSession(DaisiSession session)
        {
            SessionOutgoingQueues.TryAdd(session.Id, new ConcurrentQueue<Command>());
            SessionIncomingQueues.TryAdd(session.Id, new ConcurrentQueue<Command>());
        }

        internal void CloseSession(string sessionId)
        {
            SessionOutgoingQueues.TryRemove(sessionId, out var removedOutQ);
            SessionIncomingQueues.TryRemove(sessionId, out var removedInQ);
        }

        internal async Task SendOutgoingCommandsAsync(IServerStreamWriter<Command> responseStream, CancellationToken cancellationToken)
        {
            //_ = Task.Run(async () =>
            //{
            if (OutgoingQueue.TryDequeue(out var hostCommand))
            {
                logger.LogInformation($"ORC COMMAND {hostCommand.Name} to {Host.Name}");
                await responseStream.WriteAsync(hostCommand, cancellationToken);
            }
            //});

            //_ = Task.Run(async () =>
            //{
            foreach (var sessionOut in SessionOutgoingQueues)
            {
                if (sessionOut.Value.TryDequeue(out var sessionCommand))
                {
                    logger.LogInformation($"SESSION COMMAND {sessionCommand.Name} SENT OUT TO {Host.Name}:{sessionCommand.SessionId}");
                    await responseStream.WriteAsync(sessionCommand, cancellationToken);
                }
            }
            //});
        }
    }
}