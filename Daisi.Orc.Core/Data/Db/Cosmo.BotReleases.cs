using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string BotReleaseIdPrefix = "botrel";
        public const string BotReleasesContainerName = "BotReleases";
        public const string BotReleasesPartitionKeyName = nameof(BotRelease.ReleaseGroup);

        public async Task<BotRelease> CreateBotReleaseAsync(BotRelease release)
        {
            release.DateCreated = DateTime.UtcNow;

            var container = await GetContainerAsync(BotReleasesContainerName);
            var item = await container.CreateItemAsync(release, new PartitionKey(release.ReleaseGroup));
            return item.Resource;
        }

        public virtual async Task<BotRelease?> GetActiveBotReleaseAsync(string releaseGroup)
        {
            var container = await GetContainerAsync(BotReleasesContainerName);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.ReleaseGroup = @group AND c.IsActive = true")
                .WithParameter("@group", releaseGroup);

            using FeedIterator<BotRelease> iterator = container.GetItemQueryIterator<BotRelease>(query);
            while (iterator.HasMoreResults)
            {
                FeedResponse<BotRelease> response = await iterator.ReadNextAsync();
                if (response.Count > 0)
                    return response.First();
            }

            return null;
        }

        public async Task<List<BotRelease>> GetBotReleasesAsync(string releaseGroup)
        {
            var container = await GetContainerAsync(BotReleasesContainerName);

            var query = container.GetItemLinqQueryable<BotRelease>()
                .Where(r => r.ReleaseGroup == releaseGroup)
                .OrderByDescending(r => r.DateCreated);

            List<BotRelease> results = new();
            using var iterator = query.ToFeedIterator();
            while (iterator.HasMoreResults)
            {
                FeedResponse<BotRelease> response = await iterator.ReadNextAsync();
                results.AddRange(response);
            }

            return results;
        }

        public async Task<BotRelease?> GetBotReleaseAsync(string releaseId, string releaseGroup)
        {
            var container = await GetContainerAsync(BotReleasesContainerName);
            try
            {
                var item = await container.ReadItemAsync<BotRelease>(releaseId, new PartitionKey(releaseGroup));
                return item.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<BotRelease> ActivateBotReleaseAsync(string releaseId, string releaseGroup)
        {
            var container = await GetContainerAsync(BotReleasesContainerName);

            // Deactivate all releases in the group
            var releases = await GetBotReleasesAsync(releaseGroup);
            foreach (var release in releases.Where(r => r.IsActive))
            {
                List<PatchOperation> deactivateOps = new()
                {
                    PatchOperation.Set("/IsActive", false)
                };
                await container.PatchItemAsync<BotRelease>(release.Id, new PartitionKey(releaseGroup), deactivateOps);
            }

            // Activate the target release
            List<PatchOperation> activateOps = new()
            {
                PatchOperation.Set("/IsActive", true)
            };
            var response = await container.PatchItemAsync<BotRelease>(releaseId, new PartitionKey(releaseGroup), activateOps);
            return response.Resource;
        }
    }
}
