using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.CommandServices.Handlers;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    [Authorize]
    public class ReleasesRPC(ILogger<ReleasesRPC> logger, Cosmo cosmo, GitHubReleaseService gitHubReleaseService) : ReleasesProto.ReleasesProtoBase
    {
        public override async Task<CreateReleaseResponse> Create(CreateReleaseRequest request, ServerCallContext context)
        {
            var release = new HostRelease
            {
                ReleaseGroup = request.ReleaseGroup,
                Version = EnvironmentRequestCommandHandler.NormalizeVersion(request.Version),
                DownloadUrl = request.DownloadUrl,
                ReleaseNotes = request.ReleaseNotes,
                RequiredOrcVersion = request.RequiredOrcVersion
            };

            release = await cosmo.CreateReleaseAsync(release);

            if (request.Activate)
            {
                release = await cosmo.ActivateReleaseAsync(release.Id, release.ReleaseGroup);
            }

            logger.LogInformation("Created release {ReleaseId} for group {Group} version {Version}", release.Id, release.ReleaseGroup, release.Version);

            return new CreateReleaseResponse
            {
                Release = MapToProto(release)
            };
        }

        public override async Task<GetReleasesResponse> GetReleases(GetReleasesRequest request, ServerCallContext context)
        {
            var releases = await cosmo.GetReleasesAsync(request.ReleaseGroup);

            var response = new GetReleasesResponse();
            foreach (var release in releases)
            {
                response.Releases.Add(MapToProto(release));
            }

            return response;
        }

        public override async Task<GetActiveReleaseResponse> GetActiveRelease(GetActiveReleaseRequest request, ServerCallContext context)
        {
            var release = await cosmo.GetActiveReleaseAsync(request.ReleaseGroup);

            return new GetActiveReleaseResponse
            {
                Release = release != null ? MapToProto(release) : null
            };
        }

        public override async Task<ActivateReleaseResponse> Activate(ActivateReleaseRequest request, ServerCallContext context)
        {
            var release = await cosmo.ActivateReleaseAsync(request.ReleaseId, request.ReleaseGroup);

            logger.LogInformation("Activated release {ReleaseId} for group {Group}", release.Id, release.ReleaseGroup);

            return new ActivateReleaseResponse
            {
                Release = MapToProto(release)
            };
        }

        public override async Task<TriggerReleaseResponse> TriggerRelease(TriggerReleaseRequest request, ServerCallContext context)
        {
            var version = EnvironmentRequestCommandHandler.NormalizeVersion(DateTime.UtcNow.ToString("yyyy.MM.dd.HHmm"));

            logger.LogInformation("TriggerRelease requested: version={Version}, group={Group}, activate={Activate}",
                version, request.ReleaseGroup, request.Activate);

            try
            {
                var success = await gitHubReleaseService.TriggerOrchestrateReleaseAsync(
                    version,
                    request.ReleaseGroup,
                    request.ReleaseNotes,
                    request.Activate);

                if (!success)
                {
                    return new TriggerReleaseResponse
                    {
                        Success = false,
                        Version = version,
                        ErrorMessage = "Failed to dispatch the release workflow. Check ORC logs for details."
                    };
                }

                return new TriggerReleaseResponse
                {
                    Success = true,
                    Version = version
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error triggering release workflow for version {Version}", version);

                return new TriggerReleaseResponse
                {
                    Success = false,
                    Version = version,
                    ErrorMessage = $"Error triggering release: {ex.Message}"
                };
            }
        }

        private static HostReleaseInfo MapToProto(HostRelease release)
        {
            return new HostReleaseInfo
            {
                Id = release.Id,
                ReleaseGroup = release.ReleaseGroup,
                Version = release.Version,
                DownloadUrl = release.DownloadUrl,
                IsActive = release.IsActive,
                ReleaseNotes = release.ReleaseNotes,
                RequiredOrcVersion = release.RequiredOrcVersion,
                CreatedBy = release.CreatedBy,
                DateCreated = Timestamp.FromDateTime(DateTime.SpecifyKind(release.DateCreated, DateTimeKind.Utc))
            };
        }
    }
}
