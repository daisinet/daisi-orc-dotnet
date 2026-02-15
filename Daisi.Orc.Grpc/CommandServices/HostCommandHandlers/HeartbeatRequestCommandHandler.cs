using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Orc.Grpc.CommandServices.Interfaces;
using Daisi.Protos.V1;
using Daisi.SDK.Interfaces;
using Daisi.SDK.Models;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Daisi.Orc.Grpc.CommandServices.Handlers
{
    /// <summary>
    /// Handles the commands received from Host to log the heartbeat.
    /// </summary>
    public class HeartbeatRequestCommandHandler(Cosmo cosmo, OrcService orcService, ILogger<HeartbeatRequestCommandHandler> logger) : OrcCommandHandlerBase
    {
        public override async Task HandleAsync(string hostId, Command command, ChannelWriter<Command> responseQueue, CancellationToken cancellationToken = default)
        {
            
            if (!HostContainer.HostsOnline.TryGetValue(hostId, out var hostOnline))
            {
                logger.LogInformation($"DAISI: Could not find Host ID {hostId}");
                return;
            }

            var request = command.Payload.Unpack<HeartbeatRequest>();

            hostOnline.Host.DateLastHeartbeat = DateTime.UtcNow;
            var ip = CallContext.GetRemoteIpAddress() ?? string.Empty;
            if(!string.IsNullOrWhiteSpace(ip))
                hostOnline.Host.IpAddress = ip;
            hostOnline.Host.Status = HostStatus.Online;
            hostOnline.Host.ConnectedOrc = await orcService.GetHostOrcAsync();

            var dbHost = await cosmo.PatchHostForHeartbeatAsync(hostOnline.Host);

            // Sync fields that may have changed in the DB (e.g. ReleaseGroup updated via Manager UI)
            hostOnline.Host.ReleaseGroup = dbHost.ReleaseGroup;
            hostOnline.Host.AppVersion = dbHost.AppVersion;

            // Use cached client key ID from HostOnline to extend TTL with a single patch
            // instead of GetKeyAsync + full document upsert (saves 1 read + reduces write cost)
            if (!string.IsNullOrEmpty(hostOnline.ClientKeyId))
            {
                await cosmo.PatchKeyExpirationAsync(hostOnline.ClientKeyId, DateTime.UtcNow.AddMinutes(30));
            }
            else
            {
                // Fallback for connections established before ClientKeyId was cached
                string clientKey = CallContext.GetClientKey()!;
                var key = await cosmo.GetKeyAsync(clientKey, KeyTypes.Client);
                await cosmo.SetKeyTTLAsync(key, 30);
                hostOnline.ClientKeyId = key.Id;
            }

            responseQueue.TryWrite(new Command()
            {
                Name = nameof(HeartbeatRequest)                
            });

            await EnvironmentRequestCommandHandler.HandleHostUpdaterCheckAsync(responseQueue, hostOnline.Host, cosmo, logger);

            logger.LogInformation($"Handled Heartbeat for {hostOnline.Host.Name} at {DateTime.UtcNow} from IP {ip}");
        }

    
    }
}
