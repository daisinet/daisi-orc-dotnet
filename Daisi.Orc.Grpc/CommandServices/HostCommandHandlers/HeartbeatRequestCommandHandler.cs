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
    public class HeartbeatRequestCommandHandler(Cosmo cosmo, OrcService orcService, ILogger<HeartbeatRequestCommandHandler> logger, IConfiguration configuration) : OrcCommandHandlerBase
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

            await cosmo.PatchHostForHeartbeatAsync(hostOnline.Host);

            string clientKey = CallContext.GetClientKey()!;
            var key = await cosmo.GetKeyAsync(clientKey, KeyTypes.Client);
            await cosmo.SetKeyTTLAsync(key, 30);

            responseQueue.TryWrite(new Command()
            {
                Name = nameof(HeartbeatRequest)                
            });

            EnvironmentRequestCommandHandler.HandleHostUpdaterCheck(responseQueue, hostOnline.Host, configuration);

            logger.LogInformation($"Handled Heartbeat for {hostOnline.Host.Name} at {DateTime.UtcNow} from IP {ip}");
        }

    
    }
}
