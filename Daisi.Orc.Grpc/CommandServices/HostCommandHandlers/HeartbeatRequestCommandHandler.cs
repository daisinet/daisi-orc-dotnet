using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Orc.Grpc.CommandServices.Interfaces;
using Daisi.Orc.Grpc.RPCServices.V1;
using Daisi.Protos.V1;
using Daisi.SDK.Interfaces;
using Daisi.SDK.Models;
using Google.Protobuf.WellKnownTypes;
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

            HeartbeatRequest? request = null;
            if (command.Payload != null && !string.IsNullOrEmpty(command.Payload.TypeUrl))
            {
                request = command.Payload.Unpack<HeartbeatRequest>();
            }

            // Capture loaded model names from heartbeat settings
            if (request?.Settings?.Model?.Models is { Count: > 0 } models)
            {
                hostOnline.LoadedModelNames = models.Select(m => m.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();
            }

            hostOnline.Host.DateLastHeartbeat = DateTime.UtcNow;
            var ip = CallContext.GetRemoteIpAddress() ?? string.Empty;
            if(!string.IsNullOrWhiteSpace(ip))
                hostOnline.Host.IpAddress = ip;
            hostOnline.Host.Status = HostStatus.Online;
            hostOnline.Host.ConnectedOrc = await orcService.GetHostOrcAsync();

            var dbHost = await cosmo.PatchHostForHeartbeatAsync(hostOnline.Host);

            // Sync fields that may have changed in the DB (e.g. ReleaseGroup updated via Manager UI).
            // Only overwrite with non-null DB values to avoid clearing data set by EnvironmentRequest.
            hostOnline.Host.ReleaseGroup = dbHost.ReleaseGroup;
            if (!string.IsNullOrEmpty(dbHost.AppVersion))
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

            try
            {
                await EnvironmentRequestCommandHandler.HandleHostUpdaterCheckAsync(responseQueue, hostOnline.Host, cosmo, logger);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Update check failed for host {HostName} (OS={OS}, Version={Version})",
                    hostOnline.Host.Name, hostOnline.Host.OperatingSystem, hostOnline.Host.AppVersion);
            }

            logger.LogInformation($"Handled Heartbeat for {hostOnline.Host.Name} at {DateTime.UtcNow} from IP {ip}");

            // Model sync: send DownloadModelRequest for any required models the host doesn't have yet
            // Browser hosts manage their own models — skip server-driven model sync.
            if (hostOnline.Host.OperatingSystem != "Browser")
            {
                try
                {
                    await SyncRequiredModelsAsync(hostOnline, responseQueue);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Model sync failed for host {HostName}", hostOnline.Host.Name);
                }
            }
        }

        /// <summary>
        /// Compares the ORC's enabled models with what the host reports as loaded,
        /// and sends DownloadModelRequest for any missing models.
        /// </summary>
        private async Task SyncRequiredModelsAsync(HostOnline hostOnline, ChannelWriter<Command> responseQueue)
        {
            var enabledModels = await cosmo.GetEnabledModelsAsync();
            var loadedNames = new HashSet<string>(hostOnline.LoadedModelNames, StringComparer.OrdinalIgnoreCase);

            // Clear pending downloads for models that have now loaded
            hostOnline.PendingModelDownloads.RemoveWhere(name => loadedNames.Contains(name));

            foreach (var dbModel in enabledModels)
            {
                if (loadedNames.Contains(dbModel.Name))
                    continue;

                if (hostOnline.PendingModelDownloads.Contains(dbModel.Name))
                    continue;

                var protoModel = dbModel.ConvertToProto();
                var downloadRequest = new DownloadModelRequest { Model = protoModel };

                responseQueue.TryWrite(new Command
                {
                    Name = nameof(DownloadModelRequest),
                    Payload = Any.Pack(downloadRequest)
                });

                hostOnline.PendingModelDownloads.Add(dbModel.Name);
                logger.LogInformation("Sent DownloadModelRequest for '{ModelName}' to host '{HostName}'",
                    dbModel.Name, hostOnline.Host.Name);
            }
        }
    }
}
