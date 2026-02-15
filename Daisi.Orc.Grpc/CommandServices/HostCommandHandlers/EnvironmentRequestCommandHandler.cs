using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using System.Threading.Channels;

namespace Daisi.Orc.Grpc.CommandServices.Handlers
{
    public class EnvironmentRequestCommandHandler(ILogger<EnvironmentRequestCommandHandler> logger, Cosmo cosmo) : OrcCommandHandlerBase
    {
        public override async Task HandleAsync(string hostId, Command command, ChannelWriter<Command> responseQueue, CancellationToken cancellationToken = default)
        {
            if (HostContainer.HostsOnline.TryGetValue(hostId, out var host))
            {
                var request = command.Payload.Unpack<EnvironmentRequest>();

                host.Host.OperatingSystem = request.OperatingSystem;
                host.Host.OperatingSystemVersion = request.OperatingSystemVersion;
                host.Host.AppVersion = NormalizeVersion(request.AppVersion);

                await cosmo.PatchHostEnvironmentAsync(host.Host);

                await HandleHostUpdaterCheckAsync(responseQueue, host.Host, cosmo, logger);
            }
        }

        public static async Task HandleHostUpdaterCheckAsync(ChannelWriter<Command> responseQueue, Core.Data.Models.Host host, Cosmo cosmo, ILogger logger)
        {
            var isDesktop = host.OperatingSystem == "Windows"
                         || host.OperatingSystem == "MacCatalyst"
                         || host.OperatingSystem == "MacOS"
                         || host.OperatingSystem == "Linux";

            if (!isDesktop)
                return;

            var releaseGroup = host.ReleaseGroup ?? "production";

            var activeRelease = await cosmo.GetActiveReleaseAsync(releaseGroup);

            // Production releases act as a version floor for all groups.
            // If the host is in a non-production group, also check the production
            // release and use whichever has the higher version.
            var chosenRelease = activeRelease;
            var chosenChannel = releaseGroup;

            if (releaseGroup != "production")
            {
                var productionRelease = await cosmo.GetActiveReleaseAsync("production");

                if (productionRelease != null)
                {
                    if (chosenRelease == null)
                    {
                        chosenRelease = productionRelease;
                        chosenChannel = "production";
                    }
                    else
                    {
                        var groupVersion = Version.Parse(chosenRelease.Version);
                        var prodVersion = Version.Parse(productionRelease.Version);

                        if (prodVersion > groupVersion)
                        {
                            chosenRelease = productionRelease;
                            chosenChannel = "production";
                        }
                    }
                }
            }

            if (chosenRelease == null)
                return;

            var currentVersion = Version.Parse(host.AppVersion);
            var releaseVersion = Version.Parse(chosenRelease.Version);

            if (currentVersion < releaseVersion)
            {
                responseQueue.TryWrite(new Command()
                {
                    Name = nameof(UpdateRequiredRequest),
                    Payload = Any.Pack(new UpdateRequiredRequest()
                    {
                        Channel = chosenChannel
                    })
                });
            }
        }

        /// <summary>
        /// Normalizes a version string by parsing and re-serializing it,
        /// which strips leading zeros (e.g. "2026.02.13.1941" → "2026.2.13.1941").
        /// </summary>
        public static string NormalizeVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return version;

            if (Version.TryParse(version, out var parsed))
                return parsed.ToString();

            return version;
        }
    }
}
