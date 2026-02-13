using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using System.Threading.Channels;

namespace Daisi.Orc.Grpc.CommandServices.Handlers
{
    public class EnvironmentRequestCommandHandler(ILogger<EnvironmentRequestCommandHandler> logger, Cosmo cosmo, IConfiguration configuration) : OrcCommandHandlerBase
    {
        public override async Task HandleAsync(string hostId, Command command, ChannelWriter<Command> responseQueue, CancellationToken cancellationToken = default)
        {
            if (HostContainer.HostsOnline.TryGetValue(hostId, out var host))
            {
                var request = command.Payload.Unpack<EnvironmentRequest>();

                host.Host.OperatingSystem = request.OperatingSystem;
                host.Host.OperatingSystemVersion = request.OperatingSystemVersion;
                host.Host.AppVersion = request.AppVersion;

                await cosmo.PatchHostEnvironmentAsync(host.Host);

                await HandleHostUpdaterCheckAsync(responseQueue, host.Host, cosmo, configuration, logger);
            }
        }

        public static async Task HandleHostUpdaterCheckAsync(ChannelWriter<Command> responseQueue, Core.Data.Models.Host host, Cosmo cosmo, IConfiguration configuration, ILogger logger)
        {
            var isDesktop = host.OperatingSystem == "Windows"
                         || host.OperatingSystem == "MacCatalyst"
                         || host.OperatingSystem == "MacOS"
                         || host.OperatingSystem == "Linux";

            if (isDesktop)
            {
                var releaseGroup = host.ReleaseGroup ?? "production";

                try
                {
                    var activeRelease = await cosmo.GetActiveReleaseAsync(releaseGroup);

                    if (activeRelease != null)
                    {
                        var currentVersion = Version.Parse(host.AppVersion);
                        var releaseVersion = Version.Parse(activeRelease.Version);

                        if (currentVersion < releaseVersion)
                        {
                            var rid = MapOperatingSystemToRid(host.OperatingSystem);
                            var hostAppUrl = $"{activeRelease.DownloadUrl}/{rid}/DaisiHost-latest.zip";

                            // Send both Channel (for Velopack hosts) and HostAppUrl (for legacy hosts).
                            // New hosts check Channel first; legacy hosts read HostAppUrl.
                            responseQueue.TryWrite(new Command()
                            {
                                Name = nameof(UpdateRequiredRequest),
                                Payload = Any.Pack(new UpdateRequiredRequest()
                                {
                                    Channel = releaseGroup,
                                    HostAppUrl = hostAppUrl
                                })
                            });
                        }

                        return;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to check DB release for group {Group}, falling back to config", releaseGroup);
                }

                // Fallback to config-based check for backward compatibility
                string key = "Daisi:MinimumHostVersion";

                if (!string.IsNullOrWhiteSpace(host.ReleaseGroup))
                {
                    key += $"-{host.ReleaseGroup}";
                }

                var minimumHostVersion = configuration.GetValue<Version>(key);

                if (minimumHostVersion != null)
                {
                    var currentVersion = Version.Parse(host.AppVersion);

                    if (currentVersion < minimumHostVersion)
                    {
                        responseQueue.TryWrite(new Command()
                        {
                            Name = nameof(UpdateRequiredRequest),
                            Payload = Any.Pack(new UpdateRequiredRequest() { Channel = host.ReleaseGroup ?? "production" })
                        });
                    }
                }
            }
            else
            {
                //Mobile Update Check

            }
        }

        private static string MapOperatingSystemToRid(string operatingSystem)
        {
            return operatingSystem switch
            {
                "Windows" => "win-x64",
                "Linux" => "linux-x64",
                "MacOS" or "MacCatalyst" => "osx-x64",
                _ => "win-x64"
            };
        }
    }
}
