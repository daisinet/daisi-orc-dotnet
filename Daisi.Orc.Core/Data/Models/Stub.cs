using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Data.Models
{
    public class Stub
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        public string Name { get; set; }
    }
    public class PartitionKeyStub
    {
        [JsonPropertyName("id")]
        public string EntityId { get; set; }

        [JsonPropertyName("key")]
        public string PartitionKeyValue { get; set; }
    }
}
