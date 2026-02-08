using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Data.Models
{
    public class Stub
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }
        public string Name { get; set; }
    }
    public class PartitionKeyStub
    {
        [JsonProperty(PropertyName = "id")]
        public string EntityId { get; set; }

        [JsonProperty(PropertyName = "key")]
        public string PartitionKeyValue { get; set; }
    }
}
