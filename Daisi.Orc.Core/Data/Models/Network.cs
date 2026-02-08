using Daisi.Orc.Core.Data.Db;
using Daisi.Protos.V1;
using Daisi.SDK.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models
{
    public class Network
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.NetworksIdPrefix);
        public string AccountId { get; set; }
        public string AccountName { get; set; }

        public string Name { get; set; }
        public string Description { get; set; }

        public NetworkStatus Status { get; set;  }

        public bool IsPublic { get; set; }
    }
}
