using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string BotInstallIdPrefix = "botinst";
        public const string BotInstallsContainerName = "BotInstalls";
        public const string BotInstallsPartitionKeyName = nameof(BotInstall.AccountId);

        public async Task CreateOrUpdateBotInstallAsync(BotInstall install)
        {
            var container = await GetContainerAsync(BotInstallsContainerName);

            // Look for existing install by UserId + Platform
            var query = container.GetItemLinqQueryable<BotInstall>()
                .Where(i => i.UserId == install.UserId && i.Platform == install.Platform);

            using var iterator = query.ToFeedIterator();
            BotInstall? existing = null;
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                existing = response.FirstOrDefault();
                if (existing != null) break;
            }

            if (existing != null)
            {
                existing.Version = install.Version;
                existing.DateInstalled = DateTime.UtcNow;
                await container.UpsertItemAsync(existing, new PartitionKey(existing.AccountId));
            }
            else
            {
                install.DateInstalled = DateTime.UtcNow;
                await container.CreateItemAsync(install, new PartitionKey(install.AccountId));
            }
        }

        public async Task<int> GetBotInstallCountByVersionAsync(string version)
        {
            var container = await GetContainerAsync(BotInstallsContainerName);

            var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.Version = @version")
                .WithParameter("@version", version);

            using var iterator = container.GetItemQueryIterator<int>(query);
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                return response.FirstOrDefault();
            }

            return 0;
        }
    }
}
