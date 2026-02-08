using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string InferenceIdPrefix = "inf";
        public const string InferencesContainerName = "Inferences";
        public const string InferencesPartitionKeyName = "AccountId";

        public PartitionKey GetPartitianKey(Inference inference)
        {
            return new PartitionKey(inference.AccountId);
        }

        public async Task<Inference> Create(Inference inference)
        {
            var container = await GetContainerAsync(InferencesContainerName);
            var item = await container.CreateItemAsync(inference);
            return item.Resource;
        }
    }
}
