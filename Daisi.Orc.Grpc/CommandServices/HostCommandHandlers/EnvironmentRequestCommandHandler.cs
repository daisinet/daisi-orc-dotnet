using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using System.Collections.Concurrent;

namespace Daisi.Orc.Grpc.CommandServices.Handlers
{
    public class EnvironmentRequestCommandHandler(ILogger<EnvironmentRequestCommandHandler> logger, Cosmo cosmo, IConfiguration configuration) : OrcCommandHandlerBase
    {
        public override async Task HandleAsync(string hostId, Command command, ConcurrentQueue<Command> responseQueue, CancellationToken cancellationToken = default)
        {
            if (HostContainer.HostsOnline.TryGetValue(hostId, out var host))
            {
                var request = command.Payload.Unpack<EnvironmentRequest>();

                host.Host.OperatingSystem = request.OperatingSystem;
                host.Host.OperatingSystemVersion = request.OperatingSystemVersion;
                host.Host.AppVersion = request.AppVersion;

                await cosmo.PatchHostEnvironmentAsync(host.Host);

                HandleHostUpdaterCheck(responseQueue, host.Host, configuration);
            }
        }

        public static void HandleHostUpdaterCheck(ConcurrentQueue<Command> responseQueue, Core.Data.Models.Host host, IConfiguration configuration)
        {
            var isDesktop = host.OperatingSystem == "Windows"
                         || host.OperatingSystem == "MacCatalyst"
                         || host.OperatingSystem == "MacOS"
                         || host.OperatingSystem == "Linux";

            if (isDesktop)
            {
                string key = "Daisi:MinimumHostVersion";

                if (!string.IsNullOrWhiteSpace(host.ReleaseGroup))
                {
                    key += $"-{host.ReleaseGroup}";
                }
                
                var minimumHostVersion = configuration.GetValue<Version>(key);

                if (minimumHostVersion != null)
                {
                    // Desktop Update Check
                    var currentVersion = Version.Parse(host.AppVersion);

                    if (currentVersion < minimumHostVersion)
                    {
                        responseQueue.Enqueue(new Command()
                        {
                            Name = nameof(UpdateRequiredRequest),
                            Payload = Any.Pack(new UpdateRequiredRequest() { HostAppUrl = $"https://daisi.blob.core.windows.net/releases/latest-desktop.zip" })
                        });
                    }
                }

            }
            else
            {
                //Mobile Update Check

            }
        }
    }
}
