using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string ReleaseIdPrefix = "rel";
        public const string ReleasesContainerName = "Releases";
        public const string ReleasesPartitionKeyName = nameof(HostRelease.ReleaseGroup);

        public async Task<HostRelease> CreateReleaseAsync(HostRelease release)
        {
            release.DateCreated = DateTime.UtcNow;

            var container = await GetContainerAsync(ReleasesContainerName);
            var item = await container.CreateItemAsync(release, new PartitionKey(release.ReleaseGroup));
            return item.Resource;
        }

        public virtual async Task<HostRelease?> GetActiveReleaseAsync(string releaseGroup)
        {
            var container = await GetContainerAsync(ReleasesContainerName);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.ReleaseGroup = @group AND c.IsActive = true")
                .WithParameter("@group", releaseGroup);

            HostRelease? best = null;
            Version? bestVersion = null;

            using FeedIterator<HostRelease> iterator = container.GetItemQueryIterator<HostRelease>(query);
            while (iterator.HasMoreResults)
            {
                FeedResponse<HostRelease> response = await iterator.ReadNextAsync();
                foreach (var release in response)
                {
                    if (Version.TryParse(release.Version, out var v))
                    {
                        if (best == null || bestVersion == null || v > bestVersion)
                        {
                            best = release;
                            bestVersion = v;
                        }
                    }
                    else
                    {
                        best ??= release;
                    }
                }
            }

            return best;
        }

        public async Task<List<HostRelease>> GetReleasesAsync(string releaseGroup)
        {
            var container = await GetContainerAsync(ReleasesContainerName);

            var query = container.GetItemLinqQueryable<HostRelease>()
                .Where(r => r.ReleaseGroup == releaseGroup)
                .OrderByDescending(r => r.DateCreated);

            List<HostRelease> results = new();
            using var iterator = query.ToFeedIterator();
            while (iterator.HasMoreResults)
            {
                FeedResponse<HostRelease> response = await iterator.ReadNextAsync();
                results.AddRange(response);
            }

            return results;
        }

        public async Task<HostRelease?> GetReleaseAsync(string releaseId, string releaseGroup)
        {
            var container = await GetContainerAsync(ReleasesContainerName);
            try
            {
                var item = await container.ReadItemAsync<HostRelease>(releaseId, new PartitionKey(releaseGroup));
                return item.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<HostRelease> ActivateReleaseAsync(string releaseId, string releaseGroup)
        {
            var container = await GetContainerAsync(ReleasesContainerName);

            // Deactivate all releases in the group
            var releases = await GetReleasesAsync(releaseGroup);
            foreach (var release in releases.Where(r => r.IsActive))
            {
                List<PatchOperation> deactivateOps = new()
                {
                    PatchOperation.Set("/IsActive", false)
                };
                await container.PatchItemAsync<HostRelease>(release.Id, new PartitionKey(releaseGroup), deactivateOps);
            }

            // Activate the target release
            List<PatchOperation> activateOps = new()
            {
                PatchOperation.Set("/IsActive", true)
            };
            var response = await container.PatchItemAsync<HostRelease>(releaseId, new PartitionKey(releaseGroup), activateOps);
            return response.Resource;
        }
    }
}
