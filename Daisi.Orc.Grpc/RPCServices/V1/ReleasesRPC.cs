using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    [Authorize]
    public class ReleasesRPC(ILogger<ReleasesRPC> logger, Cosmo cosmo) : ReleasesProto.ReleasesProtoBase
    {
        public override async Task<CreateReleaseResponse> Create(CreateReleaseRequest request, ServerCallContext context)
        {
            var release = new HostRelease
            {
                ReleaseGroup = request.ReleaseGroup,
                Version = request.Version,
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
