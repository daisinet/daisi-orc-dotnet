using System.Text.Json;
using Daisi.SDK.Extensions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo(IConfiguration configuration, string connectionStringConfigurationName = "Cosmo:ConnectionString")
    {
        Lazy<CosmosClient> client = new Lazy<CosmosClient>(() =>
        {
            var connectionString = configuration[connectionStringConfigurationName];
            var options = new CosmosClientOptions
            {
                UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null
                }
            };
            return new(connectionString, options);
        });

        public static string GenerateId(string prefix)
        {
            return $"{prefix}-{DateTime.UtcNow.ToString("yyMMddHHmmss")}-{StringExtensions.Random(includeNumbers: false).ToLower()}";
        }

        public CosmosClient GetCosmoClient()
        {
            return client.Value;
        }

        public Database GetDatabase()
        {
            var client = GetCosmoClient();
            return client.GetDatabase("daisi");
        }

        public async Task<Container> GetContainerAsync(string containerName)
        {
            string partitionKeyPath = "/" +
                containerName switch
                {
                    AccessKeyContainerName => AccessKeyPartitionKeyName,
                    AccountsContainerName => AccountsPartitionKeyName,
                    BlogsContainerName => BlogsPartitionKeyName,
                    HostsContainerName => HostPartitialKeyName,
                    InferencesContainerName => InferencesPartitionKeyName,
                    AppsContainerName => AppsContainerPartitionKeyName,
                    NetworksContainerName => NetworksPartitionKeyName,
                    OrcsContainerName => OrcsPartitionKeyName,
                    SkillsContainerName => SkillsPartitionKeyName,
                    SkillReviewsContainerName => SkillReviewsPartitionKeyName,
                    InstalledSkillsContainerName => InstalledSkillsPartitionKeyName,
                    ModelsContainerName => ModelsPartitionKeyName,
                    CreditsContainerName => CreditsPartitionKeyName,
                    ReleasesContainerName => ReleasesPartitionKeyName,
                    _ => "id"
                };
                    
            var container = await GetDatabase().CreateContainerIfNotExistsAsync(containerName, partitionKeyPath);          
            return container;
        }
    }
}
