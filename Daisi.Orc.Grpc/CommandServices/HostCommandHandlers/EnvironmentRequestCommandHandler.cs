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

            // Always start with the production release as the baseline for all hosts.
            var productionRelease = await cosmo.GetActiveReleaseAsync("production");
            var chosenRelease = productionRelease;
            var chosenChannel = "production";

            // If the host belongs to a non-production group, check that group too
            // and pick whichever release has the higher version.
            var releaseGroup = host.ReleaseGroup;
            if (!string.IsNullOrEmpty(releaseGroup) && releaseGroup != "production")
            {
                var groupRelease = await cosmo.GetActiveReleaseAsync(releaseGroup);
                if (groupRelease != null && Version.TryParse(groupRelease.Version, out var groupVersion))
                {
                    if (chosenRelease == null || !Version.TryParse(chosenRelease.Version, out var prodVersion) || groupVersion > prodVersion)
                    {
                        chosenRelease = groupRelease;
                        chosenChannel = releaseGroup;
                    }
                }
            }

            if (chosenRelease == null || !Version.TryParse(chosenRelease.Version, out var releaseVersion))
                return;

            // If the host has no version yet, it always needs the update
            if (!Version.TryParse(host.AppVersion, out var currentVersion) || currentVersion < releaseVersion)
            {
                logger.LogInformation("Update required for {Host}: {Current} → {Release} ({Channel})",
                    host.Name, host.AppVersion ?? "unknown", chosenRelease.Version, chosenChannel);

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
