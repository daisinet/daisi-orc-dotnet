using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string ModelsIdPrefix = "model";
        public const string ModelsContainerName = "Models";
        public const string ModelsPartitionKeyName = "id";

        public virtual async Task<DaisiModel> CreateModelAsync(DaisiModel model)
        {
            model.CreatedAt = DateTime.UtcNow;
            model.UpdatedAt = DateTime.UtcNow;

            var container = await GetContainerAsync(ModelsContainerName);
            var item = await container.CreateItemAsync(model, new PartitionKey(model.Id));
            return item.Resource;
        }

        public async Task<DaisiModel?> GetModelAsync(string modelId)
        {
            var container = await GetContainerAsync(ModelsContainerName);
            try
            {
                var item = await container.ReadItemAsync<DaisiModel>(modelId, new PartitionKey(modelId));
                return item.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public virtual async Task<List<DaisiModel>> GetAllModelsAsync()
        {
            var container = await GetContainerAsync(ModelsContainerName);
            var query = container.GetItemLinqQueryable<DaisiModel>();
            var iterator = query.ToFeedIterator();

            List<DaisiModel> results = new();
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response);
            }
            return results;
        }

        public async Task<List<DaisiModel>> GetEnabledModelsAsync()
        {
            var container = await GetContainerAsync(ModelsContainerName);
            var query = container.GetItemLinqQueryable<DaisiModel>().Where(m => m.Enabled);
            var iterator = query.ToFeedIterator();

            List<DaisiModel> results = new();
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response);
            }
            return results;
        }

        public async Task<DaisiModel> UpdateModelAsync(DaisiModel model)
        {
            model.UpdatedAt = DateTime.UtcNow;

            var container = await GetContainerAsync(ModelsContainerName);
            var item = await container.ReplaceItemAsync(model, model.Id, new PartitionKey(model.Id));
            return item.Resource;
        }

        public async Task<bool> DeleteModelAsync(string modelId)
        {
            var container = await GetContainerAsync(ModelsContainerName);
            try
            {
                await container.DeleteItemAsync<DaisiModel>(modelId, new PartitionKey(modelId));
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
        }
    }
}
